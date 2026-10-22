// node tools/wiki/branding.mjs
//
// The brand's PNGs, rendered from its sources in branding/: the lockup for the README in both of GitHub's
// themes, the mod icon, and the patch. A browser rather than an image library, because the lockup and the
// patch are set in Saira Condensed and IBM Plex Mono, which come from this folder's npm packages.

import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const here = path.dirname(fileURLToPath(import.meta.url));
const branding = path.resolve(here, '../../branding');
const fonts = path.join(here, 'node_modules/@fontsource');

// Embedded rather than linked: a page set from a string may not load a file:// font, and falls back unasked.
const face = async (family, file, weight) => {
  const data = (await readFile(path.join(fonts, file))).toString('base64');
  return `@font-face{font-family:'${family}';font-weight:${weight};src:url(data:font/woff2;base64,${data}) format('woff2')}`;
};
const FONTS = (await Promise.all([
  face('Saira Condensed', 'saira-condensed/files/saira-condensed-latin-800-normal.woff2', 800),
  face('IBM Plex Mono', 'ibm-plex-mono/files/ibm-plex-mono-latin-500-normal.woff2', 500),
])).join('');

const svg = async (name) => (await readFile(path.join(branding, name), 'utf8'));

// The primary lockup, as the design sets it: mark, wordmark with KS in the accent and A in the tint, a rule,
// and the tagline. The light one is the light mark's colours.
const lockup = (mark, { ink, accent, tint, muted }) => `
<div style="display:flex;align-items:center;gap:56px;padding:40px">
  <div style="width:300px;height:300px">${mark.replace('<svg ', '<svg width="300" height="300" ')}</div>
  <div style="display:flex;flex-direction:column;gap:6px;font-family:'Saira Condensed'">
    <div style="font-size:164px;line-height:.9;font-weight:800;letter-spacing:.02em;color:${ink}"><span style="color:${accent}">KS</span><span style="color:${tint}">A</span>RMORY</div>
    <div style="height:4px;width:100%;background:${ink};opacity:.18"></div>
    <div style="font-family:'IBM Plex Mono';font-weight:500;font-size:19px;letter-spacing:.24em;color:${muted};padding-top:10px">WEAPON SYSTEMS FOR KITTEN SPACE AGENCY</div>
  </div>
</div>`;

const icon = (mark) => `
<div style="width:512px;height:512px;border-radius:112px;background:#171717;display:flex;align-items:center;justify-content:center">
  ${mark.replace('<svg ', '<svg width="392" height="392" ')}
</div>`;

const patch = (art) => `<div style="width:1024px;height:1024px">${art.replace('<svg ', '<svg width="1024" height="1024" ')}</div>`;

const browser = await chromium.launch();
try {
  const page = await browser.newPage({ deviceScaleFactor: 1 });
  const shoot = async (name, body) => {
    await page.setContent(`<!doctype html><style>${FONTS}body{margin:0;background:transparent}#x{display:inline-block}</style><div id="x">${body}</div>`);
    const loaded = await page.evaluate(async () => {
      // A face no text on the page uses is never fetched, so each is loaded by hand before it is checked.
      await Promise.all([...document.fonts].map((font) => font.load()));
      return document.fonts.check("800 20px 'Saira Condensed'") && document.fonts.check("500 20px 'IBM Plex Mono'");
    });
    if (!loaded) throw new Error(`${name}: the brand fonts did not load, so it would be set in a fallback`);
    await page.locator('#x').screenshot({ path: path.join(branding, name), omitBackground: true });
  };

  await shoot('logo.png', lockup(await svg('mark.svg'),
    { ink: '#EDEBE6', accent: '#F2A900', tint: '#F0CA73', muted: '#A19D94' }));
  await shoot('logo-light.png', lockup(await svg('mark-light.svg'),
    { ink: '#171717', accent: '#C98500', tint: '#B8892E', muted: '#5F5C56' }));
  await shoot('icon.png', icon(await svg('mark.svg')));
  await shoot('patch.png', patch(await svg('patch.svg')));
  console.log('branding: logo.png, logo-light.png, icon.png and patch.png written');
} finally {
  await browser.close();
}
