// Share card: an image with the rank, the points and the points per platform of one wallet. The SVG template is rendered to PNG
// with resvg (no browser needed). Only the short form of the address is drawn, and nothing is stored.

import { Resvg } from "@resvg/resvg-js";
import { createRequire } from "node:module";
import path from "node:path";

export type CardData = {
  wallet: string;
  rank: number;
  totalPoints: number;
  totalUsers: number | null; // how many wallets have points, for "Top x%" (optional)
  platforms: { name: string; points: number }[];
};

const MAX_ROWS = 5;
export const WALLET = /^[1-9A-HJ-NP-Za-km-z]{32,44}$/;

// Validates the request body. Returns null when anything is off, so a bad request never reaches the renderer.
export function sanitizeCard(input: unknown): CardData | null {
  if (typeof input !== "object" || input === null) return null;
  const c = input as Record<string, unknown>;

  if (typeof c.wallet !== "string" || !WALLET.test(c.wallet)) return null;
  if (typeof c.rank !== "number" || !Number.isInteger(c.rank) || c.rank < 0 || c.rank > 100_000_000) return null;
  if (typeof c.totalPoints !== "number" || !Number.isFinite(c.totalPoints) || c.totalPoints < 0 || c.totalPoints > 1e15) return null;
  if (!Array.isArray(c.platforms) || c.platforms.length > MAX_ROWS) return null;

  let totalUsers: number | null = null;
  if (c.totalUsers !== undefined && c.totalUsers !== null) {
    if (typeof c.totalUsers !== "number" || !Number.isInteger(c.totalUsers) || c.totalUsers < 1 || c.totalUsers > 1_000_000_000) return null;
    totalUsers = c.totalUsers;
  }

  const platforms: CardData["platforms"] = [];
  for (const p of c.platforms) {
    if (typeof p !== "object" || p === null) return null;
    const { name, points } = p as Record<string, unknown>;
    if (typeof name !== "string" || name.length === 0 || name.length > 30) return null;
    if (typeof points !== "number" || !Number.isFinite(points) || points < 0 || points > 1e15) return null;
    platforms.push({ name, points });
  }
  return { wallet: c.wallet, rank: c.rank, totalPoints: c.totalPoints, totalUsers, platforms };
}

const fmt = (n: number) => Math.round(n).toLocaleString("en-US");
const esc = (s: string) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
// "Top 5.5% of 25,079 wallets" (never shown as 0%); empty when the number of wallets is unknown or the rank is 0
function topLine(c: CardData): string {
  if (c.totalUsers === null || c.rank < 1) return "";
  const percent = Math.max(0.1, Math.round((c.rank * 1000) / c.totalUsers) / 10);
  return `Top ${percent.toFixed(1)}% of ${fmt(c.totalUsers)} wallets`;
}
export const short = (wallet: string) => `${wallet.slice(0, 6)}…${wallet.slice(-4)}`;

export function buildSvg(c: CardData): string {
  const rows = c.platforms
    .map((p, i) => {
      const y = 374 + i * 42;
      const share = c.totalPoints > 0 ? Math.max(0.01, p.points / c.totalPoints) : 0.01;
      const width = Math.round(340 * Math.min(1, share));
      return `
  <text x="80" y="${y}" font-size="24" fill="#9fb0c3">${esc(p.name)}</text>
  <rect x="400" y="${y - 20}" width="340" height="12" rx="6" fill="#1c2733"/>
  <rect x="400" y="${y - 20}" width="${width}" height="12" rx="6" fill="#35d0a0"/>
  <text x="1120" y="${y}" font-size="24" fill="#e8eef5" text-anchor="end">${fmt(p.points)}</text>`;
    })
    .join("");

  return `<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="630" viewBox="0 0 1200 630">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#0b1320"/>
      <stop offset="1" stop-color="#12263a"/>
    </linearGradient>
  </defs>
  <rect width="1200" height="630" fill="url(#bg)"/>
  <rect x="40" y="40" width="1120" height="560" rx="28" fill="none" stroke="#25405a" stroke-width="2"/>

  <text x="80" y="112" font-size="32" font-weight="700" fill="#35d0a0">OnRe Rank Report</text>
  <text x="1120" y="112" font-size="24" fill="#9fb0c3" text-anchor="end">${esc(short(c.wallet))}</text>

  <text x="80" y="200" font-size="22" fill="#9fb0c3">RANK</text>
  <text x="80" y="268" font-size="68" font-weight="700" fill="#ffffff">#${fmt(c.rank)}</text>
  <text x="80" y="304" font-size="22" fill="#35d0a0">${esc(topLine(c))}</text>
  <text x="560" y="200" font-size="22" fill="#9fb0c3">TOTAL POINTS</text>
  <text x="560" y="268" font-size="68" font-weight="700" fill="#ffffff">${fmt(c.totalPoints)}</text>

  <line x1="80" y1="332" x2="1120" y2="332" stroke="#25405a" stroke-width="2"/>
  ${rows}

  <text x="80" y="584" font-size="20" fill="#6f8296">Check yours: t.me/onre_rank_bot</text>
</svg>`;
}

// The font ships with the dejavu-fonts-ttf package, so rendering does not depend on fonts installed on the server.
const fontDir = path.join(path.dirname(createRequire(import.meta.url).resolve("dejavu-fonts-ttf/package.json")), "ttf");

export function renderSvgPng(svg: string): Buffer {
  const resvg = new Resvg(svg, {
    font: {
      loadSystemFonts: false,
      fontFiles: [path.join(fontDir, "DejaVuSans.ttf"), path.join(fontDir, "DejaVuSans-Bold.ttf")],
      defaultFontFamily: "DejaVu Sans",
    },
  });
  return resvg.render().asPng();
}
