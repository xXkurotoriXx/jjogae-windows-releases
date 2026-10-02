namespace Jjogae.Core;

// DOM readers shared with the macOS implementation; no login fields or cookies are read.
public static class YouTubeWebScripts
{
    public const string Read = """
    (() => {
      if (location.protocol !== 'https:' || location.hostname !== 'www.youtube.com') return null;
      const id = 'UC9gVXsUQNKvj-sn_SAdDi9g';
      const pathname = location.pathname.replace(/\/$/, '');
      const directChannel = pathname === '/channel/' + id || pathname.startsWith('/channel/' + id + '/');
      const channelRoute = pathname.startsWith('/channel/') || pathname.startsWith('/@');
      const channelMeta = document.querySelector('meta[itemprop="channelId"]');
      const canonical = document.querySelector('link[rel="canonical"]');
      const canonicalURL = canonical && canonical.getAttribute('href');
      const headers = [...document.querySelectorAll('ytd-c4-tabbed-header-renderer, ytd-page-header-renderer, yt-page-header-renderer, yt-page-header-view-model')]
        .filter(h => !h.closest('[hidden]'));
      const targetLink = headers.some(h => h.querySelector('a[href="/channel/' + id + '/membership"], a[href="https://www.youtube.com/channel/' + id + '/membership"]'));
      const channel = directChannel || (channelRoute && (channelMeta?.getAttribute('content') === id || targetLink ||
        canonicalURL === 'https://www.youtube.com/channel/' + id));
      const paid = location.pathname === '/paid_memberships';
      if (!channel && !paid && pathname !== '') return null;
      const clean = s => (s || '').replace(/\s+/g, ' ').trim();
      const out = {channelID:id, loggedIn:false};
      const avatar = document.querySelector('button#avatar-btn, #avatar-btn');
      out.loggedIn = !!avatar;
      if (channel) {
        // 이 URL의 채널 헤더와 회원 정보에만 한정한다.
        const buttons = headers.flatMap(h => [...h.querySelectorAll('button, [role="button"]')]);
        const labels = buttons.flatMap(b => [clean(b.innerText), clean(b.getAttribute('aria-label'))]).filter(Boolean);
        const subscribedControl = headers.some(h => h.querySelector('ytd-subscribe-button-renderer[subscribed]'));
        if (subscribedControl || labels.some(s => /구독\s*중|현재 설정은.*알림 수신|\bSubscribed\b|Current setting is/i.test(s))) { out.subscribed = true; out.loggedIn = true; }
        else if (out.loggedIn && labels.some(s => /^(구독|Subscribe)$/i.test(s))) out.subscribed = false;
        for (const header of headers) {
          const count = (header.innerText || '').split(/[\n•]/).map(clean).find(s => /^구독자\s*[\d.,]+\s*(천|만|억)?\s*명?$|^[\d.,]+\s*[KMB]?\s+subscribers?$/i.test(s));
          if (count) { out.subscriberText = count; break; }
        }
        const root = document.querySelector('ytd-browse') || document.querySelector('main');
        if (root) {
          const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
          let node, inspected = 0;
          while ((node = walker.nextNode()) && ++inspected <= 12000) {
            if (!/^회원$|^Member$/.test(clean(node.textContent))) continue;
            let p = node.parentElement;
            for (let i=0; p && i<5; i++,p=p.parentElement) {
              const text = clean(p.innerText);
              if (text.length > 500) break;
              const m = text.match(/(.+?)\s*회원\s*(\d+)\s*개월/);
              if (m) { out.active=true; out.months=Number(m[2]); out.tier=m[1].slice(-100); break; }
            }
            if (out.active) break;
          }
        }
        // 혜택 관리 링크는 해당 계정의 회원 전용 컨트롤이다.
        if (document.querySelector('a[aria-label="이 채널의 멤버십 관리"]')) out.active = true;
        if (out.active) out.loggedIn = true;
        else if (out.loggedIn && labels.some(s => /이 채널 가입|Join this channel/.test(s))) out.active = false;
      } else if (paid) {
        const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
        let node, inspected=0, inactiveSection=false;
        while ((node=walker.nextNode()) && ++inspected<=12000) {
          if (clean(node.textContent) === '비활성 멤버십') inactiveSection = true;
          if (clean(node.textContent) !== '아홀로 루파') continue;
          if (inactiveSection && out.loggedIn) { out.active=false; break; }
          // 펼친 상세는 제목 카드의 형제가 될 수 있다. 다음 제목 전까지만 읽는다.
          const details=document.createTreeWalker(document.body, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT);
          details.currentNode=node;
          let next, text='아홀로 루파 ', scanned=0;
          while ((next=details.nextNode()) && ++scanned<=1200 && text.length<=3000) {
            if (next.nodeType===Node.ELEMENT_NODE && next.matches('h1,h2,h3,[role="heading"]')) break;
            if (next.nodeType===Node.TEXT_NODE) text+=' '+clean(next.textContent);
          }
          text=clean(text);
          // 비활성 카드나 가격 안내만으로는 활성 상태를 단정하지 않는다.
          const match=text.match(/다음\s*결제일\s*[:：]?\s*((?:\d{4}년\s*)?\d{1,2}월\s*\d{1,2}일)/);
          if (!match) continue;
          if (match) {out.billing=match[1];out.active=true;out.loggedIn=true;}
          const tier=text.match(/아홀로 루파\s+(.+?)\s*₩/);
          if (tier) out.tier=tier[1].slice(0,100);
          break;
        }
      }
      return out;
    })()
    """;

    public const string ExpandDetails = """
    (() => {
      if (location.hostname !== 'www.youtube.com' || location.pathname !== '/paid_memberships') return null;
      const clean = s => (s || '').replace(/\s+/g, ' ').trim();
      const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
      let n, count=0, inactive=false;
      while ((n=walker.nextNode()) && ++count<=12000) {
        if (clean(n.textContent)==='비활성 멤버십') inactive=true;
        if (clean(n.textContent)!=='아홀로 루파') continue;
        if (inactive) return null;
        let p=n.parentElement;
        for (let i=0; p && i<10; i++,p=p.parentElement) {
          if (p === document.body || p === document.documentElement) return null;
          if ([...p.querySelectorAll('h1,h2,h3,[role="heading"]')].some(h =>
              clean(h.innerText) && clean(h.innerText)!=='아홀로 루파')) return null;
          if (clean(p.innerText).length>3000) return null;
          const button=[...p.querySelectorAll('button')].find(b =>
            clean(b.innerText)==='멤버십 관리' &&
              (b.getAttribute('aria-expanded')==='false' || b.getAttribute('aria-pressed')==='false'));
          if (button) {button.click(); return null;}
        }
        return null;
      }
      return null;
    })()
    """;
}
