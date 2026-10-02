import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

export const repository = 'xXkurotoriXx/jjogae-windows-releases';
export const releaseAssets = Object.freeze(['JjogaeStatus.exe', 'SHA256SUMS.txt', 'THIRD_PARTY_NOTICES.md']);
const apiRoot = `https://api.github.com/repos/${repository}`;
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
function requireValue(condition, message) { if (!condition) throw Error(message); }

export function versionParts(value) {
  if (typeof value !== 'string' || !/^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.test(value)) return null;
  const parts = value.split('.').map(Number);
  return parts.every(Number.isSafeInteger) ? parts : null;
}
export function compareVersions(left, right) {
  const a = versionParts(left), b = versionParts(right);
  requireValue(a && b, 'Invalid release version');
  for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return a[i] < b[i] ? -1 : 1;
  return 0;
}
function releaseVersion(release) {
  return typeof release?.tag_name === 'string' && release.tag_name.startsWith('v')
    && versionParts(release.tag_name.slice(1)) ? release.tag_name.slice(1) : null;
}
function stable(release) {
  return release?.draft === false && release?.prerelease === false && typeof release?.published_at === 'string';
}
export function olderRelease(release, version, currentId) {
  const candidate = releaseVersion(release);
  return Number.isSafeInteger(release?.id) && release.id > 0 && release.id !== currentId
    && stable(release) && candidate !== null && compareVersions(candidate, version) < 0;
}

function windowsFileVersion(filename, environment) {
  const childEnvironment = { ...environment, JJOGAE_RELEASE_EXE: path.resolve(filename) };
  delete childEnvironment.GH_TOKEN; delete childEnvironment.GITHUB_TOKEN;
  const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command',
    "$ErrorActionPreference='Stop'; [System.Diagnostics.FileVersionInfo]::GetVersionInfo($env:JJOGAE_RELEASE_EXE).FileVersion"],
  { encoding: 'utf8', env: childEnvironment });
  requireValue(!result.error && result.status === 0, 'Windows executable version inspection failed');
  return result.stdout.trim();
}

export async function publishRelease({ directory = '.', environment = process.env, fetchImpl = globalThis.fetch,
  now = () => new Date(), readFileVersion = filename => windowsFileVersion(filename, environment) } = {}) {
  const root = path.resolve(directory), files = path.join(root, 'artifacts', 'release');
  const read = name => fs.readFileSync(path.join(root, name));
  const version = read('Directory.Build.props').toString('utf8').match(/<Version>([^<]+)<\/Version>/)?.[1];
  requireValue(versionParts(version), 'A stable three-part app version is required');
  const tag = `v${version}`;
  requireValue(environment.GITHUB_ACTIONS === 'true' && environment.GITHUB_REPOSITORY === repository
    && environment.GITHUB_REF === `refs/tags/${tag}` && environment.GH_TOKEN, 'Trusted repository tag workflow required');
  requireValue(/^[a-f0-9]{40}$/i.test(environment.GITHUB_SHA ?? '') && /^[1-9]\d*$/.test(environment.GITHUB_RUN_ID ?? ''),
    'Source commit and verification run identity are required');
  const bytesByName = new Map(releaseAssets.map(name => [name,
    name === 'THIRD_PARTY_NOTICES.md' ? read(name) : fs.readFileSync(path.join(files, name))]));
  const exe = bytesByName.get('JjogaeStatus.exe'), sha256 = hash(exe);
  requireValue(exe.length > 64 && exe.length <= 256 * 1024 * 1024 && exe.subarray(0, 2).toString() === 'MZ',
    'Expected a Windows executable');
  const pe = exe.readUInt32LE(0x3c);
  requireValue(pe >= 64 && pe <= exe.length - 6 && exe.toString('ascii', pe, pe + 4) === 'PE\0\0'
    && exe.readUInt16LE(pe + 4) === 0x8664, 'Expected an x64 Windows executable');
  requireValue((await readFileVersion(path.join(files, 'JjogaeStatus.exe'))) === `${version}.0`,
    'Executable version does not match the release tag');
  requireValue(bytesByName.get('SHA256SUMS.txt').toString('utf8').trim().replace(/^\uFEFF/, '') === `${sha256}  JjogaeStatus.exe`,
    'CI executable checksum mismatch');
  requireValue(bytesByName.get('THIRD_PARTY_NOTICES.md').toString('utf8').trim().length > 0, 'Component notices are required');
  for (const name of ['smoke-test.txt', 'parity-test.txt']) {
    const report = fs.readFileSync(path.join(files, name), 'utf8');
    const checks = report.replace(/^\uFEFF/, '').split(/\r?\n/).map(line => line.trim()).filter(Boolean);
    const passed = checks.filter(line => /^PASS:\s+\S/.test(line));
    const renderDetails = checks.filter(line => name === 'smoke-test.txt'
      && /^Rendered desktop: [1-9]\d* x [1-9]\d*; narrow: 920 x 720; additional 150% bitmap render\.$/.test(line));
    requireValue(passed.length > 0 && renderDetails.length <= 1 && passed.length + renderDetails.length === checks.length,
      `CI report failed: ${name}`);
  }
  const notes = read('RELEASE_NOTES.md').toString('utf8').replace(/^#[^\n]*\n+/, '').trim();
  requireValue(notes.length > 0, 'Release notes required');
  const metadata = { version, platform: 'windows-x64', publishedAt: now().toISOString(),
    downloadUrl: `https://github.com/${repository}/releases/download/${tag}/JjogaeStatus.exe`,
    checksumsUrl: `https://github.com/${repository}/releases/download/${tag}/SHA256SUMS.txt`, size: exe.length, sha256 };

  async function request(route, { method = 'GET', json, bytes, anonymous = false, allowMissing = false } = {}) {
    const url = route.startsWith('https://uploads.github.com/') ? route : `${apiRoot}${route}`;
    if (url.startsWith('https://uploads.github.com/'))
      requireValue(url.startsWith(`https://uploads.github.com/repos/${repository}/releases/`), 'Unexpected asset upload destination');
    const headers = { Accept: 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28',
      'Content-Type': bytes ? 'application/octet-stream' : 'application/json', 'Cache-Control': 'no-cache' };
    if (!anonymous) headers.Authorization = `Bearer ${environment.GH_TOKEN}`;
    const response = await fetchImpl(url, { method, headers, body: json ? JSON.stringify(json) : bytes,
      signal: AbortSignal.timeout(120000) });
    if (allowMissing && response.status === 404) return null;
    requireValue(response.ok, `GitHub ${method} failed HTTP ${response.status}`);
    return response.status === 204 ? null : response.json();
  }
  async function pages(route) {
    const result = [], seen = new Set();
    for (let page = 1; page <= 1000; page++) {
      const items = await request(`${route}?per_page=100&page=${page}`);
      requireValue(Array.isArray(items) && items.length <= 100, 'Unexpected paginated GitHub response');
      for (const item of items) {
        requireValue(Number.isSafeInteger(item.id) && item.id > 0 && !seen.has(item.id), 'Duplicate or invalid GitHub object identity');
        seen.add(item.id); result.push(item);
      }
      if (items.length < 100) return result;
    }
    throw Error('GitHub pagination did not finish');
  }
  function assertNoNewer(releases) {
    requireValue(!releases.some(item => stable(item) && releaseVersion(item)
      && compareVersions(releaseVersion(item), version) > 0), 'A newer stable release already exists');
  }
  function assertStable(release, id) {
    requireValue(release?.id === id && release.tag_name === tag && stable(release), 'Stable release verification failed');
  }
  async function verifyAssets(id) {
    const assets = await pages(`/releases/${id}/assets`);
    requireValue(assets.length === releaseAssets.length, 'Unexpected user-facing release assets');
    for (const name of releaseAssets) {
      const asset = assets.find(item => item.name === name), expected = bytesByName.get(name);
      requireValue(asset?.state === 'uploaded' && asset.size === expected.length && asset.digest === `sha256:${hash(expected)}`,
        `Published asset verification failed: ${name}`);
    }
  }
  function feedContent(item) {
    requireValue(item?.encoding === 'base64' && typeof item.content === 'string' && typeof item.sha === 'string',
      'Update feed response is invalid');
    return JSON.parse(Buffer.from(item.content, 'base64').toString('utf8'));
  }

  const repo = await request('');
  requireValue(repo?.private === false && repo.full_name === repository, 'Update repository must remain public');
  const existing = await pages('/releases');
  requireValue(!existing.some(item => item.tag_name === tag), 'Release version already exists; do not replace a stable release');
  assertNoNewer(existing);
  let ref = await request(`/git/ref/tags/${tag}`);
  for (let depth = 0; ref?.object?.type === 'tag' && depth < 5; depth++) ref = await request(`/git/tags/${ref.object.sha}`);
  requireValue(ref?.object?.type === 'commit' && ref.object.sha === environment.GITHUB_SHA, 'Release tag does not identify the verified source');
  const release = await request('/releases', { method: 'POST', json: { tag_name: tag, target_commitish: environment.GITHUB_SHA,
    name: `쪼개 상황실 ${version} · Windows`, draft: true, prerelease: false, body: notes } });
  requireValue(Number.isSafeInteger(release?.id) && release.id > 0 && release.tag_name === tag && release.draft === true
    && release.prerelease === false && typeof release.upload_url === 'string', 'Draft release creation failed');
  const uploadUrl = release.upload_url.replace(/\{.*$/, '');
  requireValue(uploadUrl === `https://uploads.github.com/repos/${repository}/releases/${release.id}/assets`,
    'Unexpected asset upload destination');
  for (const name of releaseAssets) {
    const data = bytesByName.get(name);
    const asset = await request(`${uploadUrl}?name=${encodeURIComponent(name)}`, { method: 'POST', bytes: data });
    requireValue(asset?.name === name && asset.state === 'uploaded' && asset.size === data.length && asset.digest === `sha256:${hash(data)}`,
      `Published asset integrity mismatch: ${name}`);
  }
  await verifyAssets(release.id);
  assertNoNewer(await pages('/releases'));
  await request(`/releases/${release.id}`, { method: 'PATCH', json: { draft: false, prerelease: false, make_latest: 'true' } });
  const published = await request(`/releases/${release.id}`);
  assertStable(published, release.id);
  await verifyAssets(release.id);
  // Confirm the files users will download without giving the download host an Actions credential.
  for (const name of releaseAssets) {
    const url = `https://github.com/${repository}/releases/download/${tag}/${name}`;
    const response = await fetchImpl(url, { method: 'GET', signal: AbortSignal.timeout(120000) });
    requireValue(response.ok, `Public release download failed: ${name}`);
    const data = Buffer.from(await response.arrayBuffer()), expected = bytesByName.get(name);
    requireValue(data.length === expected.length && hash(data) === hash(expected), `Public release file mismatch: ${name}`);
  }
  const old = await request('/contents/update.json?ref=main'), oldMetadata = feedContent(old);
  requireValue(versionParts(oldMetadata.version) && compareVersions(oldMetadata.version, version) <= 0,
    'A newer or invalid update feed must not be replaced');
  await request('/contents/update.json', { method: 'PUT', json: { branch: 'main', sha: old.sha,
    message: `Update Windows ${version} release feed`, content: Buffer.from(JSON.stringify(metadata, null, 2) + '\n').toString('base64') } });
  const publicFeed = feedContent(await request('/contents/update.json?ref=main', { anonymous: true }));
  requireValue(Object.keys(publicFeed).length === Object.keys(metadata).length
    && Object.entries(metadata).every(([key, value]) => publicFeed[key] === value), 'Published update feed verification failed');

  // Cleanup starts only after the stable release, its public files and the public feed are verified.
  // Deleting a release removes its attachments; Git tag/history endpoints are never mutated.
  const deleted = [], retained = [];
  for (const candidate of await pages('/releases')) {
    if (!olderRelease(candidate, version, release.id)) { retained.push(candidate.tag_name); continue; }
    const current = await request(`/releases/${candidate.id}`, { allowMissing: true });
    if (!current) continue;
    if (current.id !== candidate.id || current.tag_name !== candidate.tag_name || !olderRelease(current, version, release.id)) {
      retained.push(current.tag_name); continue;
    }
    await request(`/releases/${current.id}`, { method: 'DELETE' });
    requireValue(await request(`/releases/${current.id}`, { allowMissing: true }) === null, 'Older release cleanup verification failed');
    deleted.push(current.tag_name);
  }
  return { published: true, version, sourceCommit: environment.GITHUB_SHA, verificationRun: environment.GITHUB_RUN_ID,
    sha256, size: exe.length, url: published.html_url, deletedReleases: deleted, retainedReleases: retained };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try { console.log(JSON.stringify(await publishRelease())); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
