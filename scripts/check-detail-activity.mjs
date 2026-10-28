// Mutates the isolated QA copy only; production never receives these requests.
import assert from 'node:assert/strict';import{mkdir,writeFile}from'node:fs/promises';
const api='http://127.0.0.1:18089',base='http://127.0.0.1:18088',prefix='QA integrado '+Date.now();
const req=async(path,method='GET',data)=>{const r=await fetch(api+path,{method,headers:{'Content-Type':'application/json'},body:data?JSON.stringify(data):undefined});assert(r.ok,path);return r.status===204?null:r.json()};
const t=await fetch('http://127.0.0.1:9224/json/new?about:blank',{method:'PUT'}).then(r=>r.json()),ws=new WebSocket(t.webSocketDebuggerUrl);await new Promise(r=>ws.addEventListener('open',r,{once:true}));let seq=0;const calls=new Map();
const cdp=(method,params={})=>new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(Error(method)),15000);calls.set(++seq,m=>{clearTimeout(timer);m.error?reject(Error(JSON.stringify(m.error))):resolve(m.result)});ws.send(JSON.stringify({id:seq,method,params}));});ws.addEventListener('message',({data})=>{const m=JSON.parse(data);if(calls.has(m.id)){calls.get(m.id)(m);calls.delete(m.id)}if(m.method==='Page.javascriptDialogOpening')void cdp('Page.handleJavaScriptDialog',{accept:true});});
const ev=async expression=>{const r=await cdp('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw Error(JSON.stringify(r.exceptionDetails));return r.result.value;},pause=ms=>new Promise(r=>setTimeout(r,ms));
async function wait(expression){for(let i=0;i<100;i++){if(await ev(expression))return;await pause(100)}throw Error(expression)}
async function click(selector){await ev(`document.querySelector(${JSON.stringify(selector)}).click()`);await pause(120)}
async function fill(selector,value){await ev(`(()=>{const e=document.querySelector(${JSON.stringify(selector)});e.value=${JSON.stringify(value)};e.dispatchEvent(new Event('input',{bubbles:true}));})()`)}
async function button(selector,text){await ev(`Array.from(document.querySelectorAll(${JSON.stringify(selector)})).find(b=>b.textContent.includes(${JSON.stringify(text)})).click()`);await pause(150)}
const out='backups/ui-activity-zoom-20261027/screens';await mkdir(out,{recursive:true});const shot=async name=>writeFile(out+'/'+name+'.png',Buffer.from((await cdp('Page.captureScreenshot')).data,'base64'));
try{
 await cdp('Page.enable');await cdp('Emulation.setDeviceMetricsOverride',{width:1440,height:1000,deviceScaleFactor:1,mobile:false});await cdp('Page.navigate',{url:base+'/item/1'});await wait('!!document.querySelector(".integrated-activity")');
 assert(await ev('!document.querySelector(".detail-tabs") && !!document.querySelector(".main-photo") && !!document.querySelector("#ficha-tareas") && !!document.querySelector("#ficha-comentarios")'),'single page');
 await click('.detail-jump-links button');assert(await ev('document.activeElement.id==="ficha-tareas"'),'jump focus');
 await click('[aria-labelledby=ficha-tareas] summary');await fill('.integrated-activity .quick-action-form input',prefix+' tarea');await button('.integrated-activity .quick-action-form button','Añadir tarea');await wait(`document.querySelector('.integrated-activity .linked-list').textContent.includes(${JSON.stringify(prefix+' tarea')})`);
 await button('.integrated-activity .linked-row button','Editar');await fill('.integrated-activity .linked-edit input',prefix+' tarea editada');await button('.integrated-activity .linked-edit button','Guardar');await wait('!document.querySelector(".integrated-activity .linked-edit")');
 await click('[aria-labelledby=ficha-comentarios] summary');await fill('.integrated-activity .comment-form textarea',prefix+' comentario');await button('.integrated-activity .comment-form button','Guardar comentario');await wait(`document.querySelector('.integrated-activity .comment-list').textContent.includes(${JSON.stringify(prefix+' comentario')})`);
 await button('.integrated-activity .comment-row button','Editar');await fill('.integrated-activity .comment-row textarea',prefix+' comentario editado');await button('.integrated-activity .comment-row button','Guardar');await wait('!document.querySelector(".integrated-activity .comment-row textarea")');
 // Collapse composers; saved rows remain visible without a tab or disclosure.
 await click('[aria-labelledby=ficha-tareas] summary');await click('[aria-labelledby=ficha-comentarios] summary');
 for(const width of [390,768,1440,2560]){
  await cdp('Emulation.setDeviceMetricsOverride',{width,height:1000,deviceScaleFactor:1,mobile:width===390});await pause(500);await click('.detail-jump-links button');await pause(250);assert(await ev('document.querySelector(".detail-shell").getBoundingClientRect().left>=0'),'no horizontal scroll from jump');assert(await ev('document.documentElement.scrollWidth<=innerWidth'),'overflow '+width);
  const side=await ev(`(()=>{const a=document.querySelectorAll('.activity-section');return Math.abs(a[0].getBoundingClientRect().top-a[1].getBoundingClientRect().top)<2})()`);assert.equal(side,width>1100,'activity columns '+width);if([390,1440].includes(width))await shot('activity-'+width);
 }
 await button('.integrated-activity .linked-row button','Completar');await wait(`!document.querySelector('.integrated-activity .linked-list').textContent.includes(${JSON.stringify(prefix)})`);
 await button('.integrated-activity .comment-row button','Eliminar');await wait(`!document.querySelector('.integrated-activity .comment-list').textContent.includes(${JSON.stringify(prefix)})`);
 await click('[aria-labelledby=ficha-tareas] summary');await fill('.integrated-activity .quick-action-form input',prefix+' borrar');await button('.integrated-activity .quick-action-form button','Añadir tarea');await wait(`document.querySelector('.integrated-activity .linked-list').textContent.includes(${JSON.stringify(prefix+' borrar')})`);await button('.integrated-activity .linked-row button','Eliminar');await wait(`!document.querySelector('.integrated-activity .linked-list').textContent.includes(${JSON.stringify(prefix)})`);
 assert(await ev('!!document.querySelector(".main-photo")'),'photo remains alongside activity');
 console.log('PASS integrated task/comment create, edit, complete/delete; jump focus; visible together; two-column desktop/stacked mobile; 390/768/1440/2560');
}finally{
 await cdp('Page.close');ws.close();const d=await req('/api/items/1');for(const type of ['actions','comments'])for(const r of d[type])if((r.title+' '+r.description).includes(prefix))await req('/api/'+type+'/'+r.id,'DELETE');
}
