import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
const root = fileURLToPath(new URL('../', import.meta.url));
const ignored = new Set(['.git', 'bin', 'obj', 'artifacts', '.cache', '.vs']);
const binary = new Set(['.png', '.ico']);
const problems = [];
let count = 0;
const prohibited = [
  /-----BEGIN [^-]*PRIVATE KEY-----/,
  /(?:gh[pousr]_[A-Za-z0-9]{25,}|github_pat_[A-Za-z0-9_]{30,}|AIza[0-9A-Za-z_-]{30,})/,
  /(?:NID_AUT|NID_SES)\s*[:=]\s*["'][^"']{10,}/,
  /(?:client_secret|access_token|refresh_token)\s*["']?\s*[:=]\s*["'][^"']+["']/i,
  /\/Users\/[A-Za-z0-9_-]+\//,
  /[A-Z0-9._%+-]+@(gmail|naver|outlook|hotmail)\.[A-Z]{2,}/i,
];
function walk(directory) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    if (ignored.has(entry.name) || entry.name === '.DS_Store') continue;
    const absolute = path.join(directory, entry.name), relative = path.relative(root, absolute);
    if (entry.isSymbolicLink()) { problems.push(`${relative}: symlink`); continue; }
    if (entry.isDirectory()) { walk(absolute); continue; }
    count++;
    if (/(?:state|dashboard-snapshot|archive|backup)\.json$|\.pfx$|\.p12$|\.env(?:\.|$)|\.log$|\.exe$|\.dll$|\.dmp$/i.test(entry.name)) problems.push(`${relative}: runtime or private file`);
    if (binary.has(path.extname(entry.name))) continue;
    const text = fs.readFileSync(absolute, 'utf8');
    for (const pattern of prohibited) {
      // Preserve verified upstream .NET copyright contacts; credential patterns still apply.
      if (relative.replaceAll('\\', '/') === 'licenses/NET-Runtime-NOTICES.txt' && pattern === prohibited.at(-1)
          && crypto.createHash('sha256').update(text.replaceAll('\r\n', '\n')).digest('hex') === '66f1d4e44973185519bb4aa8a9718eb22fc7af2cc532e3ae9cfc4c127ee7fc54') continue;
      if (pattern.test(text)) problems.push(`${relative}: prohibited credential or personal identifier pattern`);
    }
  }
}
walk(root);
if (problems.length) { console.error(problems.join('\n')); process.exit(1); }
console.log(`PASS: ${count} source files checked`);
