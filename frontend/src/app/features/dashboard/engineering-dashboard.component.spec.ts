import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, of } from 'rxjs';
import { EngineeringDashboardComponent } from './engineering-dashboard.component';
import { OrganizationService } from '../organizations/organization.service';
import { NotificationService } from '../notifications/notification.service';
import { AuthService } from '../../core/auth/auth.service';

describe('EngineeringDashboardComponent', () => {
  let http: HttpTestingController;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  const data = {
    serverTimeUtc: '2026-11-03T00:00:00Z',
    periodDays: 30,
    openWorkItems: 7,
    criticalWorkItems: 2,
    activeIncidents: 1,
    criticalIncidents: 1,
    unassignedWorkItems: 3,
    slaAtRisk: 2,
    slaBreached: 1,
    slaMet: 4,
    resolvedWorkItemsInPeriod: 5,
    resolvedIncidentsInPeriod: 2,
    successfulReleasesInPeriod: 3,
    failedReleasesInPeriod: 1,
    recentReleases: [],
    teamWorkload: [],
    workloadPage: 1,
    workloadTotalCount: 0,
  };
  beforeEach(() => {
    params = new BehaviorSubject(convertToParamMap({ organizationId: 'org' }));
    TestBed.configureTestingModule({
      imports: [EngineeringDashboardComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { paramMap: params } },
        {
          provide: OrganizationService,
          useValue: {
            get: () => of({ id: 'org', name: 'Operations' }),
            projects: () => of({ items: [], totalCount: 0 }),
          },
        },
        { provide: NotificationService, useValue: { unreadCount: () => 0, reload: vi.fn() } },
        { provide: AuthService, useValue: { currentUser: () => ({ fullName: 'Engineer' }) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('renders server measurements with their scope and time window', () => {
    const fixture = TestBed.createComponent(EngineeringDashboardComponent);
    fixture.detectChanges();
    const request = http.expectOne((r) => r.url.endsWith('/organizations/org/dashboard'));
    expect(request.request.params.get('periodDays')).toBe('30');
    expect(request.request.params.has('projectId')).toBe(false);
    request.flush(data);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('7');
    expect(fixture.nativeElement.textContent).toContain('2 critical');
    expect(fixture.nativeElement.textContent).toContain('No assigned open work');
  });
  it('cancels previous requests and clears old counts when scope changes', () => {
    const fixture = TestBed.createComponent(EngineeringDashboardComponent);
    fixture.detectChanges();
    const first = http.expectOne((r) => r.url.endsWith('/org/dashboard'));
    const c = fixture.componentInstance;
    c.selectProject({ target: { value: 'project' } } as unknown as Event);
    expect(first.cancelled).toBe(true);
    expect(c.metrics()).toBeNull();
    const second = http.expectOne((r) => r.params.get('projectId') === 'project');
    second.flush(data);
    expect(c.metrics()?.openWorkItems).toBe(7);
    params.next(convertToParamMap({ organizationId: 'other' }));
    expect(c.metrics()).toBeNull();
    const third = http.expectOne((r) => r.url.endsWith('/other/dashboard'));
    expect(third.request.params.has('projectId')).toBe(false);
    third.flush(data);
  });
  it('clears previous metrics on access failures and allows retry', () => {
    const fixture = TestBed.createComponent(EngineeringDashboardComponent);
    fixture.detectChanges();
    http.expectOne((r) => r.url.endsWith('/dashboard')).flush(data);
    const c = fixture.componentInstance;
    c.refresh();
    http
      .expectOne((r) => r.url.endsWith('/dashboard'))
      .flush({}, { status: 404, statusText: 'Not found' });
    expect(c.metrics()).toBeNull();
    expect(c.error()).toContain('current access');
    expect(c.loading()).toBe(false);
    c.refresh();
    http.expectOne((r) => r.url.endsWith('/dashboard')).flush(data);
    expect(c.error()).toBe('');
  });
});
