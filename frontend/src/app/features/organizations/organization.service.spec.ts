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

  it('loads project details using both tenant and project identifiers', () => {
    service.project('org-id', 'project-id').subscribe();
    const request = http.expectOne(`${API_BASE_URL}/organizations/org-id/projects/project-id`);
    expect(request.request.method).toBe('GET');
    request.flush({});
  });
});
