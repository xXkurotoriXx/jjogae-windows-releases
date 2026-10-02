import fs from 'node:fs';
import path from 'node:path';
const assets = new URL('../src/Jjogae.Windows/Assets/', import.meta.url);
const png = fs.readFileSync(new URL('AppIcon.png', assets));
if (png.readUInt32BE(16) !== 256 || png.readUInt32BE(20) !== 256) throw new Error('Icon must be 256 px');
// ICO wraps the unchanged 256 px PNG; no pixel conversion or repainting.
const header = Buffer.alloc(22);
header.writeUInt16LE(1, 2); header.writeUInt16LE(1, 4);
header.writeUInt16LE(1, 10); header.writeUInt16LE(32, 12);
header.writeUInt32LE(png.length, 14); header.writeUInt32LE(22, 18);
fs.writeFileSync(new URL('AppIcon.ico', assets), Buffer.concat([header, png]));
console.log('Windows icon packaged.');
