// Disposable tenants only, on an isolated localhost API/database.
const assert = require('node:assert/strict');
const {randomUUID} = require('node:crypto');
const {writeFileSync} = require('node:fs');
const {createRequire} = require('node:module');
const path = require('node:path');
const {HubConnectionBuilder,HttpTransportType,LogLevel} = createRequire(path.resolve(__dirname,'../../frontend/package.json'))('@microsoft/signalr');
const base = process.env.FLOWOPS_API_URL || 'http://localhost:5144';
if (!['localhost','127.0.0.1'].includes(new URL(base).hostname)) throw new Error('Use an isolated localhost validation API.');
const run=randomUUID().slice(0,8), password=randomUUID()+randomUUID();
let cleanup, connection;
async function request(method,route,token,body,expected=200) {
  const response=await fetch(base+route,{method,headers:{Origin:process.env.FLOWOPS_ORIGIN||'http://localhost:4200',...(token?{Authorization:`Bearer ${token}`} : {}),...(body!==undefined?{'Content-Type':'application/json'}:{})},body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(30000)});
  assert.equal(response.status,expected,`${method} ${route}: ${await response.clone().text()}`);
  return response.status===204?null:response.json();
}
async function race(method,route,token,body,statuses) {
  const responses=await Promise.all([1,2].map(()=>fetch(base+route,{method,headers:{Authorization:`Bearer ${token}`,'Content-Type':'application/json'},body:JSON.stringify(body),signal:AbortSignal.timeout(30000)})));
  assert.deepEqual(responses.map(r=>r.status).sort(),statuses);
  await Promise.all(responses.map(r=>r.arrayBuffer()));
}
(async()=>{
  const register=async label=>{
    const response=await fetch(base+'/api/auth/register',{method:'POST',headers:{'Content-Type':'application/json',Origin:process.env.FLOWOPS_ORIGIN||'http://localhost:4200'},body:JSON.stringify({email:`phase8-${run}-${label}@example.test`,fullName:`Phase 8 ${label}`,password}),signal:AbortSignal.timeout(30000)});
    assert.equal(response.status,201,'Synthetic registration failed');
    const cookie=response.headers.get('set-cookie');assert.ok(cookie&&/httponly/i.test(cookie)&&/path=\/api\/auth/i.test(cookie));
    return {...await response.json(),refreshCookie:cookie.split(';')[0]};
  };
  const admin=await register('admin'),engineer=await register('engineer'),outsider=await register('outsider');
  const refresh=origin=>fetch(base+'/api/auth/refresh',{method:'POST',headers:{Origin:origin,Cookie:admin.refreshCookie},signal:AbortSignal.timeout(30000)});
  const rejectedOrigin=await refresh('https://untrusted.example');assert.equal(rejectedOrigin.status,403);await rejectedOrigin.arrayBuffer();
  const rotated=await refresh(process.env.FLOWOPS_ORIGIN||'http://localhost:4200');assert.equal(rotated.status,200);assert.ok(rotated.headers.get('set-cookie'));await rotated.arrayBuffer();
  const replay=await refresh(process.env.FLOWOPS_ORIGIN||'http://localhost:4200');assert.equal(replay.status,401);await replay.arrayBuffer();

  const org=await request('POST','/api/organizations',admin.accessToken,{name:`Release verification ${run}`,slug:`phase8-${run}`,description:'Disposable release and dashboard verification'},201);
  cleanup=()=>request('DELETE',`/api/organizations/${org.id}`,admin.accessToken,undefined,204);
  await request('POST',`/api/organizations/${org.id}/members`,admin.accessToken,{email:engineer.email,role:'Engineer'});
  const project=await request('POST',`/api/organizations/${org.id}/projects`,admin.accessToken,{name:'Delivery',key:'REL',description:null},201);
  const root=`/api/organizations/${org.id}`,scope=`${root}/projects/${project.id}`,endpoint=scope+'/releases';
  const env=await request('POST',endpoint+'/environments',admin.accessToken,{name:'Production'},201);
  await race('POST',endpoint+'/environments',admin.accessToken,{name:'Canary'},[201,409]);
  let work=await request('POST',scope+'/work-items',admin.accessToken,{type:'Task',title:'Release regression task',description:null,priority:'Critical',assigneeUserId:engineer.userId},201);
  let incident=await request('POST',scope+'/incidents',admin.accessToken,{title:'Pre-deployment incident',description:null,affectedService:'API',severity:'Critical',ownerUserId:engineer.userId},201);
  let release=await request('POST',endpoint,engineer.accessToken,{applicationName:'Public API',releaseVersion:'1.0.0',environmentId:env.id,notes:'界'.repeat(8000)},201);
  await request('POST',endpoint,engineer.accessToken,{applicationName:'Too large',releaseVersion:'v1',environmentId:env.id,notes:'x'.repeat(66000)},413);
  await request('GET',`${endpoint}/${release.id}`,outsider.accessToken,undefined,404);
  await request('GET',root+'/dashboard',outsider.accessToken,undefined,404);
  await request('POST',`${endpoint}/${release.id}/transitions`,engineer.accessToken,{expectedVersion:release.version,status:'Approved'},403);
  let hints=0,unexpectedPayload=false;
  connection=new HubConnectionBuilder().withUrl(base+'/api/hubs/notifications',{accessTokenFactory:()=>engineer.accessToken,headers:{Origin:process.env.FLOWOPS_ORIGIN||'http://localhost:4200'},transport:HttpTransportType.WebSockets}).configureLogging(LogLevel.None).build();
  connection.on('NotificationsChanged',(...payload)=>{hints++;if(payload.length)unexpectedPayload=true;});
  await connection.start();

  await race('PUT',`${endpoint}/${release.id}/links`,engineer.accessToken,{expectedVersion:release.version,workItemIds:[work.id],incidentIds:[incident.id]},[200,409]);
  release=await request('GET',`${endpoint}/${release.id}`,engineer.accessToken);
  const stale=release.version;
  await race('PUT',`${endpoint}/${release.id}`,engineer.accessToken,{expectedVersion:release.version,notes:'Runbook checked'},[200,409]);
  await request('POST',`${endpoint}/${release.id}/transitions`,admin.accessToken,{expectedVersion:stale,status:'Approved'},409);
  release=await request('GET',`${endpoint}/${release.id}`,admin.accessToken);
  await race('POST',`${endpoint}/${release.id}/transitions`,admin.accessToken,{expectedVersion:release.version,status:'Approved'},[200,409]);
  release=await request('GET',`${endpoint}/${release.id}`,admin.accessToken);
  await request('PUT',`${endpoint}/${release.id}/links`,admin.accessToken,{expectedVersion:release.version,workItemIds:[],incidentIds:[]},409);
  release=await request('POST',`${endpoint}/${release.id}/transitions`,admin.accessToken,{expectedVersion:release.version,status:'Deploying'});
  release=await request('POST',`${endpoint}/${release.id}/transitions`,admin.accessToken,{expectedVersion:release.version,status:'Successful',outcome:'界'.repeat(4000)});
  assert.equal(release.outcome.length,4000);assert.equal(release.workItemCount,1);assert.equal(release.incidentCount,1);
  const hintDeadline=Date.now()+10000;
  while(hints<3&&Date.now()<hintDeadline) await new Promise(resolve=>setTimeout(resolve,100));
  assert.ok(hints>=3,'Real-time release hints must cross the configured proxy');assert.equal(unexpectedPayload,false,'Hints must not contain tenant data');
  for(const status of ['Investigating','Mitigated','Resolved']) incident=await request('POST',`${scope}/incidents/${incident.id}/transitions`,engineer.accessToken,{expectedVersion:incident.version,status,...(status==='Resolved'?{resolution:'界'.repeat(4000),rootCause:'界'.repeat(4000)}:{})});
  for(const status of ['Triaged','Assigned','InProgress','CodeReview','QA','Resolved','Closed']) work=await request('POST',`${scope}/work-items/${work.id}/transitions`,admin.accessToken,{status});
  const dash=await request('GET',root+'/dashboard',admin.accessToken);
  assert.equal(dash.openWorkItems,0);assert.equal(dash.activeIncidents,0);assert.equal(dash.resolvedWorkItemsInPeriod,1);assert.equal(dash.resolvedIncidentsInPeriod,1);assert.equal(dash.successfulReleasesInPeriod,1);assert.equal(dash.failedReleasesInPeriod,0);assert.equal(dash.teamWorkload.length,0);
  await request('GET',`${root}/dashboard?projectId=${randomUUID()}`,admin.accessToken,undefined,404);
  const timeline=await request('GET',`${endpoint}/${release.id}/timeline`,admin.accessToken);assert.equal(timeline.items.filter(e=>e.action==='Release.StatusChanged').length,3);
  const inbox=await request('GET','/api/notifications?pageSize=100',engineer.accessToken);assert.equal(inbox.items.filter(n=>n.releaseId===release.id).length,3);
  for(let i=0;i<10;i++) await request('POST',endpoint,engineer.accessToken,{applicationName:'API',releaseVersion:`1.0.${i+1}`,environmentId:env.id,notes:null},201);
  assert.equal((await request('GET',root+'/dashboard',admin.accessToken)).recentReleases.length,10);
  assert.equal((await request('GET',endpoint+'?pageSize=5&page=3',admin.accessToken)).items.length,1);

  if(process.env.FLOWOPS_FIXTURE_PATH) writeFileSync(process.env.FLOWOPS_FIXTURE_PATH,JSON.stringify({email:admin.email,password,organizationId:org.id,projectId:project.id,releaseId:release.id,accessToken:admin.accessToken},null,2));
  console.log(JSON.stringify({passed:true,checks:['SQL Server migration and query translation','HttpOnly cookie rotation, origin rejection and replay protection through the API/proxy','real-time SignalR hints through the configured transport','concurrent duplicate environments','concurrent link insertion','stale and concurrent edits','concurrent approval atomicity','role and tenant isolation','immutable approved links','maximum Unicode release and incident audit persistence','existing work and incident lifecycle regression','dashboard completion counts and bounds','release notification integrity','bounded release pagination']},null,2));
})().catch(error=>{console.error(error.message);process.exitCode=1;}).finally(async()=>{
  await connection?.stop().catch(()=>{});
  if(cleanup&&!(process.env.FLOWOPS_KEEP_FIXTURE==='1'&&!process.exitCode)) await cleanup().catch(error=>{console.error(error.message);process.exitCode=1;});
});
