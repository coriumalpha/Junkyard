// Uses isolated API/UI and private fixture output from check-item-placements.cjs.
import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
const out='backups/temporary-placement-20261029';
const fixture=JSON.parse(await readFile(out+'/fixture.json','utf8'));
const base='http://127.0.0.1:18088';
const target=await fetch('http://127.0.0.1:9224/json/new?about:blank',{method:'PUT'}).then(r=>r.json());
const ws=new WebSocket(target.webSocketDebuggerUrl);await new Promise(r=>ws.addEventListener('open',r,{once:true}));
let seq=0,dialogs=0;const calls=new Map(),errors=[];
const cdp=(method,params={})=>new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(Error(method)),20000);calls.set(++seq,m=>{clearTimeout(timer);m.error?reject(Error(JSON.stringify(m.error))):resolve(m.result)});ws.send(JSON.stringify({id:seq,method,params}));});
ws.addEventListener('message',({data})=>{const m=JSON.parse(data);if(calls.has(m.id)){calls.get(m.id)(m);calls.delete(m.id)}if(m.method==='Page.javascriptDialogOpening'){dialogs++;void cdp('Page.handleJavaScriptDialog',{accept:false})}if(m.method==='Runtime.exceptionThrown')errors.push(m.params.exceptionDetails.text);});
const ev=async expression=>{const r=await cdp('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw Error(JSON.stringify(r.exceptionDetails));return r.result.value;};
const pause=ms=>new Promise(r=>setTimeout(r,ms));
async function wait(expression){for(let n=0;n<100;n++){if(await ev(expression))return;await pause(100)}throw Error(expression)}
const click=text=>ev(`Array.from(document.querySelectorAll('app-item-placement button')).find(b=>b.textContent.trim().endsWith(${JSON.stringify(text)})).click()`);
const fill=async(name,value)=>{await ev(`(()=>{const e=document.querySelector('app-item-placement [name="${name}"]');const p=e.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:e.tagName==='SELECT'?HTMLSelectElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(p,'value').set.call(e,${JSON.stringify(value)});e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));})()`);await pause(100)};
const item=()=>fetch('http://127.0.0.1:18089/api/items/'+fixture.id).then(r=>r.json());
const fit=()=>ev('document.documentElement.scrollWidth<=innerWidth');
async function choose(label){await ev(`document.querySelector('app-item-placement mat-select').click()`);await wait('!!document.querySelector("mat-option")');await ev(`Array.from(document.querySelectorAll('mat-option')).find(o=>o.textContent.includes(${JSON.stringify(label)})).click()`);await pause(100)}
try{
 await cdp('Page.enable');await cdp('Runtime.enable');
 for(const width of [390,768,1440,1920]){
  await cdp('Emulation.setDeviceMetricsOverride',{width,height:1000,deviceScaleFactor:1,mobile:width===390});
  await cdp('Page.navigate',{url:base+'/item/'+fixture.id});await wait('!!document.querySelector("app-item-placement .current-place")');
  assert(await fit(),`overflow ${width}`);assert.equal(await ev('!!document.querySelector(".placement-form")'),false);
  assert.equal(await ev('document.querySelector(".placement-history").open'),false);
  await ev(`document.querySelector('app-item-placement').scrollIntoView({block:'center',inline:'nearest'})`);await pause(150);
  await writeFile(`${out}/placement-active-${width}.png`,Buffer.from((await cdp('Page.captureScreenshot')).data,'base64'));
  await click('Modificar');await wait('!!document.querySelector(".placement-form")');assert(await fit());
  await ev(`document.querySelector('.placement-form').scrollIntoView({block:'center',inline:'nearest'})`);await pause(100);
  await writeFile(`${out}/placement-form-${width}.png`,Buffer.from((await cdp('Page.captureScreenshot')).data,'base64'));
  await click('Cancelar');console.log('PASS responsive',width,'compact active state, collapsed history, form fits');
 }
 await click('Modificar');const newDest='Mesa de pruebas '+Date.now();await fill('destination',newDest);
 // Unsaved movement fields use the existing navigation guard.
 await ev(`document.querySelector('a.nav-link[routerlink="/dashboard"]').click()`);await pause(150);assert(dialogs>0);assert(await ev(`location.pathname==='/item/${fixture.id}'`));
 await click('Guardar cambios');await wait('!document.querySelector(".placement-form")');assert.equal((await item()).currentPlacement.destination,newDest);
 await click('Modificar');await fill('destinationType','location');
 const options=await fetch('http://127.0.0.1:18089/api/inventory/options').then(r=>r.json());
 await choose(options.locations.find(l=>l.id===fixture.away).name);await click('Guardar cambios');await wait('!document.querySelector(".placement-form")');assert.equal((await item()).currentPlacement.locationId,fixture.away);
 await click('Modificar');await fill('destinationType','box');await choose(options.boxes.find(b=>b.id===fixture.target).name);await click('Guardar cambios');await wait('!document.querySelector(".placement-form")');
 assert.equal((await item()).currentPlacement.destinationBoxId,fixture.target);
 assert.equal((await ev(`document.querySelector('app-item-placement a.destination').getAttribute('href')`)).replaceAll('-',''),('/boxes/'+fixture.targetCode).replaceAll('-',''));
 // Destination container shows visitors separately, membership remains at home.
 await ev(`document.querySelector('app-item-placement a.destination').click()`);await wait('!!document.querySelector(".visiting-items")');
 assert(await ev(`document.querySelector('.visiting-items').textContent.includes('QA travelling item')`));
 await cdp('Page.navigate',{url:base+'/item/'+fixture.id});await wait('!!document.querySelector(".current-place")');
 await click('Registrar devolución');assert(await ev('!!document.querySelector(".return-confirm")'));
 await click('Confirmar devolución');await wait('!!document.querySelector(".start-placement")');
 let current=await item();assert.equal(current.currentPlacement,null);assert.equal(current.box.id,fixture.owner);
 await ev(`document.querySelector('.placement-history summary').click()`);assert(await ev('document.querySelector(".placement-history").open'));
 await click('Desplazar temporalmente');await fill('destination','Escritorio de prueba');await click('Registrar salida');await wait('!!document.querySelector(".current-place")');
 current=await item();assert.equal(current.box.id,fixture.owner);assert.equal(current.currentPlacement.destination,'Escritorio de prueba');
 await cdp('Page.navigate',{url:base+'/inventory?onlyAway=true&layout=table'});await wait('!!document.querySelector(".away-badge")');assert(await fit());
 assert.equal(errors.length,0,errors.join(';'));
 console.log('PASS browser: text/location/container editing, destination navigation, visitors, draft guard, return confirmation, history, new departure and away filter');
}finally{await cdp('Page.close');ws.close()}
