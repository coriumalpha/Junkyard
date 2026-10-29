// Mutation tests: disposable database only, never production.
const assert = require('node:assert/strict');
const {mkdirSync,writeFileSync} = require('node:fs');
const base='http://127.0.0.1:18089';
const suffix=Date.now();
const startedAt=new Date(Date.now()-86400000).toISOString();
const future=new Date(Date.now()+7*86400000).toISOString().slice(0,10);
async function call(path,method='GET',data){
 const r=await fetch(base+path,{method,headers:{'content-type':'application/json'},body:data===undefined?undefined:JSON.stringify(data)});
 const text=await r.text();let body;try{body=JSON.parse(text)}catch{body=text.slice(0,100)}return {status:r.status,body};
}
async function ok(path,method='GET',data){const r=await call(path,method,data);assert.equal(r.status,200,`${method} ${path}: ${JSON.stringify(r.body)}`);return r.body;}
(async()=>{
 for(let n=0;n<60;n++){try{if((await fetch(base+'/health')).ok)break}catch{}await new Promise(r=>setTimeout(r,200))}
 const home=await ok('/api/locations','POST',{name:'QA home '+suffix});
 const away=await ok('/api/locations','POST',{name:'QA away '+suffix});
 const makeBox=(name,locationId)=>ok('/api/boxes','POST',{name,locationId,containerType:'box',status:'Active'});
 const owner=await makeBox('QA owner '+suffix,home.id);
 const target=await makeBox('QA destination '+suffix,away.id);
 const input={name:'QA travelling item '+suffix,category:'Otros',quantity:1,boxId:owner.id,tagIds:[]};
 let item=await ok('/api/items','POST',input);const id=item.id,code=item.code;
 const route=`/api/items/${id}/placements`;
 assert.equal(item.currentPlacement,null);assert.equal(item.placementHistory.length,0);
 const text={destinationType:'text',destination:'Escritorio QA '+suffix,startedAt,expectedReturnDate:future,notes:'Prueba sintética'};
 for(const payload of [{...text,destination:''},{...text,destination:'x'.repeat(161)},{...text,notes:'x'.repeat(1001)},
   {...text,destinationType:'invalid'},{...text,destinationType:'location',locationId:2147483647},
   {...text,destinationType:'box',destinationBoxId:owner.id},
   {...text,startedAt:new Date(Date.now()+86400000).toISOString()}, {...text,expectedReturnDate:'1900-01-01'}]){
   assert.equal((await call(route,'POST',payload)).status,400);
 }
 item=await ok(route,'POST',text);let p=item.currentPlacement;
 assert.equal(item.box.id,owner.id);assert.equal(item.code,code);assert.equal(p.events.length,1);assert.equal(p.destination,text.destination);
 assert.equal((await call(route,'POST',text)).status,400);
 assert((await ok('/api/inventory/live?onlyAway=true')).items.some(i=>i.id===id));
 assert((await ok('/api/inventory/live?q='+encodeURIComponent(text.destination))).items.some(i=>i.id===id));
 assert((await ok('/api/boxes/'+owner.code)).items.find(i=>i.id===id).currentPlacement);
 assert.equal((await call('/api/items/'+id,'PUT',{...input,boxId:target.id})).status,400);
 assert.equal((await call('/api/items/bulk','POST',{itemIds:[id],moveToBoxId:target.id})).status,400);
 assert.equal((await call('/api/boxes/'+owner.id+'/archive','POST',{targetBoxId:target.id})).status,400);
 const stale=p.version;
 item=await ok(route+'/'+p.id,'PUT',{...text,destinationType:'location',locationId:away.id,version:p.version});p=item.currentPlacement;
 assert.equal(p.destination,away.name);assert.equal(p.events.length,2);assert.equal(item.box.id,owner.id);
 assert((await ok('/api/inventory/live?locationId='+away.id)).items.some(i=>i.id===id));
 assert(!(await ok('/api/inventory/live?locationId='+home.id)).items.some(i=>i.id===id));
 assert.equal((await call('/api/locations/'+away.id,'DELETE')).status,400);
 assert.equal((await call(route+'/'+p.id,'PUT',{...text,version:stale})).status,400);
 assert.equal((await call(route+'/'+p.id+'/return','POST',{version:stale})).status,409);
 item=await ok(route+'/'+p.id,'PUT',{...text,destinationType:'box',destinationBoxId:target.id,version:p.version});p=item.currentPlacement;
 assert((await ok('/api/boxes/'+target.code)).visitingItems.some(i=>i.id===id));
 assert.equal(p.destinationBoxCode,target.code);assert.equal(p.currentLocationId,away.id);assert.equal(p.events.length,3);
 assert.equal((await call('/api/boxes/'+target.id+'/archive','POST',{})).status,400);
 assert((await ok('/api/inventory/live?locationId='+away.id)).items.some(i=>i.id===id));
 item=await ok(route+'/'+p.id+'/return','POST',{version:p.version});
 assert(!(await ok('/api/boxes/'+target.code)).visitingItems.some(i=>i.id===id));
 assert.equal(item.currentPlacement,null);assert.equal(item.box.id,owner.id);assert.equal(item.placementHistory.length,1);
 assert.equal(item.placementHistory[0].events.length,4);assert(item.placementHistory[0].returnedAt);
 assert.equal((await call(route+'/'+p.id+'/return','POST',{version:p.version})).status,409);
 assert(!(await ok('/api/inventory/live?onlyAway=true')).items.some(i=>i.id===id));
 assert((await ok('/api/inventory/live?locationId='+home.id)).items.some(i=>i.id===id));
 assert(!(await ok('/api/inventory/live?locationId='+away.id)).items.some(i=>i.id===id));
 // A definitive move is separate and available after return.
 item=await ok('/api/items/'+id,'PUT',{...input,boxId:target.id});assert.equal(item.box.id,target.id);
 item=await ok('/api/items/'+id,'PUT',input);assert.equal(item.box.id,owner.id);
 // Two overlapping requests cannot create two active stays.
 const raced=await Promise.all([call(route,'POST',text),call(route,'POST',text)]);
 assert.deepEqual(raced.map(r=>r.status).sort(),[200,400]);
 item=await ok('/api/items/'+id);p=item.currentPlacement;
 assert.equal(item.placementHistory.length,2);
 // Repeated return cannot end a later displacement.
 assert.equal((await call(route+'/'+item.placementHistory.find(stay=>stay.returnedAt).id+'/return','POST',{version:p.version})).status,409);
 // Leave an active synthetic item for browser QA, no production changes.
 mkdirSync('backups/temporary-placement-20261029',{recursive:true});
 writeFileSync('backups/temporary-placement-20261029/fixture.json',JSON.stringify({id,owner:owner.id,ownerCode:owner.code,target:target.id,targetCode:target.code,home:home.id,away:away.id}));
 console.log('PASS: destinations, dates, membership, filters, history, editing, stale requests, duplicate/racing creation, return and independent permanent move');
})().catch(e=>{console.error(e);process.exitCode=1});
