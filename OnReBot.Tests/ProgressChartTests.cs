using System.Net;
using System.Text.Json;
using OnReBot.Clients;
using OnReBot.Models;
using OnReBot.Services;

namespace OnReBot.Tests;

public class ProgressChartTests : IDisposable
{
    private static readonly string Wallet = new('A', 44);
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"onre-progress-{Guid.NewGuid()}");

    [Fact]
    public void Chart_UsesSevenUtcDaysAndLatestSnapshotOfEachDay()
    {
        var writer = new SnapshotStore(_dir);
        Save(writer, Now.AddDays(-7), 100, 1);
        Save(writer, Now.AddDays(-6).AddHours(-11), 200, 30);
        Save(writer, Now.AddDays(-6), 240, 25);
        Save(writer, Now.AddDays(-3), 300, 22);
        Save(writer, Now.AddDays(-3).AddHours(1)); // Wallet no longer appears in this day's final snapshot.
        Save(writer, Now.AddDays(-1), 450, 18);
        Save(writer, Now.AddHours(-1), 470, 19);
        Save(writer, Now.AddHours(1), 500, 12);

        // Open a fresh store so the test covers existing gzip files as well as the selector.
        var chart = TimeMachine.GetProgressChart(new SnapshotStore(_dir), Wallet, Now.ToOffset(TimeSpan.FromHours(13)));

        Assert.Equal(Wallet, chart.Wallet);
        Assert.True(chart.HasData);
        Assert.Equal(Enumerable.Range(4, 7).Select(day => new DateOnly(2026, 11, day)), chart.Days.Select(day => day.Date));
        Assert.Equal(new decimal?[] { 240m, null, null, null, null, 450m, 470m }, chart.Days.Select(day => day.Points));
        Assert.Equal(new int?[] { 25, null, null, null, null, 18, 19 }, chart.Days.Select(day => day.Rank));
    }

    [Fact]
    public void Chart_FallsBackToEarlierReadableSnapshotAndKeepsAnIsolatedPoint()
    {
        var store = new SnapshotStore(_dir);
        Save(store, Now.AddHours(-2), 0, 77);
        File.WriteAllText(Path.Combine(_dir, "20261110T1100Z.snap.gz"), "damaged");

        var chart = TimeMachine.GetProgressChart(store, Wallet, Now);

        Assert.True(chart.HasData);
        Assert.Equal(new ProgressDay(new DateOnly(2026, 11, 10), 0, 77), chart.Days[^1]);
        Assert.All(chart.Days.Take(6), day => Assert.Null(day.Rank));
    }

    [Fact]
    public void Chart_HasNoDataForUnlistedWalletOrExpiredHistory()
    {
        var store = new SnapshotStore(_dir);
        Save(store, Now.AddDays(-7), 100, 40);
        Save(store, Now.AddDays(-1));

        var chart = TimeMachine.GetProgressChart(store, Wallet, Now);

        Assert.False(chart.HasData);
        Assert.Equal(7, chart.Days.Count);
        Assert.All(chart.Days, day => Assert.Null(day.Points));
    }

    [Fact]
    public void SummaryButton_RoundTripsWalletAndRejectsInvalidCallbacks()
    {
        var button = Assert.Single(ReportButtons.Keyboard(Wallet).InlineKeyboard.SelectMany(row => row),
            button => button.Text == "📈 My Progress Chart");
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(button.CallbackData!) <= 64);
        Assert.True(ReportButtons.TryParseProgress(button.CallbackData, out var wallet));
        Assert.Equal(Wallet, wallet);
        Assert.False(ReportButtons.TryParseProgress("progress:not-a-wallet", out _));
        Assert.False(ReportButtons.TryParseProgress(ReportButtons.CardData(Wallet), out _));
    }

    [Fact]
    public async Task RendererRequest_PreservesDatesAndGaps()
    {
        var store = new SnapshotStore(_dir);
        Save(store, Now, 123.45m, 8);
        var chart = TimeMachine.GetProgressChart(store, Wallet, Now);
        using var http = new HttpClient(new RendererHandler());

        var png = await new CardClient(http, "http://localhost:3001/").RenderProgressAsync(chart, CancellationToken.None);

        Assert.Equal(new byte[] { 137, 80, 78, 71 }, png);
    }

    private sealed class RendererHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/progress-chart", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal(Wallet, body.RootElement.GetProperty("wallet").GetString());
            var days = body.RootElement.GetProperty("days");
            Assert.Equal(7, days.GetArrayLength());
            Assert.Equal(JsonValueKind.Null, days[0].GetProperty("points").ValueKind);
            Assert.Equal(JsonValueKind.Null, days[0].GetProperty("rank").ValueKind);
            Assert.Equal("2026-11-10", days[6].GetProperty("date").GetString());
            Assert.Equal(123.45m, days[6].GetProperty("points").GetDecimal());
            Assert.Equal(8, days[6].GetProperty("rank").GetInt32());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([137, 80, 78, 71]) };
        }
    }

    private static void Save(SnapshotStore store, DateTimeOffset at, decimal points = 100, int? rank = null) =>
        store.Save(new Snapshot
        {
            At = at,
            Entries = rank is int value ? [new SnapshotEntry(value, Wallet, points)] : []
        });

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
