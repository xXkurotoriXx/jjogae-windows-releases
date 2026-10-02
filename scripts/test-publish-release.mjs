import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import crypto from 'node:crypto';
import { publishRelease, repository, releaseAssets, versionParts, compareVersions, olderRelease } from './publish-release.mjs';

const digest = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const json = (data, status = 200) => new Response(JSON.stringify(data), { status, headers: { 'Content-Type': 'application/json' } });
const release = (id, tag, extra = {}) => ({ id, tag_name: tag, draft: false, prerelease: false,
  published_at: '2026-10-01T00:00:00Z', ...extra });

function fixture(t, releases = [release(17, 'v0.4.17')]) {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'jjogae-publisher-test-'));
  t.after(() => {
    assert.equal(path.dirname(directory), path.resolve(os.tmpdir()));
    assert.ok(path.basename(directory).startsWith('jjogae-publisher-test-'));
    fs.rmSync(directory, { recursive: true, force: true });
  });
  const output = path.join(directory, 'artifacts', 'release'); fs.mkdirSync(output, { recursive: true });
  const exe = Buffer.alloc(512); exe.write('MZ'); exe.writeUInt32LE(128, 0x3c); exe.write('PE\0\0', 128); exe.writeUInt16LE(0x8664, 132);
  fs.writeFileSync(path.join(directory, 'Directory.Build.props'), '<Project><PropertyGroup><Version>0.5.0</Version></PropertyGroup></Project>');
  fs.writeFileSync(path.join(directory, 'RELEASE_NOTES.md'), '# 쪼개 상황실 0.5.0\n\nWindows용 쪼개 상황실입니다.\n');
  fs.writeFileSync(path.join(directory, 'THIRD_PARTY_NOTICES.md'), '# Component notices\nFull license links.\n');
  fs.writeFileSync(path.join(output, 'JjogaeStatus.exe'), exe);
  fs.writeFileSync(path.join(output, 'SHA256SUMS.txt'), `\uFEFF${digest(exe)}  JjogaeStatus.exe\n`);
  for (const name of ['smoke-test.txt', 'parity-test.txt']) fs.writeFileSync(path.join(output, name), 'PASS: test fixture\n');
  const environment = { GITHUB_ACTIONS: 'true', GITHUB_REPOSITORY: repository, GITHUB_REF: 'refs/tags/v0.5.0',
    GITHUB_SHA: 'a'.repeat(40), GITHUB_RUN_ID: '123', GH_TOKEN: 'fixture-authentication' };
  const state = { releases: structuredClone(releases), calls: [], assets: [], data: new Map(),
    feed: { version: '0.4.17', minimumManualMigrationVersion: '0.4.15', sourceCommit: 'older-source', verificationRun: 'older-run' },
    feedSha: 'old-feed-sha', uploaded: [], deleted: [], publicFeedRead: false };
  async function fetchImpl(address, options = {}) {
    const url = new URL(address), method = options.method ?? 'GET', body = typeof options.body === 'string' ? JSON.parse(options.body) : options.body;
    state.calls.push({ url: url.href, method, headers: options.headers ?? {}, body });
    const apiPrefix = `/repos/${repository}`, route = url.pathname.slice(apiPrefix.length);
    if (url.hostname === 'uploads.github.com') {
      assert.equal(method, 'POST'); assert.equal(url.pathname, `${apiPrefix}/releases/5000/assets`);
      const name = url.searchParams.get('name'), data = Buffer.from(body);
      state.uploaded.push(name); state.data.set(name, data);
      const asset = { id: 6000 + state.assets.length, name, state: 'uploaded', size: data.length,
        digest: `sha256:${state.failUpload === name ? '0'.repeat(64) : digest(data)}` };
      state.assets.push(asset); return json(asset, 201);
    }
    if (url.hostname === 'github.com') {
      assert.equal(method, 'GET'); assert.equal(options.headers?.Authorization, undefined);
      const name = url.pathname.split('/').at(-1);
      assert.equal(url.pathname, `/${repository}/releases/download/v0.5.0/${name}`);
      if (state.failDownload === name) return new Response('Unavailable', { status: 503 });
      return new Response(state.corruptDownload === name ? Buffer.from('different bytes') : state.data.get(name));
    }
    assert.equal(url.hostname, 'api.github.com'); assert.ok(url.pathname.startsWith(apiPrefix));
    if (route === '' && method === 'GET') return json({ full_name: repository, private: state.privateRepo ?? false });
    if (route === '/git/ref/tags/v0.5.0' && method === 'GET')
      return json({ object: { type: state.annotatedTag ? 'tag' : 'commit', sha: state.tagSha ?? environment.GITHUB_SHA } });
    if (route === `/git/tags/${environment.GITHUB_SHA}` && method === 'GET')
      return json({ object: { type: 'commit', sha: environment.GITHUB_SHA } });
    if (route === '/releases' && method === 'GET') {
      if (state.publicFeedRead && state.addAfterFeed && !state.added) { state.releases.push(...state.addAfterFeed); state.added = true; }
      const page = Number(url.searchParams.get('page')), items = state.releases.slice((page - 1) * 100, page * 100);
      if (page === 2 && state.duplicatePage) items.unshift(state.releases[0]);
      return json(items);
    }
    if (route === '/releases' && method === 'POST') {
      state.current = { id: 5000, ...body, published_at: null,
        upload_url: `https://uploads.github.com/repos/${repository}/releases/5000/assets{?name,label}`,
        html_url: `https://github.com/${repository}/releases/tag/v0.5.0` };
      state.releases.push(state.current); return json(state.current, 201);
    }
    if (route === '/releases/5000/assets' && method === 'GET') return json(state.extraAsset
      ? [...state.assets, { id: 99999, name: 'unexpected.exe' }] : state.assets);
    const releaseId = route.match(/^\/releases\/(\d+)$/)?.[1];
    if (releaseId) {
      const id = Number(releaseId), found = state.releases.find(item => item.id === id);
      if (!found) return json({ message: 'Not Found' }, 404);
      if (method === 'PATCH') {
        Object.assign(found, body, { published_at: '2026-10-02T08:00:00Z' });
        if (state.invalidPublished) found.prerelease = true;
        return json(found);
      }
      if (method === 'GET') {
        if (state.publicFeedRead && state.changedCandidate === id) found.tag_name = 'v0.6.0';
        return json(found);
      }
      if (method === 'DELETE') {
        assert.equal(state.publicFeedRead, true, 'Cleanup must follow public feed verification');
        if (state.failDelete === id) return json({ message: 'Denied' }, 403);
        state.deleted.push(id); state.releases = state.releases.filter(item => item.id !== id);
        return new Response(null, { status: 204 });
      }
    }
    if (route === '/contents/update.json' && method === 'GET') {
      const anonymous = !options.headers?.Authorization;
      if (anonymous) state.publicFeedRead = true;
      const data = anonymous && state.corruptFeed ? { ...state.feed, sha256: 'wrong' } : state.feed;
      return json({ sha: state.feedSha, encoding: 'base64', content: Buffer.from(JSON.stringify(data)).toString('base64') });
    }
    if (route === '/contents/update.json' && method === 'PUT') {
      assert.equal(body.sha, state.feedSha); assert.equal(body.branch, 'main');
      state.feed = JSON.parse(Buffer.from(body.content, 'base64').toString('utf8')); state.feedSha = 'new-feed-sha';
      return json({ content: { sha: state.feedSha } });
    }
    throw Error(`Unmocked request: ${method} ${url.href}`);
  }
  return { state, directory, output, environment, run: overrides => publishRelease({ directory, environment, fetchImpl,
    now: () => new Date('2026-10-02T08:00:00Z'), readFileVersion: async () => '0.5.0.0', ...overrides }) };
}
const writes = state => state.calls.filter(call => call.method !== 'GET');
const deletes = state => state.calls.filter(call => call.method === 'DELETE');

test('stable version comparison and cleanup do not infer unknown tags', () => {
  assert.deepEqual(versionParts('0.5.0'), [0, 5, 0]);
  for (const value of ['v0.5.0', '0.05.0', '0.5', '0.5.0-test', '9007199254740992.0.0', null]) assert.equal(versionParts(value), null);
  assert.equal(compareVersions('0.4.100', '0.5.0'), -1); assert.equal(compareVersions('1.0.0', '0.5.0'), 1);
  assert.equal(olderRelease(release(1, 'v0.4.17'), '0.5.0', 5000), true);
  for (const candidate of [release(5000, 'v0.4.17'), release(2, 'v0.5.0'), release(3, 'v0.6.0'),
    release(4, 'v0.4.17-test'), release(5, '0.4.17'), release(6, 'v0.4.17', { draft: true }),
    release(7, 'v0.4.17', { prerelease: true }), release(8, 'v0.4.17', { published_at: null })])
    assert.equal(olderRelease(candidate, '0.5.0', 5000), false);
});

test('publishes only the three app assets, verifies the public feed, then deletes older releases', async t => {
  const { run, state } = fixture(t); const result = await run();
  assert.deepEqual(state.uploaded, releaseAssets); assert.deepEqual(result.deletedReleases, ['v0.4.17']);
  assert.deepEqual(Object.keys(state.feed), ['version', 'platform', 'publishedAt', 'downloadUrl', 'checksumsUrl', 'size', 'sha256']);
  assert.equal(state.feed.version, '0.5.0'); assert.equal(result.sourceCommit, 'a'.repeat(40));
  const stableIndex = state.calls.findIndex(call => call.method === 'PATCH');
  const publicFileIndex = state.calls.findIndex(call => call.url.startsWith('https://github.com/'));
  const feedWriteIndex = state.calls.findIndex(call => call.method === 'PUT');
  const publicFeedIndex = state.calls.findIndex(call => call.url.includes('/contents/update.json') && call.method === 'GET' && !call.headers.Authorization);
  const deleteIndex = state.calls.findIndex(call => call.method === 'DELETE');
  assert.ok(stableIndex < publicFileIndex && publicFileIndex < feedWriteIndex && feedWriteIndex < publicFeedIndex && publicFeedIndex < deleteIndex);
  assert.ok(state.calls.filter(call => call.url.includes('/git/')).every(call => call.method === 'GET'));
  assert.deepEqual(state.releases.map(item => item.tag_name), ['v0.5.0']);
});

test('trusted tag context is required before all GitHub writes', async t => {
  for (const changes of [{ GITHUB_ACTIONS: 'false' }, { GITHUB_REPOSITORY: 'other/repo' }, { GITHUB_REF: 'refs/heads/main' },
    { GH_TOKEN: '' }, { GITHUB_SHA: 'bad' }, { GITHUB_RUN_ID: 'bad' }]) {
    const { run, state, environment } = fixture(t);
    await assert.rejects(run({ environment: { ...environment, ...changes } }), /Trusted|identity/);
    assert.equal(state.calls.length, 0);
  }
});

test('binary version, architecture, checksum and both internal test reports gate publishing', async t => {
  for (const failure of ['version', 'architecture', 'checksum', 'smoke-test.txt', 'parity-test.txt']) {
    const { run, state, output } = fixture(t);
    if (failure === 'architecture') { const file = path.join(output, 'JjogaeStatus.exe'), bytes = fs.readFileSync(file); bytes.writeUInt16LE(0x14c, 132); fs.writeFileSync(file, bytes); }
    else if (failure === 'checksum') fs.writeFileSync(path.join(output, 'SHA256SUMS.txt'), '0'.repeat(64) + '  JjogaeStatus.exe\n');
    else if (failure.endsWith('.txt')) fs.writeFileSync(path.join(output, failure), 'PASS: one check\nFAIL: another check\n');
    await assert.rejects(run(failure === 'version' ? { readFileVersion: async () => '0.4.17.0' } : {}), /version|x64|checksum|report/);
    assert.equal(state.calls.length, 0);
  }
});

test('empty or malformed reports cannot hide failed lines among successful checks', async t => {
  for (const report of ['', 'PASS: one check\n   FAIL: another check\n', 'PASS: one check\nFAILED: another check\n',
    'PASS: one check\nunknown result\n', 'PASS: \n']) {
    const { run, state, output } = fixture(t);
    fs.writeFileSync(path.join(output, 'smoke-test.txt'), report);
    await assert.rejects(run(), /CI report failed/); assert.equal(state.calls.length, 0);
  }
});

test('actual smoke render metadata is accepted alongside passing checks', async t => {
  const { run, state, output } = fixture(t);
  fs.writeFileSync(path.join(output, 'smoke-test.txt'), 'PASS: clean launch\r\n'
    + 'Rendered desktop: 1044 x 788; narrow: 920 x 720; additional 150% bitmap render.\r\n'
    + 'PASS: cheese page size control\r\n');
  await run(); assert.deepEqual(state.deleted, [17]);
});

test('render metadata alone or in parity reports cannot substitute for successful tests', async t => {
  const metadata = 'Rendered desktop: 1044 x 788; narrow: 920 x 720; additional 150% bitmap render.';
  for (const [name, report] of [['smoke-test.txt', metadata], ['parity-test.txt', `PASS: parity fixture\n${metadata}\n`]]) {
    const { run, state, output } = fixture(t); fs.writeFileSync(path.join(output, name), report);
    await assert.rejects(run(), /CI report failed/); assert.equal(state.calls.length, 0);
  }
});

test('smoke metadata allowance rejects failed checks, unknown lines and changed render formats', async t => {
  const metadata = 'Rendered desktop: 1044 x 788; narrow: 920 x 720; additional 150% bitmap render.';
  for (const addition of ['FAIL: hidden failure', 'unknown status', metadata,
    metadata.replace('1044', '0'), metadata.replace('920', '921'), metadata.replace('150%', '100%')]) {
    const { run, state, output } = fixture(t);
    fs.writeFileSync(path.join(output, 'smoke-test.txt'), `PASS: fixture\n${metadata}\n${addition}\n`);
    await assert.rejects(run(), /CI report failed/); assert.equal(state.calls.length, 0);
  }
});

test('existing stable versions and newer releases cannot be overwritten', async t => {
  for (const tag of ['v0.5.0', 'v0.6.0']) {
    const { run, state } = fixture(t, [release(1, tag)]);
    await assert.rejects(run(), /already exists/); assert.equal(writes(state).length, 0);
  }
});

test('private repositories and mismatched source tags refuse publishing', async t => {
  for (const property of ['privateRepo', 'tagSha']) {
    const { run, state } = fixture(t); state[property] = property === 'privateRepo' ? true : 'b'.repeat(40);
    await assert.rejects(run(), /remain public|verified source/); assert.equal(writes(state).length, 0);
  }
});

test('annotated tag resolves to the verified source without tag mutations', async t => {
  const { run, state } = fixture(t); state.annotatedTag = true;
  await run(); assert.ok(state.calls.some(call => call.url.includes('/git/tags/')));
  assert.ok(state.calls.filter(call => call.url.includes('/git/')).every(call => call.method === 'GET'));
});

test('upload digest and unexpected asset failures prevent promotion and cleanup', async t => {
  for (const mode of ['failUpload', 'extraAsset']) {
    const { run, state } = fixture(t); state[mode] = mode === 'failUpload' ? 'JjogaeStatus.exe' : true;
    await assert.rejects(run(), /integrity|Unexpected/);
    assert.equal(state.calls.some(call => call.method === 'PATCH' || call.method === 'PUT' || call.method === 'DELETE'), false);
  }
});

test('stable release and anonymous downloads must verify before the feed or cleanup changes', async t => {
  for (const mode of ['invalidPublished', 'failDownload', 'corruptDownload']) {
    const { run, state } = fixture(t); state[mode] = mode === 'invalidPublished' ? true : 'JjogaeStatus.exe';
    await assert.rejects(run(), /Stable|download|file mismatch/);
    assert.equal(state.calls.some(call => call.method === 'PUT' || call.method === 'DELETE'), false);
  }
});

test('a newer feed and failed public feed verification preserve every existing release', async t => {
  for (const mode of ['newer', 'corrupt']) {
    const { run, state } = fixture(t);
    if (mode === 'newer') state.feed.version = '0.6.0'; else state.corruptFeed = true;
    await assert.rejects(run(), /feed/); assert.equal(deletes(state).length, 0);
    if (mode === 'newer') assert.equal(state.calls.some(call => call.method === 'PUT'), false);
    assert.ok(state.releases.some(item => item.tag_name === 'v0.4.17'));
  }
});

test('pagination finds older releases on later pages and protects unknown and newer refs', async t => {
  const previous = [...Array.from({ length: 99 }, (_, i) => release(i + 1, `unclassified-${i}`)),
    release(100, 'v0.4.17'), release(101, 'v0.4.16')];
  const { run, state } = fixture(t, previous);
  state.addAfterFeed = [release(102, 'v0.6.0'), release(103, 'v0.4.15', { draft: true }), release(104, 'v0.4.14', { prerelease: true })];
  const result = await run(); assert.deepEqual(result.deletedReleases, ['v0.4.17', 'v0.4.16']);
  assert.ok(state.calls.some(call => call.url.includes('/releases?per_page=100&page=2')));
  for (const id of [1, 99, 102, 103, 104, 5000]) assert.ok(state.releases.some(item => item.id === id));
});

test('release identity is rechecked immediately before deletion', async t => {
  const { run, state } = fixture(t); state.changedCandidate = 17;
  await run(); assert.equal(deletes(state).length, 0); assert.ok(state.releases.some(item => item.tag_name === 'v0.6.0'));
});

test('duplicate paginated identities stop before publishing', async t => {
  const { run, state } = fixture(t, Array.from({ length: 101 }, (_, i) => release(i + 1, `unknown-${i}`)));
  state.duplicatePage = true; await assert.rejects(run(), /Duplicate/); assert.equal(writes(state).length, 0);
});

test('cleanup refusal does not mutate any tag or continue to delete other releases', async t => {
  const { run, state } = fixture(t, [release(17, 'v0.4.17'), release(16, 'v0.4.16')]); state.failDelete = 17;
  await assert.rejects(run(), /DELETE failed HTTP 403/); assert.equal(deletes(state).length, 1);
  assert.ok(state.calls.filter(call => call.url.includes('/git/')).every(call => call.method === 'GET'));
  assert.ok(state.releases.some(item => item.id === 16));
});
