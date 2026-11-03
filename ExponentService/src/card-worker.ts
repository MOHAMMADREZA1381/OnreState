// Renders one card in its own short-lived process: reads the card data (JSON) from stdin and writes the PNG to stdout.
// A separate process is used because the renderer keeps several MB allocated after each image; when this process exits, the
// memory goes back to the system, so the long-running service stays small.

import { renderCardPng, sanitizeCard } from "./card.js";

const chunks: Buffer[] = [];
for await (const chunk of process.stdin) chunks.push(chunk as Buffer);

try {
  const data = sanitizeCard(JSON.parse(Buffer.concat(chunks).toString("utf8")));
  if (data === null) {
    console.error("invalid card data");
    process.exit(2);
  }
  process.stdout.write(renderCardPng(data), () => process.exit(0));
} catch (e) {
  console.error(String(e));
  process.exit(1);
}
