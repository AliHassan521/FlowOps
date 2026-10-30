import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../../core/config/api.config';
import { OrganizationService } from './organization.service';

describe('OrganizationService', () => {
  let service: OrganizationService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(OrganizationService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('requests only the current user organizations from the protected API', () => {
    service.list().subscribe((response) => expect(response.items).toEqual([]));
    const request = http.expectOne(`${API_BASE_URL}/organizations`);
    expect(request.request.method).toBe('GET');
    request.flush({ items: [], page: 1, pageSize: 50, totalCount: 0 });
  });

  it('creates projects within the selected organization route', () => {
    service.createProject('org-id', { name: 'Reliability', key: 'REL', description: null }).subscribe();
    const request = http.expectOne(`${API_BASE_URL}/organizations/org-id/projects`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ name: 'Reliability', key: 'REL', description: null });
    request.flush({});
  });

  it('updates an organization through its organization-scoped route', () => {
    service.update('org-id', { name: 'Updated name', description: null }).subscribe();
    const request = http.expectOne(`${API_BASE_URL}/organizations/org-id`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ name: 'Updated name', description: null });
    request.flush({});
  });

  it('deactivates an organization through its scoped resource route', () => {
    service.deactivate('org-id').subscribe();
    const request = http.expectOne(`${API_BASE_URL}/organizations/org-id`);
    expect(request.request.method).toBe('DELETE');
    request.flush(null);
  });

  it('updates a project only within its organization route', () => {
    service.updateProject('org-id', {
      id: 'project-id', organizationId: 'org-id', name: 'Old name', key: 'REL', description: null,
      isActive: true, createdAtUtc: '2026-10-30T00:00:00Z', updatedAtUtc: null,
    }, { name: 'New name', description: 'New details' }).subscribe();
    const request = http.expectOne(`${API_BASE_URL}/organizations/org-id/projects/project-id`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ name: 'New name', description: 'New details' });
    request.flush({});
  });

  it('loads project details using both tenant and project identifiers', () => {
    service.project('org-id', 'project-id').subscribe();
    const request = http.expectOne(`${API_BASE_URL}/organizations/org-id/projects/project-id`);
    expect(request.request.method).toBe('GET');
    request.flush({});
  });
});
