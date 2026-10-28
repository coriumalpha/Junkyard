const assert = require('node:assert/strict');
const http = require('node:http');
const {spawn} = require('node:child_process');
(async () => {
  const upstream = http.createServer((req, res) => {res.writeHead(200); res.end('{}');});
  await new Promise(resolve => upstream.listen(0, '127.0.0.1', resolve));
  try {
    for (const trusted of ['127.0.0.1', '192.0.2.42']) {
      const child = spawn('node', ['scripts/serve-secure.mjs'], {env: {...process.env,
        JUNKYARD_PUBLIC_ORIGIN: 'https://inventory.example.com', JUNKYARD_ORIGIN_HTTP: 'true',
        JUNKYARD_HTTPS_HOST: '127.0.0.1', JUNKYARD_HTTPS_PORT: '18445',
        JUNKYARD_API: `http://127.0.0.1:${upstream.address().port}`, JUNKYARD_TRUSTED_PROXY_IPS: trusted
      }, stdio: 'ignore'});
      try {
        const call = headers => new Promise((resolve, reject) => {
          const req = http.request({hostname: '127.0.0.1', port: 18445, path: '/api/auth/session', headers}, res => {
            res.resume(); res.on('end', () => resolve({status: res.statusCode, headers: res.headers}));
          }); req.on('error', reject); req.end();
        });
        let ready = false;
        for (let n = 0; n < 40; n++) {
          try {await call({}); ready = true; break;} catch {await new Promise(r => setTimeout(r, 50));}
        }
        assert(ready, 'origin ready');
        assert.equal((await call({})).status, 308);
        assert.equal((await call({host: 'inventory.example.com', 'x-forwarded-proto': 'http'})).status, 308);
        assert.equal((await call({host: 'wrong.example', 'x-forwarded-proto': 'https'})).status, 308);
        const r = await call({host: 'inventory.example.com', 'x-forwarded-proto': 'https', 'x-forwarded-for': '192.0.2.42'});
        assert.equal(r.status, trusted === '127.0.0.1' ? 200 : 308);
        if (r.status === 308) assert.equal(r.headers.location, 'https://inventory.example.com/api/auth/session');
      } finally {
        const exited = new Promise(r => child.once('exit', r)); child.kill(); await exited;
      }
    }
  } finally {await new Promise(r => upstream.close(r));}
  console.log('PASS: trusted proxy only, HTTPS and host required, spoofed forwarding rejected');
})().catch(e => {console.error(e); process.exitCode = 1;});
