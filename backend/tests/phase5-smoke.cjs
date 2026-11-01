// Run against an isolated, migrated local validation API: FLOWOPS_API_URL=http://localhost:5155 node backend/tests/phase5-smoke.cjs
// Creates synthetic accounts and an organization, then soft-deactivates that organization. Never target a production database.
const assert = require('node:assert/strict');
const { createRequire } = require('node:module');
const { randomUUID } = require('node:crypto');
const path = require('node:path');
const requireFrontend = createRequire(path.resolve(__dirname, '../../frontend/package.json'));
const { HubConnectionBuilder, LogLevel } = requireFrontend('@microsoft/signalr');
const base = process.env.FLOWOPS_API_URL || 'http://localhost:5155';
if (!['localhost', '127.0.0.1'].includes(new URL(base).hostname)) throw new Error('Smoke test requires a local API.');
const origin = 'http://localhost:4200';
const run = randomUUID().slice(0, 8);
const password = randomUUID() + randomUUID();
let connection;
async function request(method, route, token, body, expected = 200) {
  const response = await fetch(base + route, {
    method, headers: { Origin: origin, ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(15000),
  });
  if (response.status !== expected) throw new Error(`${method} ${route}: expected ${expected}, got ${response.status}: ${await response.text()}`);
  return response.status === 204 ? null : response.json();
}
(async () => {
  const register = label => request('POST', '/api/auth/register', null,
    { email: `phase5-${run}-${label}@example.test`, fullName: `Phase 5 ${label}`, password }, 201);
  const admin = await register('admin');
  const recipient = await register('recipient');
  const organization = await request('POST', '/api/organizations', admin.accessToken,
    { name: `Phase 5 smoke ${run}`, slug: `phase5-smoke-${run}`, description: 'Synthetic local verification' }, 201);
  await request('POST', `/api/organizations/${organization.id}/members`, admin.accessToken,
    { email: recipient.email, role: 'Engineer' });
  const project = await request('POST', `/api/organizations/${organization.id}/projects`, admin.accessToken,
    { name: 'Notification smoke', key: 'P5', description: null }, 201);
  connection = new HubConnectionBuilder().withUrl(`${base}/api/hubs/notifications`,
    { accessTokenFactory: () => recipient.accessToken, headers: { Origin: origin } })
    .configureLogging(LogLevel.None).build();
  await connection.start();
  let resolveHint;
  connection.on('NotificationsChanged', () => resolveHint?.());
  async function withHint(action) {
    let timer;
    const hint = new Promise((resolve, reject) => { resolveHint = resolve; timer = setTimeout(() => reject(new Error('No live notification hint')), 10000); });
    try { const result = await action(); await hint; return result; }
    finally { clearTimeout(timer); resolveHint = undefined; }
  }
  const route = `/api/organizations/${organization.id}/projects/${project.id}/work-items`;
  const dueAtUtc = '2026-12-01T12:00:00.000Z';
  const item = await withHint(() => request('POST', route, admin.accessToken,
    { type: 'Task', title: 'Verify durable live notifications', description: null, priority: 'High', assigneeUserId: recipient.userId, dueAtUtc }, 201));
  assert.match(item.createdAtUtc, /Z$/); assert.match(item.dueAtUtc, /Z$/);
  assert.equal(Date.parse(item.dueAtUtc), Date.parse(dueAtUtc));
  const page = await request('GET', '/api/notifications', recipient.accessToken);
  assert.equal(page.totalCount, 1); assert.equal(page.items[0].kind, 'WorkItem.Assigned');
  await request('PUT', `/api/notifications/${page.items[0].id}/read`, admin.accessToken, {}, 404);
  await withHint(() => request('PUT', `/api/notifications/${page.items[0].id}/read`, recipient.accessToken, {}, 204));
  assert.equal((await request('GET', '/api/notifications/summary', recipient.accessToken)).unreadCount, 0);
  await connection.stop();
  await request('POST', `${route}/${item.id}/transitions`, admin.accessToken, { status: 'Triaged' });
  await request('POST', `${route}/${item.id}/comments`, admin.accessToken, { body: 'Persist this while the recipient is offline.' }, 201);
  const offline = await request('GET', '/api/notifications?pageSize=1&page=2', recipient.accessToken);
  assert.equal(offline.totalCount, 3); assert.equal(offline.items.length, 1);
  assert.equal((await request('GET', '/api/notifications/summary', recipient.accessToken)).unreadCount, 2);
  await request('PUT', '/api/notifications/read-all', recipient.accessToken, {}, 204);
  assert.equal((await request('GET', '/api/notifications/summary', recipient.accessToken)).unreadCount, 0);
  await request('DELETE', `/api/organizations/${organization.id}/members/${recipient.userId}`, admin.accessToken, undefined, 204);
  assert.equal((await request('GET', '/api/notifications', recipient.accessToken)).totalCount, 0);
  await request('DELETE', `/api/organizations/${organization.id}`, admin.accessToken, undefined, 204);
  console.log(JSON.stringify({ passed: true, run, checks: ['SQL Server persistence', 'live SignalR recipient hint',
    'UTC timestamp round-trip', 'ownership enforcement', 'read state and unread counts', 'offline recovery', 'pagination', 'membership revocation'],
    cleanup: 'Synthetic organization soft-deactivated; audit history and synthetic accounts retained.' }, null, 2));
})().catch(error => { console.error(error.message); process.exitCode = 1; }).finally(async () => { await connection?.stop(); });
