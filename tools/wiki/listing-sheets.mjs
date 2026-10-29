// node tools/wiki/listing-sheets.mjs <docs-dir>
//
// The store listing's pictures, drawn from the wiki's own stills: one sheet per family, every weapon in it
// side by side and named, on the link cards' ground. Run after stills.mjs (`tools/wiki/build.py`); writes
// into listing/images/.
// PNG because Borea takes PNG or JPEG and not WebP.

import { createServer } from 'node:http';
import { copyFile, readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const [docs] = process.argv.slice(2);
if (!docs) {
  console.error('usage: node tools/wiki/listing-sheets.mjs <docs-dir>');
  process.exit(2);
}
const out = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../listing/images');

const SHEETS = {
  'sheet-guns': ['Guns', ['ciws', 'mk42', 'm197']],
  'sheet-missiles': ['Missiles', ['aim9j', 'aim120c', 'agm88']],
  'sheet-rockets': ['Rocket pods', ['lau10', 'zunipod6']],
  'sheet-sensors': ['Sights and sensors', ['optic', 'litening', 'irst']],
  'sheet-nuclear': ['Nuclear weapons', ['b61', 'mirvbus']],
};

// A family of one is its link card as it stands.
const COPIES = { 'banner.png': 'assets/og.png', 'card-pantsir.png': 'models/launcher6-card.png',
  'card-yal1.png': 'models/yal1turret-card.png' };

const TYPES = { '.svg': 'image/svg+xml', '.webp': 'image/webp', '.woff2': 'font/woff2', '.json': 'application/json' };
const escape = (text) => text.replace(/[&<>"]/g, (c) => `&#${c.charCodeAt(0)};`);
const STYLE = `
@font-face{font-family:'Saira Condensed';font-weight:800;src:url(assets/fonts/saira-condensed-800.woff2)}
@font-face{font-family:'IBM Plex Mono';font-weight:500;src:url(assets/fonts/ibm-plex-mono-500.woff2)}
body{margin:0}
.sheet{width:1200px;height:540px;box-sizing:border-box;padding:36px 48px 28px;display:flex;flex-direction:column;
  background:radial-gradient(ellipse at 50% 55%,#2a2a28 0%,#171717 70%);border-bottom:8px solid #f2a900;color:#edebe6}
.head{display:flex;justify-content:space-between;align-items:center}
.family{font:500 22px 'IBM Plex Mono';letter-spacing:.2em;color:#f2a900;text-transform:uppercase}
.brand{display:flex;align-items:center;gap:10px;font:800 24px 'Saira Condensed';letter-spacing:.04em;color:#a19d94}
.brand img{width:36px;height:36px}
.row{flex:1;display:flex;gap:24px;min-height:0}
.tile{flex:1;display:flex;flex-direction:column;align-items:center;min-width:0}
.tile img{flex:1;min-height:0;width:100%;object-fit:contain;margin-bottom:20px}
.name{font:800 34px/1 'Saira Condensed';text-transform:uppercase;text-align:center}`;

const server = createServer(async (request, response) => {
  try {
    const file = path.join(docs, decodeURIComponent(new URL(request.url, 'http://localhost').pathname));
    const body = await readFile(file);
    response.writeHead(200, { 'content-type': TYPES[path.extname(file)] ?? 'application/octet-stream' }).end(body);
  } catch {
    response.writeHead(404).end();
  }
}).listen(0, '127.0.0.1');
await new Promise((resolve) => server.once('listening', resolve));

const cards = JSON.parse(await readFile(path.join(docs, 'models/cards.json'), 'utf8'));
const browser = await chromium.launch();
try {
  const page = await browser.newPage({ viewport: { width: 1200, height: 540 } });
  // On the server's own origin: a font is fetched with CORS, which a blank page's opaque origin fails.
  await page.goto(`http://127.0.0.1:${server.address().port}/`);
  for (const [file, [family, slugs]] of Object.entries(SHEETS)) {
    const tiles = slugs.map((slug) => `<div class="tile"><img src="models/${slug}.webp">
      <div class="name">${escape(cards[slug].name)}</div></div>`).join('');
    await page.setContent(`<!doctype html><style>${STYLE}</style><div class="sheet"><div class="head">
      <div class="family">${escape(family)}</div><div class="brand"><img src="assets/mark.svg">KSARMORY</div></div>
      <div class="row">${tiles}</div></div>`);
    await page.evaluate(async () => {
      await document.fonts.load("500 22px 'IBM Plex Mono'", 'AZ');
      await document.fonts.load("800 34px 'Saira Condensed'", 'AZ');
      await Promise.all([...document.images].map((img) => img.decode()));
      // A still is framed for its viewer, with a margin a sheet of three cannot spare: crop to what is drawn.
      for (const img of document.querySelectorAll('.tile img')) {
        const canvas = Object.assign(document.createElement('canvas'), { width: img.naturalWidth, height: img.naturalHeight });
        const g = canvas.getContext('2d');
        g.drawImage(img, 0, 0);
        const { data, width, height } = g.getImageData(0, 0, canvas.width, canvas.height);
        let x0 = width, y0 = height, x1 = 0, y1 = 0;
        for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
          if (data[(y * width + x) * 4 + 3] > 8) { x0 = Math.min(x0, x); x1 = Math.max(x1, x); y0 = Math.min(y0, y); y1 = Math.max(y1, y); }
        }
        if (x1 < x0) continue;
        const pad = 12, crop = document.createElement('canvas');
        crop.width = x1 - x0 + 1 + 2 * pad;
        crop.height = y1 - y0 + 1 + 2 * pad;
        crop.getContext('2d').drawImage(canvas, x0, y0, x1 - x0 + 1, y1 - y0 + 1, pad, pad, x1 - x0 + 1, y1 - y0 + 1);
        img.src = crop.toDataURL();
        await img.decode();
      }
    });
    await page.screenshot({ path: path.join(out, `${file}.png`) });
  }
  for (const [file, from] of Object.entries(COPIES)) await copyFile(path.join(docs, from), path.join(out, file));
  console.log(`listing-sheets: ${Object.keys(SHEETS).length} drawn and ${Object.keys(COPIES).length} copied into ${out}`);
} finally {
  await browser.close();
  server.close();
}
