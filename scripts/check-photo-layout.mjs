// Isolated QA only. Real copied images; API response rotations are intercepted, never saved.
import assert from 'node:assert/strict';import{mkdir,writeFile}from'node:fs/promises';
const api='http://127.0.0.1:18089',base='http://127.0.0.1:18088';
const fixtures=await Promise.all([1,197,203].map(id=>fetch(api+'/api/items/'+id).then(r=>r.json())));
const target=await fetch('http://127.0.0.1:9224/json/new?about:blank',{method:'PUT'}).then(r=>r.json());const ws=new WebSocket(target.webSocketDebuggerUrl);await new Promise(r=>ws.addEventListener('open',r,{once:true}));let seq=0,rotation=0,current=fixtures[0];const calls=new Map();
const cdp=(method,params={})=>new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(Error(method)),20000);calls.set(++seq,m=>{clearTimeout(timer);m.error?reject(Error(JSON.stringify(m.error))):resolve(m.result)});ws.send(JSON.stringify({id:seq,method,params}));});
ws.addEventListener('message',({data})=>{const m=JSON.parse(data);if(calls.has(m.id)){calls.get(m.id)(m);calls.delete(m.id)}if(m.method==='Fetch.requestPaused'){const p=m.params;const body={...current,photos:current.photos.map(photo=>({...photo,rotationDegrees:rotation}))};void cdp('Fetch.fulfillRequest',{requestId:p.requestId,responseCode:200,responseHeaders:[{name:'Content-Type',value:'application/json'}],body:Buffer.from(JSON.stringify(body)).toString('base64')});}});
const ev=async expression=>{const r=await cdp('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw Error(JSON.stringify(r.exceptionDetails));return r.result.value;};const pause=ms=>new Promise(r=>setTimeout(r,ms));
async function wait(expression){for(let i=0;i<100;i++){if(await ev(expression))return;await pause(100)}throw Error('Timeout '+expression)}
const out='backups/ui-activity-zoom-20261027/screens';await mkdir(out,{recursive:true});async function shot(name){await writeFile(out+'/'+name+'.png',Buffer.from((await cdp('Page.captureScreenshot')).data,'base64'));}
function bounds(selector){return `(()=>{const i=document.querySelector(${JSON.stringify(selector)}),h=i.parentElement;const a=i.getBoundingClientRect(),b=h.getBoundingClientRect();return {fits:a.left>=b.left-1&&a.right<=b.right+1&&a.top>=b.top-1&&a.bottom<=b.bottom+1,center:Math.abs((a.left+a.right-b.left-b.right)/2)<1&&Math.abs((a.top+a.bottom-b.top-b.bottom)/2)<1,natural:[i.naturalWidth,i.naturalHeight],ratio:Math.abs(i.width/i.height-i.naturalWidth/i.naturalHeight)<.02,width:a.width,height:a.height};})()`}
try{await cdp('Page.enable');await cdp('Fetch.enable',{patterns:[{urlPattern:'*/api/items/1',requestStage:'Request'},{urlPattern:'*/api/items/197',requestStage:'Request'},{urlPattern:'*/api/items/203',requestStage:'Request'}]});
let count=0;const sizes=[];
for(current of fixtures)for(const width of [390,768,1440,2560])for(rotation of [0,90,180,270]){
 await cdp('Emulation.setDeviceMetricsOverride',{width,height:1050,deviceScaleFactor:1,mobile:width===390});
 await cdp('Emulation.setTouchEmulationEnabled',{enabled:width===390});
 await cdp('Input.dispatchMouseEvent',{type:'mouseMoved',x:0,y:0});
 await cdp('Page.navigate',{url:base+'/item/'+current.id});
 await wait(`document.querySelector('.main-photo img')?.naturalWidth>0 && document.querySelector('.main-photo img')?.getBoundingClientRect().width>0`);await pause(120);
 const b=await ev(bounds('.main-photo img'));assert(b.fits&&b.center&&b.ratio,JSON.stringify({id:current.id,width,rotation,b}));assert(await ev('document.documentElement.scrollWidth<=innerWidth'),'page overflow');
 const thumb=await ev(bounds('.thumb-button img'));assert(thumb.fits&&thumb.center,'thumbnail geometry');
 const rect=await ev('document.querySelector(".main-photo app-fitted-photo").getBoundingClientRect().toJSON()');
 const fine=await ev("matchMedia('(hover: hover) and (pointer: fine)').matches");
 await cdp('Input.dispatchMouseEvent',{type:'mouseMoved',x:rect.x+rect.width/2,y:rect.y+rect.height/2});await pause(60);
 const enlarged=await ev(bounds('.main-photo img'));
 if(fine){assert(Math.abs(enlarged.width/b.width-2)<.02,'hover zoom ×2');assert(enlarged.center,'centered zoom');
  await cdp('Input.dispatchMouseEvent',{type:'mouseMoved',x:rect.x+rect.width*.8,y:rect.y+rect.height*.8});await pause(50);
  assert(await ev(`(()=>{const i=document.querySelector('.main-photo img').getBoundingClientRect(),h=document.querySelector('.main-photo app-fitted-photo').getBoundingClientRect();return (i.width<h.width+1||(i.left<=h.left+1&&i.right>=h.right-1))&&(i.height<h.height+1||(i.top<=h.top+1&&i.bottom>=h.bottom-1));})()`),'pan must not reveal blank edges');
 }else assert.deepEqual(enlarged,b,'touch devices retain fitted image');
 if(current.id===1&&width===1440&&rotation===90)await shot('hover-rotated-1440');
 await cdp('Input.dispatchMouseEvent',{type:'mouseMoved',x:0,y:0});await pause(50);assert.deepEqual(await ev(bounds('.main-photo img')),b,'mouseleave restores fitted centered image');
 if(rotation===0&&width===1440)sizes.push({id:current.id,natural:b.natural});
 if(current.id===1&&[390,1440].includes(width)&&[0,90].includes(rotation))await shot(`item-${width}-${rotation}`);
 if(rotation===90&&[390,1440].includes(width)){
  await ev('document.querySelector(".main-photo").focus()');await cdp('Input.dispatchKeyEvent',{type:'keyDown',key:'Enter',code:'Enter',windowsVirtualKeyCode:13});await cdp('Input.dispatchKeyEvent',{type:'keyUp',key:'Enter',code:'Enter',windowsVirtualKeyCode:13});
  await wait('document.querySelector(".photo-modal img")?.naturalWidth>0 && document.querySelector(".photo-modal img")?.getBoundingClientRect().width>0');const modal=await ev(bounds('.photo-modal img'));assert(modal.fits&&modal.center&&modal.ratio,'full-screen geometry');
  assert(await ev('!!document.activeElement.closest(".photo-modal")'),'dialog focus');await cdp('Input.dispatchKeyEvent',{type:'keyDown',key:'Escape',code:'Escape',windowsVirtualKeyCode:27});await cdp('Input.dispatchKeyEvent',{type:'keyUp',key:'Escape',code:'Escape',windowsVirtualKeyCode:27});await wait('!document.querySelector(".photo-modal")');assert(await ev('document.activeElement.classList.contains("main-photo")'),'focus restored');
 }
 count++;
}
console.log('PASS',count,'real-photo layouts; centered/uncropped at 0/90/180/270; 390/768/1440/2560; mouse hover ×2 with clamped pan, touch excluded, reset on leave; keyboard/modal/focus',JSON.stringify(sizes));
// Multiple-photo selection and collapsed upload remain usable.
current=fixtures.find(x=>x.photos.length>1)||fixtures[0];rotation=0;await cdp('Page.navigate',{url:base+'/item/'+current.id});await wait('!!document.querySelector(".thumb-button")');
if(current.photos.length>1){await ev('document.querySelectorAll(".thumb-button")[1].click()');await wait('document.querySelector(".photo-counter")?.textContent.includes("Foto 2")');}
assert(await ev('!document.querySelector(".photo-upload-disclosure").open'));await ev('document.querySelector(".photo-upload-disclosure summary").click()');assert(await ev('document.querySelector(".photo-upload-disclosure").open'));console.log('PASS photo selection and progressive upload');
}finally{await cdp('Page.close');ws.close()}
