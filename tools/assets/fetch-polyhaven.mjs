// Downloads the CC0 Poly Haven assets listed in polyhaven-assets.json into
// MultiTravelValizChallenge/Assets/MultiTravel/ThirdParty/PolyHaven/<id>/ (1K resolution).
// Re-runnable: existing files are skipped. Licence: CC0 (https://polyhaven.com/license).
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '..', '..');
const dest = path.join(repo, 'MultiTravelValizChallenge', 'Assets', 'MultiTravel', 'ThirdParty', 'PolyHaven');
const list = JSON.parse(fs.readFileSync(path.join(here, 'polyhaven-assets.json'), 'utf8'));
const RES = '1k';

async function json(url) {
  const r = await fetch(url, { headers: { 'User-Agent': 'MultiTravelValizChallenge-asset-fetch' } });
  if (!r.ok) throw new Error(`${url}: HTTP ${r.status}`);
  return r.json();
}

async function download(url, file) {
  if (fs.existsSync(file) && fs.statSync(file).size > 0) return 0;
  fs.mkdirSync(path.dirname(file), { recursive: true });
  const r = await fetch(url);
  if (!r.ok) throw new Error(`${url}: HTTP ${r.status}`);
  const buf = Buffer.from(await r.arrayBuffer());
  fs.writeFileSync(file, buf);
  return buf.length;
}

let total = 0;
const credits = [];
for (const id of list.models) {
  const files = await json(`https://api.polyhaven.com/files/${id}`);
  const fbx = files.fbx?.[RES]?.fbx;
  if (!fbx) throw new Error(`${id}: no ${RES} FBX`);
  total += await download(fbx.url, path.join(dest, id, `${id}.fbx`));
  for (const [rel, inc] of Object.entries(fbx.include || {})) {
    total += await download(inc.url, path.join(dest, id, rel));
  }
  credits.push(id);
  console.log('model', id);
}
const MAPS = { Diffuse: 'diff', nor_gl: 'nor_gl', Rough: 'rough', arm: 'arm', AO: 'ao' };
for (const id of list.textures) {
  const files = await json(`https://api.polyhaven.com/files/${id}`);
  for (const [key, suffix] of Object.entries(MAPS)) {
    const entry = files[key]?.[RES]?.jpg || files[key]?.[RES]?.png;
    if (!entry) continue;
    const ext = path.extname(new URL(entry.url).pathname);
    total += await download(entry.url, path.join(dest, 'Textures', id, `${id}_${suffix}_${RES}${ext}`));
  }
  credits.push(id);
  console.log('texture', id);
}
console.log(`downloaded ${(total / 1048576).toFixed(1)} MB`);
