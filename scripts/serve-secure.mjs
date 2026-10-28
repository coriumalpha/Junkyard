// Production Angular + same-origin HTTPS proxy. No passwords or inventory data on disk here.
import https from 'node:https';
import http from 'node:http';
import {createReadStream,readFileSync,statSync} from 'node:fs';
import {resolve,extname,sep} from 'node:path';
const root=resolve(process.env.JUNKYARD_SPA_ROOT || 'spikes/angular-inventory-spike/dist/angular-inventory-spike/browser');
const cert=process.env.JUNKYARD_TLS_CERT, key=process.env.JUNKYARD_TLS_KEY;
const originMode=process.env.JUNKYARD_ORIGIN_HTTP==='true';
const publicOrigin=new URL(process.env.JUNKYARD_PUBLIC_ORIGIN || 'https://inventory.example.com');
const trustedPeers=(process.env.JUNKYARD_TRUSTED_PROXY_IPS || '').split(',');
if(!originMode && (!cert || !key))throw new Error('Configure JUNKYARD_TLS_CERT and JUNKYARD_TLS_KEY');
const types={'.html':'text/html; charset=utf-8','.js':'text/javascript; charset=utf-8','.css':'text/css; charset=utf-8','.png':'image/png','.svg':'image/svg+xml','.jpg':'image/jpeg','.webp':'image/webp','.ico':'image/x-icon','.woff2':'font/woff2','.json':'application/json'};
const backend=new URL(process.env.JUNKYARD_API || 'http://127.0.0.1:8089');
const handler=(req,res)=>{
 if(originMode){
  const peer=(req.socket.remoteAddress || '').replace(/^::ffff:/,'');
  if(!trustedPeers.includes(peer) || req.headers['x-forwarded-proto']!=='https' || req.headers.host!==publicOrigin.host){
   const target=new URL(publicOrigin);const path=new URL(req.url,'http://local');target.pathname=path.pathname;target.search=path.search;
   res.writeHead(308,{Location:target.href,'Cache-Control':'no-store'});res.end();return;
  }
 }
 res.setHeader('X-Content-Type-Options','nosniff');res.setHeader('X-Frame-Options','DENY');res.setHeader('Referrer-Policy','same-origin');res.setHeader('Cache-Control','no-store');
 let path;try{path=decodeURIComponent(new URL(req.url,'https://local').pathname);}catch{res.writeHead(400);res.end();return;}
 if(/^\/(api|uploads|photo-derivatives)(\/|$)/.test(path)){
  const headers={...req.headers,host:backend.host,'x-forwarded-proto':'https'};
  delete headers['x-forwarded-for'];delete headers.forwarded;delete headers['x-forwarded-host'];
  const upstream=http.request(backend,{method:req.method,path:req.url,headers},incoming=>{res.writeHead(incoming.statusCode,incoming.headers);incoming.pipe(res);});
  upstream.on('error',()=>{if(!res.headersSent)res.writeHead(502,{'Content-Type':'application/json'});res.end('{"error":"Servidor temporalmente no disponible"}');});
  req.on('aborted',()=>upstream.destroy());req.pipe(upstream);return;
 }
 if(req.method!=='GET' && req.method!=='HEAD'){res.writeHead(405);res.end();return;}
 let file=resolve(root,'.'+path);
 if(file!==root && !file.startsWith(root+sep)){res.writeHead(403);res.end();return;}
 try{if(!statSync(file).isFile())throw Error();}catch{if(extname(path)){res.writeHead(404);res.end();return;}file=resolve(root,'index.html');}
 res.setHeader('Content-Type',types[extname(file)]||'application/octet-stream');
 if(req.method==='HEAD'){res.end();return;}
 createReadStream(file).on('error',()=>{if(!res.headersSent)res.writeHead(404);res.end();}).pipe(res);
};
const server=originMode ? http.createServer(handler) : https.createServer({cert:readFileSync(cert),key:readFileSync(key),minVersion:'TLSv1.2'},handler);
server.listen(Number(process.env.JUNKYARD_HTTPS_PORT || 8443),process.env.JUNKYARD_HTTPS_HOST || '127.0.0.1',()=>console.log(originMode ? 'Junkyard: origen privado para NPM listo' : 'Junkyard HTTPS listo'));
