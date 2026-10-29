using OnReBot.Services;

namespace OnReBot.Tests;

public class AddressValidatorTests
{
    [Theory]
    [InlineData("TestWa11etAddressForUnitTestsJust1111")]
    [InlineData("AnotherWa11etAddressForUnitTestsJust11")]
    [InlineData("11111111111111111111111111111111")]
    public void Valid(string address) => Assert.True(AddressValidator.IsValid(address));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0estWa11etAddressForUnitTestsJust1111")]      // 0
    [InlineData("OestWa11etAddressForUnitTestsJust1111")]      // O
    [InlineData("IestWa11etAddressForUnitTestsJust1111")]      // I
    [InlineData("lestWa11etAddressForUnitTestsJust1111")]      // l
    [InlineData("TestWa11etAddressForUnitTestsJust1111 ")]     // space
    [InlineData("1111111111111111111111111111111")]             // 31 chars
    [InlineData("111111111111111111111111111111111111111111111")] // 45 chars
    public void Invalid(string? address)
    {
        Assert.False(AddressValidator.IsValid(address));
    }
}
