// Run after phase6-smoke against the same isolated local API. The final check
// intentionally consumes the authentication rate limit; do not share a dev API.
const assert = require('node:assert/strict');
const base = 'http://localhost:5156';
async function send(path, options = {}) {
  return fetch(base + path, { ...options, signal: AbortSignal.timeout(15000) });
}
(async () => {
  assert.equal((await send('/health')).status, 200);
  for (const route of ['/api/organizations', '/api/notifications', '/api/auth/me', '/api/hubs/notifications/negotiate?negotiateVersion=1']) {
    const response = await send(route, route.includes('negotiate') ? { method: 'POST' } : {});
    assert.equal(response.status, 401, route);
  }
  const oversized = await send('/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: 'nobody@example.test', password: 'x'.repeat(17000) }) });
  assert.equal(oversized.status, 413, 'Kestrel must enforce the real 16 KiB authentication body limit');
  const forbidden = await send('/api/hubs/notifications/negotiate?negotiateVersion=1', {
    method: 'POST', headers: { Origin: 'https://attacker.example' } });
  assert.equal(forbidden.status, 403);
  const cors = await send('/api/auth/login', { method: 'OPTIONS', headers: {
    Origin: 'https://attacker.example', 'Access-Control-Request-Method': 'POST' } });
  assert.equal(cors.headers.get('Access-Control-Allow-Origin'), null);
  let limited = false;
  for (let attempt = 0; attempt < 11; attempt++) {
    const response = await send('/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: 'nobody@example.test', password: 'incorrect password' }) });
    if (response.status === 429) { limited = true; break; }
    assert.equal(response.status, 401);
    assert.doesNotMatch(await response.text(), /SqlException|StackTrace|PasswordHash/);
  }
  assert.ok(limited, 'authentication attempts must be bounded');
  console.log('PASS: real Kestrel body limit, protected routes, hub origin, CORS, sanitized errors and authentication rate limit.');
})().catch(error => { console.error(error.message); process.exitCode = 1; });
