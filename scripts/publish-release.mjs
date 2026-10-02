import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
const repository='xXkurotoriXx/jjogae-windows-releases';
const hash=bytes=>crypto.createHash('sha256').update(bytes).digest('hex');
const version=fs.readFileSync('Directory.Build.props','utf8').match(/<Version>(\d+\.\d+\.\d+)<\/Version>/)?.[1];
if(process.env.GITHUB_REPOSITORY!==repository||process.env.GITHUB_REF!=='refs/tags/v'+version||!process.env.GH_TOKEN)throw Error('Trusted repository tag workflow required');
const files='artifacts/release',exe=fs.readFileSync(path.join(files,'JjogaeStatus.exe')),sha256=hash(exe);
if(fs.readFileSync(path.join(files,'SHA256SUMS.txt'),'utf8').trim().replace(/^\uFEFF/,'')!==sha256+'  JjogaeStatus.exe')throw Error('CI executable checksum mismatch');
for(const name of ['smoke-test.txt','parity-test.txt']){const report=fs.readFileSync(path.join(files,name),'utf8');if(report.includes('FAIL')||!report.includes('PASS'))throw Error('CI report failed');}
const metadata={version,platform:'windows-x64',publishedAt:new Date().toISOString(),downloadUrl:'https://github.com/'+repository+'/releases/download/v'+version+'/JjogaeStatus.exe',checksumsUrl:'https://github.com/'+repository+'/releases/download/v'+version+'/SHA256SUMS.txt',size:exe.length,sha256,minimumManualMigrationVersion:'0.4.15',sourceCommit:process.env.GITHUB_SHA,verificationRun:process.env.GITHUB_RUN_ID};
fs.writeFileSync(path.join(files,'update.json'),JSON.stringify(metadata,null,2)+'\n');
async function request(route,{method='GET',json,bytes}={}){
 const url=route.startsWith('https://uploads.github.com/')?route:'https://api.github.com'+route;
 const response=await fetch(url,{method,headers:{Authorization:'Bearer '+process.env.GH_TOKEN,Accept:'application/vnd.github+json','X-GitHub-Api-Version':'2022-11-28','Content-Type':bytes?'application/octet-stream':'application/json'},body:json?JSON.stringify(json):bytes,signal:AbortSignal.timeout(120000)});
 if(!response.ok)throw Error('GitHub '+method+' failed HTTP '+response.status);
 return response.status===204?null:response.json();
}
const existing=await request('/repos/'+repository+'/releases?per_page=100');if(existing.some(x=>x.tag_name==='v'+version))throw Error('Release version already exists; do not replace a stable release');
const notes=fs.readFileSync('RELEASE_NOTES.md','utf8').replace(/^#[^\n]*\n+/,'').trim();if(!notes)throw Error('Release notes required');
const release=await request('/repos/'+repository+'/releases',{method:'POST',json:{tag_name:'v'+version,target_commitish:process.env.GITHUB_SHA,name:'쪼개 상황실 '+version+' · Windows',draft:true,prerelease:false,body:notes}});
for(const name of ['JjogaeStatus.exe','SHA256SUMS.txt','README.md','PRIVACY.md','THIRD_PARTY_NOTICES.md','INSTALLATION.md','update.json']){
 const data=fs.readFileSync(path.join(['JjogaeStatus.exe','SHA256SUMS.txt','update.json'].includes(name)?files:'.',name));
 const asset=await request(release.upload_url.replace(/\{.*$/,'')+'?name='+encodeURIComponent(name),{method:'POST',bytes:data});if(asset.size!==data.length||asset.digest!=='sha256:'+hash(data))throw Error('Published asset integrity mismatch');
}
await request('/repos/'+repository+'/releases/'+release.id,{method:'PATCH',json:{draft:false,prerelease:false,make_latest:'true'}});
// The app reads releases, so never advertise an unpublished file in the auxiliary feed.
const old=await request('/repos/'+repository+'/contents/update.json?ref=main');
const identity={name:'xXkurotoriXx',email:'270817688+xXkurotoriXx@users.noreply.github.com'};
await request('/repos/'+repository+'/contents/update.json',{method:'PUT',json:{branch:'main',sha:old.sha,message:'Update Windows '+version+' release feed',content:Buffer.from(JSON.stringify(metadata,null,2)+'\n').toString('base64'),author:identity,committer:identity}});
console.log(JSON.stringify({published:true,version,sourceCommit:process.env.GITHUB_SHA,sha256,size:exe.length,url:release.html_url}));
