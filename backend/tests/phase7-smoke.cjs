// Isolated local SQL Server/SignalR functional flow; observes a real two-minute incident SLA.
const assert = require('node:assert/strict');
const { randomUUID } = require('node:crypto');
const { createRequire } = require('node:module');
const path = require('node:path');
const { writeFileSync } = require('node:fs');
const { HubConnectionBuilder, LogLevel } = createRequire(path.resolve(__dirname, '../../frontend/package.json'))('@microsoft/signalr');
const base = process.env.FLOWOPS_API_URL || 'http://localhost:5144';
if (!['localhost', '127.0.0.1'].includes(new URL(base).hostname)) throw new Error('Only an isolated local validation API is allowed.');
const run = randomUUID().slice(0,8), password = randomUUID() + randomUUID();
const delay = ms => new Promise(resolve => setTimeout(resolve,ms));
let connection, cleanup;
async function request(method, route, token, body, expected = 200) {
  const response = await fetch(base + route, { method, headers: { Origin: 'http://localhost:4200',
    ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(body !== undefined ? { 'Content-Type':'application/json' } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(20000) });
  assert.equal(response.status,expected,`${method} ${route}: ${await response.clone().text()}`);
  return response.status === 204 ? null : response.json();
}
async function waitSla(route,token,predicate) {
  const until = Date.now() + 100000;
  while (Date.now() < until) { const state = await request('GET',route,token); if (predicate(state.sla)) return state.sla; await delay(1000); }
  throw new Error('Timed out waiting for incident SLA processing.');
}
(async()=>{
  const register = label => request('POST','/api/auth/register',null,{ email:`phase7-${run}-${label}@example.test`,fullName:`Phase 7 ${label}`,password },201);
  const admin = await register('admin'), owner = await register('owner'), outsider = await register('outsider');
  const org = await request('POST','/api/organizations',admin.accessToken,{name:`Phase 7 verification ${run}`,slug:`phase7-${run}`,description:'Synthetic incident functional verification'},201);
  cleanup = () => request('DELETE',`/api/organizations/${org.id}`,admin.accessToken,undefined,204);
  await request('POST',`/api/organizations/${org.id}/members`,admin.accessToken,{email:owner.email,role:'Engineer'});
  const project = await request('POST',`/api/organizations/${org.id}/projects`,admin.accessToken,{name:'Incident operations',key:'OPS',description:null},201);
  const endpoint = `/api/organizations/${org.id}/projects/${project.id}/incidents`;
  const create = title => request('POST',endpoint,admin.accessToken,{title,description:'Synthetic outage',affectedService:'Public API',severity:'Critical',ownerUserId:owner.userId},201);
  let incident = await create('Observe incident SLA and recovery');
  await request('POST',endpoint,admin.accessToken,{title:'Oversized incident',description:'x'.repeat(66000),affectedService:'API',severity:'High'},413);
  await request('GET',`${endpoint}/${incident.id}`,outsider.accessToken,undefined,404);
  const policy = await request('POST',`/api/organizations/${org.id}/sla-policies`,admin.accessToken,{name:'Two minute incident recovery',durationMinutes:2,warningBeforeMinutes:1},201);
  const attachments = await Promise.all([1,2].map(() => fetch(base + `${endpoint}/${incident.id}/sla`, {
    method:'POST',headers:{Authorization:`Bearer ${admin.accessToken}`,'Content-Type':'application/json'},
    body:JSON.stringify({expectedVersion:incident.version,policyId:policy.id}),signal:AbortSignal.timeout(20000)
  })));
  assert.deepEqual(attachments.map(response=>response.status).sort(),[201,409]);
  incident = await request('GET',`${endpoint}/${incident.id}`,admin.accessToken);
  const staleVersion = incident.version;
  incident = await request('PUT',`${endpoint}/${incident.id}`,admin.accessToken,{expectedVersion:incident.version,title:'API outage confirmed',description:null,affectedService:'Public API',severity:'Critical'});
  await request('PUT',`${endpoint}/${incident.id}`,admin.accessToken,{expectedVersion:staleVersion,title:'Old draft',description:null,affectedService:'API',severity:'Low'},409);
  const concurrent = await Promise.all(['Concurrent edit A','Concurrent edit B'].map(title => fetch(base + `${endpoint}/${incident.id}`,{method:'PUT',headers:{Authorization:`Bearer ${admin.accessToken}`,'Content-Type':'application/json'},body:JSON.stringify({expectedVersion:incident.version,title,description:null,affectedService:'Public API',severity:'Critical'})})));
  assert.deepEqual(concurrent.map(response=>response.status).sort(),[200,409]);
  await request('POST',`${endpoint}/${incident.id}/comments`,owner.accessToken,{body:'Investigating API errors'},201);
  incident = await request('GET',`${endpoint}/${incident.id}`,owner.accessToken);
  incident = await request('POST',`${endpoint}/${incident.id}/transitions`,owner.accessToken,{expectedVersion:incident.version,status:'Investigating'});
  await request('POST',`${endpoint}/${incident.id}/transitions`,owner.accessToken,{expectedVersion:incident.version,status:'Resolved',resolution:'Skip mitigation'},409);
  const workPath = `/api/organizations/${org.id}/projects/${project.id}/work-items`;
  const work = await request('POST',workPath,admin.accessToken,{type:'Task',title:'Legacy workflow regression',description:null,priority:'High',assigneeUserId:owner.userId},201);
  await request('POST',`${workPath}/${work.id}/sla`,admin.accessToken,{policyId:policy.id},201);
  let hints = 0;
  connection = new HubConnectionBuilder().withUrl(`${base}/api/hubs/notifications`,{accessTokenFactory:()=>owner.accessToken,headers:{Origin:'http://localhost:4200'}}).configureLogging(LogLevel.None).build();
  connection.on('NotificationsChanged',()=>hints++); await connection.start();
  const route = `${endpoint}/${incident.id}/sla`;
  console.log(JSON.stringify({stage:'Waiting for incident warning and breach',run}));
  await waitSla(route,owner.accessToken,s=>s.warnedAtUtc!==null);
  assert.equal((await request('GET',`${endpoint}/summary`,owner.accessToken)).atRiskCount,1);
  await waitSla(route,owner.accessToken,s=>s.breachedAtUtc!==null); await delay(2500); assert.ok(hints>=2);
  incident = await request('GET',`${endpoint}/${incident.id}`,owner.accessToken);
  incident = await request('POST',`${endpoint}/${incident.id}/transitions`,owner.accessToken,{expectedVersion:incident.version,status:'Mitigated'});
  await request('POST',`${endpoint}/${incident.id}/transitions`,owner.accessToken,{expectedVersion:incident.version,status:'Resolved'},400);
  incident = await request('POST',`${endpoint}/${incident.id}/transitions`,owner.accessToken,{expectedVersion:incident.version,status:'Resolved',resolution:'Rolled back and verified recovery',rootCause:'Bad configuration'});
  await request('POST',`${endpoint}/${incident.id}/transitions`,owner.accessToken,{expectedVersion:incident.version,status:'Closed'},403);
  incident = await request('POST',`${endpoint}/${incident.id}/transitions`,admin.accessToken,{expectedVersion:incident.version,status:'Closed'});
  for(const status of ['Triaged','Assigned','InProgress','CodeReview','QA','Resolved']) await request('POST',`${workPath}/${work.id}/transitions`,admin.accessToken,{status});
  const timeline = await request('GET',`${endpoint}/${incident.id}/timeline`,owner.accessToken);
  assert.equal(timeline.items.filter(e=>e.action==='Incident.SlaWarning').length,1);
  assert.equal(timeline.items.filter(e=>e.action==='Incident.SlaBreached'&&e.actorName==='System').length,1);
  const notifications = await request('GET','/api/notifications?pageSize=100',owner.accessToken);
  assert.equal(notifications.items.filter(n=>n.kind==='Incident.SlaWarning').length,1);
  assert.equal(notifications.items.filter(n=>n.kind==='Incident.SlaBreached').length,1);
  assert.ok(notifications.items.some(n=>n.incidentId===incident.id&&n.workItemId===null));
  assert.ok(notifications.items.some(n=>n.workItemId===work.id&&n.incidentId===null));
  const summary = await request('GET',`/api/organizations/${org.id}/incident-summary`,owner.accessToken);
  assert.equal(summary.resolvedCount,1);assert.equal(summary.breachedCount,1);
  if (process.env.FLOWOPS_FIXTURE_PATH) {
    await create('Browser functional verification');
    writeFileSync(process.env.FLOWOPS_FIXTURE_PATH,JSON.stringify({base,email:admin.email,password,organizationId:org.id,projectId:project.id,accessToken:admin.accessToken,endpoint},null,2));
  }
  console.log(JSON.stringify({passed:true,run,checks:['SQL Server lifecycle','stale and concurrent edits','concurrent SLA attachment','ownership and tenant boundaries','durable incident timeline','real warning and breach worker','SignalR hints','mixed notification resource navigation','legacy work-item regression','incident dashboard aggregates']},null,2));
})().catch(error=>{console.error(error.message);process.exitCode=1;}).finally(async()=>{
  await connection?.stop().catch(()=>{});
  if(cleanup && !(process.env.FLOWOPS_KEEP_FIXTURE==='1' && !process.exitCode)) await cleanup().catch(error=>{console.error(error.message);process.exitCode=1;});
});
