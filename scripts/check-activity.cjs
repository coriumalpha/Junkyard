const assert=require('node:assert/strict');
const base='http://127.0.0.1:18089';
async function call(path,method='GET',data){const r=await fetch(base+path,{method,headers:{'Content-Type':'application/json'},body:data?JSON.stringify(data):undefined});const text=await r.text();return {status:r.status,body:text?JSON.parse(text):null};}
(async()=>{for(const comment of [false,true]){
 const route=comment?'comments':'actions';
 const created=await call(`/api/items/1/${route}`,'POST',comment?{text:'QA comentario'}:{title:'QA tarea',priority:3});assert.equal(created.status,200);const id=created.body.id;
 assert.equal((await call(`/api/${route}/${id}`,'PUT',comment?{text:'QA editado'}:{title:'QA editada',priority:4})).status,200);
 assert.equal((await call(`/api/${comment?'actions':'comments'}/${id}`,'DELETE')).status,404);
 assert.equal((await call(`/api/${route}/${id}`,'DELETE')).status,204);
 const detail=await call('/api/items/1');assert(!detail.body[route].some(x=>x.id===id));
 assert.equal((await call(`/api/${route}/${id}`,'DELETE')).status,404);
 assert.equal((await call(`/api/${route}/${id}`,'PUT',comment?{text:'No revivir'}:{title:'No revivir',priority:1})).status,404);
 }console.log('PASS: create, edit, kind isolation, soft delete, invisible on detail, cannot mutate deleted rows');})().catch(e=>{console.error(e);process.exitCode=1});
