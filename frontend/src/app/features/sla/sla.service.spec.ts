import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../../core/config/api.config';
import { SlaService } from './sla.service';

describe('SlaService', () => {
  let service: SlaService; let http: HttpTestingController;
  beforeEach(() => { TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }); service = TestBed.inject(SlaService); http = TestBed.inject(HttpTestingController); });
  afterEach(() => http.verify());
  it('keeps policy pagination and tenant identity in the request', () => {
    service.policies('org-1', 2, false).subscribe();
    const request = http.expectOne(request => request.url === `${API_BASE_URL}/organizations/org-1/sla-policies`);
    expect(request.request.params.get('page')).toBe('2'); expect(request.request.params.get('activeOnly')).toBe('false');
    request.flush({ items: [], totalCount: 0, page: 2, pageSize: 50 });
  });
  it('attaches a policy using both project and work-item scope', () => {
    service.attach('org-1', 'project-1', 'item-1', 'policy-1').subscribe();
    const request = http.expectOne(`${API_BASE_URL}/organizations/org-1/projects/project-1/work-items/item-1/sla`);
    expect(request.request.method).toBe('POST'); expect(request.request.body).toEqual({ policyId: 'policy-1' });
    request.flush({ sla: null, serverTimeUtc: '2026-11-02T10:00:00Z' });
  });
});
