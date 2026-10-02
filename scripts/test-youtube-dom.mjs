import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const source = fs.readFileSync(new URL('../src/Jjogae.Core/YouTubeWebScripts.cs', import.meta.url), 'utf8');
function extract(source, name) {
  const normalized = source.replace(/\r\n/g, '\n');
  const match = normalized.match(new RegExp('string ' + name + ' = """\\n([\\s\\S]*?)\\n    """;'));
  assert.ok(match, `Missing embedded script: ${name}`);
  return match[1];
}
const read = extract(source, 'Read');
const expand = extract(source, 'ExpandDetails');
let checks = 0;
function test(condition) { assert.ok(condition); checks++; }
test(extract(source.replace(/\r?\n/g, '\r\n'), 'Read') === read);
test(extract(source.replace(/\r?\n/g, '\r\n'), 'ExpandDetails') === expand);
function run({ path = '/channel/UC9gVXsUQNKvj-sn_SAdDi9g/membership', host = 'www.youtube.com', loggedIn = true, labels = [], member = false, texts = [], script = read, channelID = null, subscribedAttribute = false, subscriberText = '' } = {}) {
  const header = { innerText:subscriberText, closest:()=>null,
    querySelector: s => s.startsWith('ytd-subscribe') && subscribedAttribute ? {} : null,
    querySelectorAll: () => labels.map(text => ({ innerText: typeof text === 'string' ? text : text.text, getAttribute: () => typeof text === 'string' ? '' : text.aria })) };
  const document = {
    body: {}, documentElement: {},
    querySelectorAll: () => [{...header, innerText:'', querySelector:()=>null, querySelectorAll:()=>[]},header],
    querySelector(selector) {
      if (selector.includes('avatar-btn')) return loggedIn ? {} : null;
      if (selector === 'meta[itemprop="channelId"]') return channelID ? { getAttribute: () => channelID } : null;
      if (selector === 'link[rel="canonical"]') return null;
      if (selector.includes('header-renderer')) return { querySelector: () => subscribedAttribute ? {} : null, querySelectorAll: () => labels.map(text => ({ innerText: typeof text === 'string' ? text : text.text, getAttribute: () => typeof text === 'string' ? '' : text.aria })) };
      if (selector === 'a[aria-label="이 채널의 멤버십 관리"]') return member ? {} : null;
      if (selector.includes('ytd-browse')) return {};
      throw new Error('Unexpected DOM read: ' + selector);
    },
    createTreeWalker() { let index = 0; return { nextNode: () => index < texts.length ? texts[index++] : null }; }
  };
  return vm.runInNewContext(script, { location: { protocol: 'https:', hostname: host, pathname: path }, document, NodeFilter: { SHOW_TEXT: 4, SHOW_ELEMENT: 1 }, Node: { ELEMENT_NODE: 1, TEXT_NODE: 3 } }, { timeout: 1000 });
}
test(run({ labels: ['구독 중'], member: true }).subscribed === true);
test(run({ labels: ['Subscribed'] }).subscribed === true);
test(run({ labels: [{text:'구독', aria:'구독'}] }).subscribed === false);
test(run({ labels: [{text:'', aria:'Subscribed. Current setting is all notifications.'}] }).subscribed === true);
test(run({ subscribedAttribute:true }).subscribed === true);
test(run({ subscribedAttribute:true, loggedIn:false }).subscribed === true);
test(run({ labels:['구독중'], loggedIn:false }).loggedIn === true);
test(run({ labels:['구독중'], subscriberText:'루파\n구독자 6.94천명\n동영상 81개' }).subscriberText === '구독자 6.94천명');
test(run({ path:'/channel/UC9gVXsUQNKvj-sn_SAdDi9g', labels:['구독 중'] }).subscribed === true);
test(run({ path:'/@rupa', channelID:'UC9gVXsUQNKvj-sn_SAdDi9g', labels:['구독 중'] }).subscribed === true);
test(run({ path:'/@other', channelID:'other', labels:['구독 중'] }) === null);
test(run({ path:'/watch', channelID:'UC9gVXsUQNKvj-sn_SAdDi9g', labels:['구독 중'] }) === null);
test(run({ labels:[] }).loggedIn === true && run({ labels:[] }).subscribed === undefined);
test(run({ path:'/', labels:['구독 중'] }).subscribed === undefined);
test(run({ labels: ['구독'] }).subscribed === false);
test(run({ labels: ['이 채널 가입'] }).active === false);
test(run({ labels: ['구독', '이 채널 가입'], loggedIn: false }).active === undefined);
test(run({ labels: ['구독', '이 채널 가입'], loggedIn: false }).subscribed === undefined);
test(run({ host: 'accounts.google.com' }) === null);
test(run({ path: '/watch' }) === null);
test(run({ path: '/channel/another/membership' }) === null);
const memberNode = { textContent: '회원', parentElement: { innerText: '루파 회원 6개월', parentElement: null } };
const result = run({ texts: [memberNode] });
test(result.active === true && result.months === 6 && result.tier === '루파');
test(run({ script: expand, path: '/watch' }) === null);
test(run({ script: expand, path: '/paid_memberships', texts: [] }) === null);
console.log(`PASS: ${checks} YouTube DOM fixture checks`);
