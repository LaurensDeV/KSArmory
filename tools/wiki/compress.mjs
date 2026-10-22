// node tools/wiki/compress.mjs <in-dir> <out-dir>
//
// Shrinks every .glb models.py assembled for the wiki: textures to 1024 and WebP, meshes to meshopt.
// Normal maps are lossless, because lossy WebP bands a normal map where it leaves a colour map alone.
// Nothing is simplified, joined or flattened: the subparts stay the separate, named nodes they are.

import { readdir, mkdir, writeFile, stat } from 'node:fs/promises';
import path from 'node:path';
import { Logger, NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { dedup, prune, textureCompress, meshopt } from '@gltf-transform/functions';
import { MeshoptEncoder, MeshoptDecoder } from 'meshoptimizer';
import sharp from 'sharp';

const [input, output] = process.argv.slice(2);
if (!input || !output) {
  console.error('usage: node tools/wiki/compress.mjs <in-dir> <out-dir>');
  process.exit(2);
}

await MeshoptEncoder.ready;
await MeshoptDecoder.ready;
const io = new NodeIO()
  .setLogger(new Logger(Logger.Verbosity.WARN))
  .registerExtensions(ALL_EXTENSIONS)
  .registerDependencies({ 'meshopt.encoder': MeshoptEncoder, 'meshopt.decoder': MeshoptDecoder });

await mkdir(output, { recursive: true });
let before = 0, after = 0;
for (const name of (await readdir(input)).filter((f) => f.endsWith('.glb')).sort()) {
  const source = path.join(input, name);
  const doc = await io.read(source);
  doc.setLogger(new Logger(Logger.Verbosity.WARN));
  await doc.transform(
    dedup(),
    prune(),
    textureCompress({ encoder: sharp, targetFormat: 'webp', resize: [1024, 1024], slots: /^(?!normalTexture$)/, quality: 85 }),
    textureCompress({ encoder: sharp, targetFormat: 'webp', resize: [1024, 1024], slots: /^normalTexture$/, lossless: true }),
    meshopt({ encoder: MeshoptEncoder, level: 'medium' }),
  );
  const bytes = await io.writeBinary(doc);
  await writeFile(path.join(output, name), bytes);
  before += (await stat(source)).size;
  after += bytes.length;
}
console.log(`compress: ${(before / 1e6).toFixed(1)} MB to ${(after / 1e6).toFixed(2)} MB`);
