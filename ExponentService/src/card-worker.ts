// Renders one card in its own short-lived process: reads the card data (JSON) from stdin and writes the PNG to stdout.
// A separate process is used because the renderer keeps several MB allocated after each image; when this process exits, the
// memory goes back to the system, so the long-running service stays small.

import { buildSvg, renderSvgPng, sanitizeCard } from "./card.js";
import { buildProgressSvg, sanitizeProgress } from "./progress-chart.js";

const chunks: Buffer[] = [];
for await (const chunk of process.stdin) chunks.push(chunk as Buffer);

try {
  const payload: unknown = JSON.parse(Buffer.concat(chunks).toString("utf8"));
  const svg = process.argv[2] === "/progress-chart"
    ? buildProgress()
    : buildCard();
  if (svg === null) {
    console.error("invalid image data");
    process.exit(2);
  }
  process.stdout.write(renderSvgPng(svg), () => process.exit(0));

  function buildProgress() {
    const data = sanitizeProgress(payload);
    return data && buildProgressSvg(data);
  }

  function buildCard() {
    const data = sanitizeCard(payload);
    return data && buildSvg(data);
  }
} catch (e) {
  console.error(String(e));
  process.exit(1);
}
