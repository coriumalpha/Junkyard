const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const ts=require('../spikes/angular-inventory-spike/node_modules/typescript');
const js=ts.transpileModule(fs.readFileSync('spikes/angular-inventory-spike/src/app/inventory-code.ts','utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS}}).outputText;
const ctx={exports:{}};vm.runInNewContext(js,ctx);const {formatInventoryCode:f,normalizeInventoryCodes:n}=ctx.exports;
for(const [input,expected] of [['it1','IT-000-001'],['CT-000007','CT-000-007'],['IT-000-001','IT-000-001'],[' CT 12 ','CT-000-012'],['IT-12345678901234567890','IT-12345678901234567-890'],['ABC-123','ABC-123'],['', '']])assert.equal(f(input),expected);
const input={code:'CT-7',path:'CT-7 · Caja',notes:'Conservar CT-7 literalmente',name:'CT-7 original',nested:[{itemCode:'IT001'}]};const out=n(input);assert.equal(out.path,'CT-000-007 · Caja');assert.equal(out.notes,input.notes);assert.equal(out.name,input.name);assert.equal(out.nested[0].itemCode,'IT-000-001');assert.equal(input.code,'CT-7');console.log('PASS códigos: variantes, idempotencia, precisión, rutas, texto libre intacto, sin mutación');
