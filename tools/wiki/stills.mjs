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
const TYPES = { '.js': 'text/javascript', '.glb': 'model/gltf-binary', '.html': 'text/html' };
const PAGE = `<!doctype html><script src="/assets/3d/config.js"></script>
<script type="module" src="/assets/3d/model-viewer.min.js"></script>
<style>body{margin:0;background:transparent}model-viewer{width:${WIDTH}px;height:${HEIGHT}px}</style>
<model-viewer id="mv" camera-orbit="35deg 70deg auto" environment-image="neutral" shadow-intensity="0.6"
  interaction-prompt="none"></model-viewer>`;

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
} finally {
  await browser.close();
  server.close();
}
if (failures.length) {
  console.error(`stills: the page raised ${failures.join('; ')}`);
  process.exit(1);
}
