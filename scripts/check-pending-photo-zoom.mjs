// Read-only browser QA against the isolated copy; never production.
import assert from 'node:assert/strict';
import {mkdir,writeFile} from 'node:fs/promises';
const base='http://127.0.0.1:18088';
const fixture=await fetch('http://127.0.0.1:18089/api/photos/review').then(r=>r.json());
assert(fixture.current && fixture.pending.length>1);
const target=await fetch('http://127.0.0.1:9224/json/new?about:blank',{method:'PUT'}).then(r=>r.json());
const ws=new WebSocket(target.webSocketDebuggerUrl);await new Promise(r=>ws.addEventListener('open',r,{once:true}));
let seq=0,rotation=0;const calls=new Map();
const cdp=(method,params={})=>new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(Error(method)),20000);calls.set(++seq,m=>{clearTimeout(timer);m.error?reject(Error(JSON.stringify(m.error))):resolve(m.result)});ws.send(JSON.stringify({id:seq,method,params}));});
ws.addEventListener('message',({data})=>{const m=JSON.parse(data);if(calls.has(m.id)){calls.get(m.id)(m);calls.delete(m.id)}if(m.method==='Fetch.requestPaused'){const p=m.params;const body=p.request.url.includes('/api/ai/')?{enabled:false,isUsable:false,hasApiKey:false}: {...fixture,current:{...(fixture.pending.find(photo=>photo.id===Number(new URL(p.request.url).searchParams.get('id'))) || fixture.current),rotationDegrees:rotation},pending:fixture.pending.map(p=>({...p,rotationDegrees:rotation}))};void cdp('Fetch.fulfillRequest',{requestId:p.requestId,responseCode:200,responseHeaders:[{name:'Content-Type',value:'application/json'}],body:Buffer.from(JSON.stringify(body)).toString('base64')});}});
const ev=async expression=>{const r=await cdp('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw Error(JSON.stringify(r.exceptionDetails));return r.result.value;};
const pause=ms=>new Promise(r=>setTimeout(r,ms));async function wait(expression){for(let n=0;n<100;n++){if(await ev(expression))return;await pause(100)}throw Error(expression)}
const geometry=`(()=>{const i=document.querySelector('.review-photo-frame img'),h=i.parentElement,a=i.getBoundingClientRect(),b=h.getBoundingClientRect();return {w:a.width,h:a.height,fit:a.left>=b.left-1&&a.right<=b.right+1&&a.top>=b.top-1&&a.bottom<=b.bottom+1,center:Math.abs(a.left+a.right-b.left-b.right)<2&&Math.abs(a.top+a.bottom-b.top-b.bottom)<2,left:i.style.left,top:i.style.top,transform:i.style.transform}})()`;
const out='backups/ui-pending-zoom-20261028';await mkdir(out,{recursive:true});
try{
await cdp('Page.enable');await cdp('Fetch.enable',{patterns:[{urlPattern:'*/api/photos/review*'},{urlPattern:'*/api/ai/*'}]});
for(const width of [390,768,1440,2560])for(rotation of [0,90,180,270]){
await cdp('Emulation.setDeviceMetricsOverride',{width,height:1050,deviceScaleFactor:1,mobile:width===390});await cdp('Emulation.setTouchEmulationEnabled',{enabled:width===390});
await cdp('Input.dispatchMouseEvent',{type:'mouseMoved',x:0,y:0});await cdp('Page.navigate',{url:base+'/photos/review'});
await wait(`document.querySelector('.review-photo-frame img')?.naturalWidth>0 && document.querySelector('.review-photo-frame img')?.getBoundingClientRect().width>0`);await pause(120);
let normal=await ev(geometry);assert(normal.fit&&normal.center,JSON.stringify({width,rotation,normal}));assert(await ev('document.documentElement.scrollWidth<=innerWidth'));
const rect=await ev(`document.querySelector('.review-photo-frame app-fitted-photo').getBoundingClientRect().toJSON()`);
await cdp('Input.dispatchMouseEvent',{type:'mouseMoved',x:rect.x+rect.width/2,y:rect.y+rect.height/2});await pause(50);
const enlarged=await ev(geometry),fine=await ev(`matchMedia('(hover: hover) and (pointer: fine)').matches`);
assert(Math.abs(enlarged.w/normal.w-(fine?2:1))<.02,'hover zoom factor');
await cdp('Input.dispatchMouseEvent',{type:'mouseMoved',x:rect.x+rect.width*.9,y:rect.y+rect.height*.9});await pause(50);
const pan=await ev(geometry);if(fine)assert(pan.left!==enlarged.left || pan.top!==enlarged.top,'cursor pans');
await cdp('Input.dispatchMouseEvent',{type:'mouseMoved',x:0,y:0});await pause(50);normal=await ev(geometry);assert(normal.fit&&normal.center,'exit resets');
await ev(`document.querySelector('.photo-tools button:last-child').click()`);await wait(`document.querySelector('.film.selected')`);
await ev(`document.querySelectorAll('.film')[1].click()`);await wait(`document.querySelectorAll('.film')[1]?.classList.contains('current')`);await wait(`document.querySelector('.review-photo-frame img')?.complete && document.querySelector('.review-photo-frame img')?.naturalWidth>0 && document.querySelector('.review-photo-frame img')?.getBoundingClientRect().width>0`);await pause(150);assert((await ev(geometry)).fit,'changing photo fits');
if(rotation===0 && [390,1440].includes(width))await writeFile(out+'/pending-'+width+'.png',Buffer.from((await cdp('Page.captureScreenshot')).data,'base64'));
await ev(`document.querySelectorAll('.destination')[1].click()`);await wait(`!!document.querySelector('app-item-editor')`);
if(width<=900){await ev(`document.querySelector('.photo-expand').click()`);await pause(80);assert((await ev(geometry)).fit,'mobile expand refits');}
console.log('PASS pending photo',width,rotation,'fit, zoom, pan, reset, selection, next photo, editor');
}
}finally{await cdp('Page.close');ws.close()}
