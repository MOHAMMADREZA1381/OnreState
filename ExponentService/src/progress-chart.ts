import { short, WALLET } from "./card.js";

type Day = { date: string; points: number | null; rank: number | null };
type ProgressData = { wallet: string; days: Day[] };
const DAY_MS = 86_400_000;

export function sanitizeProgress(input: unknown): ProgressData | null {
  if (typeof input !== "object" || input === null) return null;
  const { wallet, days } = input as Record<string, unknown>;
  if (typeof wallet !== "string" || !WALLET.test(wallet)) return null;
  if (!Array.isArray(days) || days.length !== 7) return null;

  const clean: Day[] = [];
  let previous = 0;
  for (const day of days) {
    if (typeof day !== "object" || day === null) return null;
    const { date, points, rank } = day as Record<string, unknown>;
    if (typeof date !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(date)) return null;
    const time = Date.parse(date);
    if (!Number.isFinite(time) || new Date(time).toISOString().slice(0, 10) !== date) return null;
    if (clean.length && time - previous !== DAY_MS) return null;
    previous = time;

    if (points === null && rank === null) {
      clean.push({ date, points, rank });
    } else {
      if (typeof points !== "number" || !Number.isFinite(points) || points < 0) return null;
      if (typeof rank !== "number" || !Number.isInteger(rank) || rank < 1 || rank > 2_147_483_647) return null;
      clean.push({ date, points, rank });
    }
  }
  return clean.some(day => day.rank !== null) ? { wallet, days: clean } : null;
}

const mint = "#79E7BD";
const orange = "#E5A575";
const muted = "#A1BAB5";
const x = (index: number) => 218 + index * 143;
const pointFormat = new Intl.NumberFormat("en-US", { notation: "compact", maximumSignificantDigits: 5 });
const rankFormat = new Intl.NumberFormat("en-US", { maximumFractionDigits: 1 });
const dateFormat = new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", timeZone: "UTC" });

function panel(days: Day[], metric: "points" | "rank", top: number, color: string): string {
  const values = days.map(day => day[metric]);
  const present = values.filter(value => value !== null);
  const min = Math.min(...present);
  const max = Math.max(...present);
  const isRank = metric === "rank";
  const title = isRank ? "Rank" : "Points";
  const format = (value: number) => isRank ? `#${rankFormat.format(value)}` : pointFormat.format(value);
  const plotTop = top + 82;
  const height = 114;
  const y = (value: number) => {
    if (max === min) return plotTop + height / 2;
    const fraction = (value - min) / (max - min);
    return plotTop + height * (isRank ? fraction : 1 - fraction);
  };

  const grid = (max === min ? [min] : [min, max]).map(value => `
    <text x="190" y="${y(value) + 6}" font-size="18" fill="${muted}" text-anchor="end">${format(value)}</text>
    <path d="M 218 ${y(value)} H 1076" stroke="#31534E"/>`).join("");
  const marks = values.map((value, i) => {
    if (value === null) return "";
    const previous = values[i - 1];
    const line = previous == null ? "" :
      `<path d="M ${x(i - 1)} ${y(previous)} L ${x(i)} ${y(value)}" fill="none" stroke="${color}" stroke-width="4" stroke-linecap="round"/>`;
    return `${line}<circle cx="${x(i)}" cy="${y(value)}" r="5.5" fill="${color}"/>`;
  }).join("");
  const latest = present.at(-1)!;
  const change = isRank ? present[0] - latest : latest - present[0];
  const changeText = present.length === 1 ? "1 recorded day" : isRank
    ? change === 0 ? "No rank change" : `${rankFormat.format(Math.abs(change))} places ${change > 0 ? "up" : "down"}`
    : `${change >= 0 ? "+" : "−"}${pointFormat.format(Math.abs(change))} since first point`;
  const dates = days.map((day, i) => `
    <text x="${x(i)}" y="${top + 237}" font-size="18" text-anchor="middle" fill="${day.rank === null ? "#76908A" : muted}">
      ${dateFormat.format(new Date(day.date))}
    </text>`).join("");

  return `<rect x="48" y="${top}" width="1104" height="261" rx="21" fill="#133632" stroke="#284C46"/>
    <text x="76" y="${top + 49}" font-size="27" font-weight="700" fill="${color}">${title}</text>
    <text x="1124" y="${top + 48}" font-size="20" text-anchor="end" fill="#E5F4EC">
      ${format(latest)} latest · ${changeText}
    </text>
    ${grid}${marks}${dates}`;
}

export function buildProgressSvg(data: ProgressData): string {
  const count = data.days.filter(day => day.rank !== null).length;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="770" viewBox="0 0 1200 770">
    <rect width="1200" height="770" fill="#0C2926"/>
    <text x="48" y="56" font-size="21" font-weight="700" fill="${mint}">ONRE / TIME MACHINE</text>
    <text x="1152" y="56" font-size="20" text-anchor="end" fill="${muted}">${short(data.wallet)}</text>
    <text x="48" y="111" font-size="47" font-weight="700" fill="#E5F4EC">My Progress Chart</text>
    <text x="48" y="149" font-size="21" fill="${muted}">7 days · UTC · ${count}/7 recorded</text>
    ${panel(data.days, "points", 177, mint)}
    ${panel(data.days, "rank", 456, orange)}
    <text x="48" y="749" font-size="19" fill="${muted}">Latest snapshot per UTC day · Gaps indicate missing data · Better ranks appear higher</text>
  </svg>`;
}
