// Isolated verification. Synthetic credentials exist only in process memory.
const assert=require('node:assert/strict');
const {randomBytes,pbkdf2Sync}=require('node:crypto');
const {mkdirSync,writeFileSync,readFileSync}=require('node:fs');
const {resolve}=require('node:path');
const {spawnSync,spawn}=require('node:child_process');
const https=require('node:https');
const root=resolve('backups/ui-access-20261026/test-data');
mkdirSync(root+'/access',{recursive:true,mode:0o700});
const password=randomBytes(32).toString('base64url'), salt=randomBytes(16);
writeFileSync(root+'/access/owner.json',JSON.stringify({Username:'qa',Salt:salt.toString('base64'),Hash:pbkdf2Sync(password,salt,600000,32,'sha256').toString('base64'),Iterations:600000}),{mode:0o600});
const name='inventario-auth-qa';
const result=spawnSync('docker',['run','-d','--name',name,'-p','127.0.0.1:18090:8080','-v',root+':/data',process.env.JUNKYARD_TEST_IMAGE || 'inventario:session-20261027'],{encoding:'utf8'});assert.equal(result.status,0,result.stderr);
const proxy=spawn('node',['scripts/serve-secure.mjs'],{env:{...process.env,JUNKYARD_TLS_CERT:resolve('backups/ui-access-20261026/tls-test/cert.pem'),JUNKYARD_TLS_KEY:resolve('backups/ui-access-20261026/tls-test/key.pem'),JUNKYARD_API:'http://127.0.0.1:18090',JUNKYARD_HTTPS_PORT:'18443',JUNKYARD_SPA_ROOT:resolve(process.env.JUNKYARD_TEST_SPA_ROOT || 'spikes/angular-inventory-spike/dist/angular-inventory-spike/browser')},stdio:'ignore'});
const ca=readFileSync('backups/ui-access-20261026/tls-test/cert.pem');
const jar=new Map();let csrf='';
function call(path,method='GET',data,options={}){return new Promise((yes,no)=>{
 const headers={'content-type':'application/json'};
 if(!options.anonymous){headers.cookie=[...jar].map(([k,v])=>`${k}=${v}`).join('; ');if(csrf&&!options.noCsrf)headers['X-Junkyard-CSRF']=csrf;}
 if(options.cookie)headers.cookie=options.cookie;
 const req=https.request({hostname:'127.0.0.1',port:18443,path,method,headers,ca},res=>{
  if(!options.anonymous)for(const c of res.headers['set-cookie']||[]){const part=c.split(';')[0];jar.set(part.slice(0,part.indexOf('=')),part.slice(part.indexOf('=')+1));}
  let text='';res.on('data',c=>text+=c);res.on('end',()=>{let body;try{body=JSON.parse(text)}catch{}yes({status:res.statusCode,body,headers:res.headers,text});});
 });req.on('error',no);req.end(data?JSON.stringify(data):undefined);
});}
(async()=>{
 try{
  for(let n=0;n<40;n++){try{const r=await fetch('http://127.0.0.1:18090/health');if(r.ok)break;}catch{}await new Promise(r=>setTimeout(r,300));}
  assert.equal((await fetch('http://127.0.0.1:18090/api/items/1')).status,426,'HTTPS enforced');
  for(const path of ['/api/items/1','/api/csv/export','/uploads/no-file.jpg','/photo-derivatives/no-file.webp'])assert.equal((await call(path,'GET',null,{anonymous:true})).status,401,path);
  const session=await call('/api/auth/session');assert.equal(session.status,200);csrf=session.body.csrfToken;assert(csrf);assert.equal(session.body.authenticated,false);
  assert.equal((await call('/api/auth/login','POST',{username:'qa',password},{noCsrf:true})).status,403,'login CSRF');
  const login=await call('/api/auth/login','POST',{username:'qa',password});assert.equal(login.status,200,login.text);
  assert(login.headers['set-cookie'].some(x=>x.includes('junkyard.session=')&&/httponly/i.test(x)&&/secure/i.test(x)&&/samesite=strict/i.test(x)));
  assert(!login.headers['set-cookie'].find(x=>x.startsWith('junkyard.session=')).match(/expires=|max-age=/i),'normal login is a browser-session cookie');
  async function restart(){
    assert.equal(spawnSync('docker',['restart',name],{stdio:'ignore'}).status,0);
    for(let n=0;n<60;n++){try{if((await fetch('http://127.0.0.1:18090/health')).ok)return;}catch{}await new Promise(r=>setTimeout(r,200));}
    throw Error('QA backend failed restart');
  }
  await restart();
  assert.equal((await call('/api/items/1')).status,200,'normal session also survives deployment within its 8h limit');
  csrf=(await call('/api/auth/session')).body.csrfToken;
  assert.equal((await call('/api/auth/logout','POST',{})).status,200);
  csrf=(await call('/api/auth/session')).body.csrfToken;
  const remembered=await call('/api/auth/login','POST',{username:'qa',password,remember:true});
  assert.equal(remembered.status,200);
  const expiry=remembered.headers['set-cookie'].find(x=>x.startsWith('junkyard.session=')).match(/expires=([^;]+)/i);
  assert(expiry);const remaining=new Date(expiry[1]).getTime()-Date.now();assert(remaining>29*86400000&&remaining<=30*86400000);
  const registry=JSON.parse(spawnSync('docker',['exec',name,'cat','/data/access/sessions.json'],{encoding:'utf8'}).stdout);
  assert(Object.keys(registry).every(k=>/^[A-F0-9]{64}$/.test(k)));
  assert.equal(spawnSync('docker',['exec',name,'stat','-c','%a','/data/access/sessions.json'],{encoding:'utf8'}).stdout.trim(),'600');
  await restart();assert.equal((await call('/api/items/1')).status,200,'remembered session survives restart');
  console.log('PASS: session/remember cookie lifetimes, private durable registry, login survives backend restarts');
  const auth=await call('/api/auth/session');assert.equal(auth.body.authenticated,true);csrf=auth.body.csrfToken;
  assert.equal((await call('/api/items/1')).status,200);
  assert.equal((await call('/api/actions','POST',{title:'blocked'},{noCsrf:true})).status,403,'write CSRF');
  const created=await call('/api/actions','POST',{title:'QA security',priority:3});assert.equal(created.status,200,created.text);
  assert.equal((await call(`/api/actions/${created.body.id}`,'DELETE')).status,204);
  // Browser proof of the login gate and authenticated Angular routing (synthetic account).
  const target=await fetch('http://127.0.0.1:9224/json/new?about:blank',{method:'PUT'}).then(r=>r.json());
  const ws=new WebSocket(target.webSocketDebuggerUrl);await new Promise(r=>ws.addEventListener('open',r,{once:true}));
  let sequence=0;const calls=new Map();
  ws.addEventListener('message',({data})=>{const m=JSON.parse(data);if(calls.has(m.id)){calls.get(m.id)(m);calls.delete(m.id)}});
  const cdp=(method,params={})=>new Promise((resolve,reject)=>{const t=setTimeout(()=>reject(Error(method)),15000);calls.set(++sequence,m=>{clearTimeout(t);m.error?reject(Error(JSON.stringify(m.error))):resolve(m.result)});ws.send(JSON.stringify({id:sequence,method,params}));});
  const ev=async expression=>{const result=await cdp('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});if(result.exceptionDetails)throw Error(JSON.stringify(result.exceptionDetails));return result.result?.value;};
  try {
    await cdp('Page.enable');await cdp('Security.setIgnoreCertificateErrors',{ignore:true});
    await cdp('Emulation.setDeviceMetricsOverride',{width:390,height:1000,deviceScaleFactor:1,mobile:true});
    await cdp('Page.navigate',{url:'https://127.0.0.1:18443/item/1'});
    await new Promise(r=>setTimeout(r,1200));
    assert.equal(await ev('Boolean(document.querySelector("app-access-login input[name=password]"))'),true,'login visible');
    assert.equal(await ev('document.querySelector("app-detail-page")===null'),true,'no inventory before login');
    assert.equal(await ev('!!document.querySelector("input[name=remember]")'),true,'remember option visible');
    await ev(`document.querySelector('input[name=remember]').click();document.querySelector('input[name=username]').value='qa';document.querySelector('input[name=username]').dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('input[name=password]').value=${JSON.stringify(password)};document.querySelector('input[name=password]').dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('button[type=submit]').click()`);
    await new Promise(r=>setTimeout(r,1500));
    assert.equal(await ev('Boolean(document.querySelector("app-detail-page .detail-header h1"))'),true,'authenticated detail');
    for(const width of [390,1440]) {
      await cdp('Emulation.setDeviceMetricsOverride',{width,height:1000,deviceScaleFactor:1,mobile:width===390});
      assert.equal(await ev('document.documentElement.scrollWidth<=innerWidth'),true,`auth overflow ${width}`);
    }
    const cookies=await cdp('Network.getCookies',{urls:['https://127.0.0.1:18443']});
    assert(cookies.cookies.find(x=>x.name==='junkyard.session').expires>Date.now()/1000+29*86400,'remember checkbox sent by UI');
    // Explicit logout must remove loaded private content.
    await ev(`document.querySelector('button[title="Cerrar sesión"]').click()`);
    await new Promise(r=>setTimeout(r,1200));
    assert.equal(await ev('Boolean(document.querySelector("app-access-login")) && !document.querySelector("app-detail-page")'),true,'logout clears inventory DOM');
    console.log('PASS: browser login, private data gate, authenticated routing, 390/1440, logout clears data');
  } finally {await cdp('Security.setIgnoreCertificateErrors',{ignore:false});await cdp('Page.close');ws.close();}
  const oldCookie=[...jar].map(([k,v])=>`${k}=${v}`).join('; ');
  assert.equal((await call('/api/auth/logout','POST',{})).status,200);
  assert.equal((await call('/api/items/1','GET',null,{anonymous:true,cookie:oldCookie})).status,401,'logout revokes replay');
  await restart();
  assert.equal((await call('/api/items/1','GET',null,{anonymous:true,cookie:oldCookie})).status,401,'revocation survives restart');
  const anon=await call('/api/auth/session');csrf=anon.body.csrfToken;
  let last;for(let i=0;i<7;i++)last=await call('/api/auth/login','POST',{username:'qa',password:'incorrect-test-only'});
  assert.equal(last.status,429,'login rate limit');
  assert.equal((await call('/item/1','GET',null,{anonymous:true})).status,200,'Angular shell');
  assert.equal((await call('/missing.js','GET',null,{anonymous:true})).status,404,'no false SPA for assets');
  await restart();
  csrf=(await call('/api/auth/session')).body.csrfToken;
  assert.equal((await call('/api/auth/login','POST',{username:'qa',password,remember:true})).status,200);
  const expired=JSON.parse(spawnSync('docker',['exec',name,'cat','/data/access/sessions.json'],{encoding:'utf8'}).stdout);
  for(const entry of Object.values(expired))entry.Until=new Date(Date.now()-1000).toISOString();
  assert.equal(spawnSync('docker',['exec','-i',name,'sh','-c','cat > /data/access/sessions.json'],{input:JSON.stringify(expired)}).status,0);
  await restart();assert.equal((await call('/api/items/1')).status,401,'expired registry rejects replay');
  csrf=(await call('/api/auth/session')).body.csrfToken;
  assert.equal((await call('/api/auth/login','POST',{username:'qa',password,remember:true})).status,200);
  const owner=JSON.parse(readFileSync(root+'/access/owner.json','utf8'));owner.Hash=randomBytes(32).toString('base64');
  writeFileSync(root+'/access/owner.json',JSON.stringify(owner),{mode:0o600});
  assert.equal((await call('/api/items/1')).status,401,'password change immediately revokes old sessions');
  console.log('PASS: remembered-session expiry, password-change revocation, logout revocation survives restart');
  console.log('PASS: HTTPS enforcement, protected API/photos/export, login and write CSRF, secure HttpOnly Strict cookie, session, soft deletion, logout replay rejection, login rate limit, Angular routes');
 }finally{proxy.kill();spawnSync('docker',['stop',name],{stdio:'ignore'});}
})().catch(e=>{console.error(e);process.exitCode=1});
