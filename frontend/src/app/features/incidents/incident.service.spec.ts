import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { API_BASE_URL } from '../../core/config/api.config';
import { IncidentService } from './incident.service';
import { Incident } from './incident.models';
describe('IncidentService', () => {
  let api: IncidentService;
  let http: HttpTestingController;
  const base = `${API_BASE_URL}/organizations/org/projects/project/incidents`;
  const item = { id: 'incident', version: 'version-1' } as Incident;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(IncidentService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('scopes and bounds list filters without interpolating search into a URL', () => {
    api
      .list('org', 'project', {
        page: 2,
        severity: 'Critical',
        status: 'Open',
        search: '?owner=attacker&',
      })
      .subscribe();
    const request = http.expectOne((r) => r.url === base);
    expect(request.request.params.get('pageSize')).toBe('20');
    expect(request.request.params.get('search')).toBe('?owner=attacker&');
    request.flush({ items: [] });
  });
  it('includes the expected incident version for mutation and SLA requests', () => {
    api.transition('org', 'project', item, 'Resolved', 'Rolled back', null).subscribe();
    const transition = http.expectOne(`${base}/incident/transitions`);
    expect(transition.request.body).toEqual({
      expectedVersion: 'version-1',
      status: 'Resolved',
      resolution: 'Rolled back',
      rootCause: null,
    });
    transition.flush({});
    api.attach('org', 'project', item, 'policy').subscribe();
    const attach = http.expectOne(`${base}/incident/sla`);
    expect(attach.request.body).toEqual({ expectedVersion: 'version-1', policyId: 'policy' });
    attach.flush({});
  });
  it('requests project and organization metrics from their own scoped endpoints', () => {
    api.summary('org', 'project').subscribe();
    http.expectOne(`${base}/summary`).flush({});
    api.summary('org').subscribe();
    http.expectOne(`${API_BASE_URL}/organizations/org/incident-summary`).flush({});
  });
});
