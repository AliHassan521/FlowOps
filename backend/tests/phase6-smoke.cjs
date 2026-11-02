// Run against a migrated, isolated local API with SlaWorker:Enabled=true and IntervalSeconds=1.
// This deliberately observes a real two-minute SLA; no test clock or production bypass endpoint is exposed.
const assert = require('node:assert/strict');
const { createRequire } = require('node:module');
const { randomUUID } = require('node:crypto');
const path = require('node:path');
const { HubConnectionBuilder, LogLevel } = createRequire(path.resolve(__dirname, '../../frontend/package.json'))('@microsoft/signalr');
const base = process.env.FLOWOPS_API_URL || 'http://localhost:5156';
if (!['localhost', '127.0.0.1'].includes(new URL(base).hostname)) throw new Error('Smoke test requires a local validation API.');
const run = randomUUID().slice(0, 8), password = randomUUID() + randomUUID();
let connection, cleanup;
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function request(method, route, token, body, expected = 200) {
  const response = await fetch(base + route, { method, headers: { Origin: 'http://localhost:4200',
    ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(20000) });
  if (response.status !== expected) throw new Error(`${method} ${route}: expected ${expected}, got ${response.status}: ${await response.text()}`);
  return response.status === 204 ? null : response.json();
}
async function waitForState(route, token, predicate, timeout = 90000) {
  const until = Date.now() + timeout;
  while (Date.now() < until) { const state = await request('GET', route, token); if (predicate(state.sla)) return state.sla; await delay(1000); }
  throw new Error('Timed out waiting for the hosted SLA worker.');
}
(async () => {
  const register = label => request('POST', '/api/auth/register', null,
    { email: `phase6-${run}-${label}@example.test`, fullName: `Phase 6 ${label}`, password }, 201);
  const admin = await register('admin'), recipient = await register('engineer');
  const org = await request('POST', '/api/organizations', admin.accessToken,
    { name: `Phase 6 smoke ${run}`, slug: `phase6-smoke-${run}`, description: 'Synthetic local worker verification' }, 201);
  cleanup = () => request('DELETE', `/api/organizations/${org.id}`, admin.accessToken, undefined, 204);
  await request('POST', `/api/organizations/${org.id}/members`, admin.accessToken, { email: recipient.email, role: 'Engineer' });
  const project = await request('POST', `/api/organizations/${org.id}/projects`, admin.accessToken,
    { name: 'SLA validation', key: 'SLA', description: null }, 201);
  const policy = await request('POST', `/api/organizations/${org.id}/sla-policies`, admin.accessToken,
    { name: 'Two-minute validation', durationMinutes: 2, warningBeforeMinutes: 1 }, 201);
  const items = `/api/organizations/${org.id}/projects/${project.id}/work-items`;
  const createItem = title => request('POST', items, admin.accessToken,
    { type: 'Task', title, description: null, priority: 'High', assigneeUserId: recipient.userId, dueAtUtc: null }, 201);
  const item = await createItem('Observe the real SLA clock');
  const route = `${items}/${item.id}/sla`;
  const initial = await request('POST', route, admin.accessToken, { policyId: policy.id }, 201);
  assert.equal(Date.parse(initial.sla.deadlineAtUtc) - Date.parse(initial.sla.startedAtUtc), 120000);
  assert.equal(Date.parse(initial.sla.warningAtUtc) - Date.parse(initial.sla.startedAtUtc), 60000);
  let hints = 0;
  connection = new HubConnectionBuilder().withUrl(`${base}/api/hubs/notifications`,
    { accessTokenFactory: () => recipient.accessToken, headers: { Origin: 'http://localhost:4200' } })
    .configureLogging(LogLevel.None).build();
  connection.on('NotificationsChanged', () => hints++); await connection.start();
  const resolveItem = async id => {
    for (const status of ['Triaged', 'Assigned', 'InProgress', 'CodeReview', 'QA', 'Resolved'])
      await request('POST', `${items}/${id}/transitions`, admin.accessToken, { status });
  };
  const metItem = await createItem('Complete before the SLA deadline');
  await request('POST', `${items}/${metItem.id}/sla`, admin.accessToken, { policyId: policy.id }, 201);
  await resolveItem(metItem.id);
  assert.equal((await request('GET', `${items}/${metItem.id}/sla`, recipient.accessToken)).sla.status, 'Met');
  await request('DELETE', `/api/organizations/${org.id}/sla-policies/${policy.id}`, admin.accessToken, undefined, 204);
  const hintsBeforeWarning = hints;
  console.log(JSON.stringify({ stage: 'Waiting for the real warning threshold', deadlineAtUtc: initial.sla.deadlineAtUtc }));
  const warned = await waitForState(route, recipient.accessToken, sla => sla.warnedAtUtc !== null);
  assert.equal(warned.status, 'AtRisk');
  await delay(300); assert.ok(hints > hintsBeforeWarning);
  const hintsBeforeBreach = hints;
  console.log(JSON.stringify({ stage: 'Warning persisted and delivered; waiting for breach' }));
  const breached = await waitForState(route, recipient.accessToken, sla => sla.breachedAtUtc !== null);
  assert.equal(breached.status, 'Breached'); assert.equal(breached.breachedAtUtc, initial.sla.deadlineAtUtc);
  await delay(3200); assert.ok(hints > hintsBeforeBreach);
  await resolveItem(item.id);
  const completed = (await request('GET', route, recipient.accessToken)).sla;
  assert.equal(completed.status, 'Breached'); assert.ok(completed.completedAtUtc);
  const notifications = await request('GET', '/api/notifications?pageSize=100', recipient.accessToken);
  assert.equal(notifications.items.filter(n => n.kind === 'Sla.Warning').length, 1);
  assert.equal(notifications.items.filter(n => n.kind === 'Sla.Breached').length, 1);
  const history = await request('GET', `${items}/${item.id}/history`, recipient.accessToken);
  assert.equal(history.items.filter(e => e.action === 'Sla.Breached' && e.actorUserId === null && e.actorName === 'System').length, 1);
  const summary = await request('GET', `/api/organizations/${org.id}/sla-summary`, recipient.accessToken);
  assert.equal(summary.breachedCount, 1); assert.equal(summary.metCount, 1);
  console.log(JSON.stringify({ passed: true, run, checks: ['real hosted worker ticks', 'SQL Server warning and breach persistence',
    'SignalR delivery', 'no duplicate events over repeated ticks', 'fixed deadline after policy archival', 'early and late resolution',
    'system audit actor', 'tenant-scoped SLA metrics'], cleanup: 'Synthetic organization soft-deactivated; synthetic accounts and audit history retained.' }, null, 2));
})().catch(error => { console.error(error.message); process.exitCode = 1; }).finally(async () => {
  await connection?.stop().catch(() => {});
  if (cleanup) await cleanup().catch(error => { console.error('Synthetic workspace cleanup failed: ' + error.message); process.exitCode = 1; });
});
