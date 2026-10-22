// node tools/wiki/stills.mjs <docs-dir>
//
// A still of every model in <docs-dir>/models, written beside it as <name>.webp: the gallery's pictures and
// each viewer's poster. Rendered by the same viewer, at the same camera, that shows the model on its page,
// so a still and the model it stands for cannot disagree. Headless Chromium has no GPU, so it draws WebGL on
// SwiftShader, which it no longer falls back to unasked.

import { createServer } from 'node:http';
import { readFile, readdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { chromium } from 'playwright';

const [docs] = process.argv.slice(2);
if (!docs) {
  console.error('usage: node tools/wiki/stills.mjs <docs-dir>');
  process.exit(2);
}

const WIDTH = 640, HEIGHT = 480;
const TYPES = { '.js': 'text/javascript', '.glb': 'model/gltf-binary', '.html': 'text/html', '.webp': 'image/webp',
  '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };
const PAGE = `<!doctype html><script src="/assets/3d/config.js"></script>
<script type="module" src="/assets/3d/model-viewer.min.js"></script>
<style>body{margin:0;background:transparent}model-viewer{width:${WIDTH}px;height:${HEIGHT}px}</style>
<model-viewer id="mv" camera-orbit="35deg 70deg auto" environment-image="neutral" shadow-intensity="0.6"
  interaction-prompt="none"></model-viewer>`;

const escape = (text) => text.replace(/[&<>"]/g, (c) => `&#${c.charCodeAt(0)};`);
const CARD_STYLE = `
@font-face{font-family:'Saira Condensed';font-weight:800;src:url(assets/fonts/saira-condensed-800.woff2)}
@font-face{font-family:'IBM Plex Mono';font-weight:500;src:url(assets/fonts/ibm-plex-mono-500.woff2)}
body{margin:0}
.card{width:1200px;height:630px;box-sizing:border-box;display:flex;align-items:center;padding:0 72px;gap:32px;
  background:radial-gradient(ellipse at 72% 55%,#2a2a28 0%,#171717 62%);border-bottom:8px solid #f2a900;color:#edebe6}
.text{flex:0 0 470px}
.family{font:500 22px 'IBM Plex Mono';letter-spacing:.2em;color:#f2a900;text-transform:uppercase}
.name{font:800 76px/0.95 'Saira Condensed';text-transform:uppercase;margin:18px 0 40px}
.brand{display:flex;align-items:center;gap:14px;font:800 30px 'Saira Condensed';letter-spacing:.04em;color:#a19d94}
.brand img{width:48px;height:48px}
.still{flex:1;min-width:0;height:560px;object-fit:contain}
.site{justify-content:center;gap:56px}
.mark{width:250px;height:250px}
.word{font:800 150px/0.9 'Saira Condensed';letter-spacing:.02em}.k{color:#f2a900}.a{color:#f0ca73}
.rule{height:5px;background:#edebe6;opacity:.18;margin:10px 0 14px}
.tag{font:500 20px 'IBM Plex Mono';letter-spacing:.24em;color:#a19d94}`;

const server = createServer(async (request, response) => {
  const url = new URL(request.url, 'http://localhost');
  if (url.pathname === '/') {
    response.writeHead(200, { 'content-type': 'text/html' }).end(PAGE);
    return;
  }
  try {
    const file = path.join(docs, decodeURIComponent(url.pathname));
    response.writeHead(200, { 'content-type': TYPES[path.extname(file)] ?? 'application/octet-stream' })
      .end(await readFile(file));
  } catch {
    response.writeHead(404).end();
  }
}).listen(0, '127.0.0.1');
await new Promise((resolve) => server.once('listening', resolve));
const origin = `http://127.0.0.1:${server.address().port}`;

const browser = await chromium.launch({ args: ['--enable-unsafe-swiftshader', '--use-angle=swiftshader'] });
const failures = [];
try {
  const page = await browser.newPage({ viewport: { width: WIDTH, height: HEIGHT } });
  page.on('pageerror', (error) => failures.push(error.message));
  await page.goto(origin);
  await page.evaluate(() => customElements.whenDefined('model-viewer'));

  const models = (await readdir(path.join(docs, 'models'))).filter((f) => f.endsWith('.glb')).sort();
  for (const model of models) {
    const bytes = await page.evaluate(async (src) => {
      const viewer = document.getElementById('mv');
      await new Promise((resolve, reject) => {
        viewer.addEventListener('load', resolve, { once: true });
        viewer.addEventListener('error', (event) => reject(new Error(JSON.stringify(event.detail))), { once: true });
        viewer.src = src;
      });
      await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
      const blob = await viewer.toBlob({ mimeType: 'image/webp', qualityArgument: 0.9 });
      return Array.from(new Uint8Array(await blob.arrayBuffer()));
    }, `/models/${model}`);
    await writeFile(path.join(docs, 'models', model.replace(/\.glb$/, '.webp')), Buffer.from(bytes));
  }
  console.log(`stills: ${models.length} rendered`);

  // A link preview's picture: each weapon's still on the brand's ground, and one for the site itself.
  const cards = JSON.parse(await readFile(path.join(docs, 'models/cards.json'), 'utf8'));
  const card = await browser.newPage({ viewport: { width: 1200, height: 630 } });
  // On the server's own origin: a font is fetched with CORS, which a blank page's opaque origin fails.
  await card.goto(origin);
  const draw = async (body, file) => {
    await card.setContent(`<!doctype html><style>${CARD_STYLE}</style>${body}`);
    await card.evaluate(async () => {
      await Promise.all([...document.fonts].map((font) => font.load()));
      await Promise.all([...document.images].map((img) => img.decode()));
    });
    await card.screenshot({ path: path.join(docs, file) });
  };
  for (const [slug, { name, family }] of Object.entries(cards)) {
    await draw(`<div class="card"><div class="text"><div class="family">${escape(family)}</div>
      <div class="name">${escape(name)}</div><div class="brand"><img src="assets/mark.svg">KSARMORY</div></div>
      <img class="still" src="models/${slug}.webp"></div>`, `models/${slug}-card.png`);
  }
  await draw(`<div class="card site"><img class="mark" src="assets/mark.svg"><div>
    <div class="word"><span class="k">KS</span><span class="a">A</span>RMORY</div><div class="rule"></div>
    <div class="tag">WEAPON SYSTEMS FOR KITTEN SPACE AGENCY</div></div></div>`, 'assets/og.png');
  console.log(`stills: ${Object.keys(cards).length + 1} link preview cards drawn`);
} finally {
  await browser.close();
  server.close();
}
if (failures.length) {
  console.error(`stills: the page raised ${failures.join('; ')}`);
  process.exit(1);
}
