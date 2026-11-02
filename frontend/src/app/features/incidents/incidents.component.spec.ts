import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, of, Subject, throwError } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { NotificationService } from '../notifications/notification.service';
import { OrganizationService } from '../organizations/organization.service';
import { SlaService } from '../sla/sla.service';
import { IncidentService } from './incident.service';
import { IncidentsComponent } from './incidents.component';
import { Incident } from './incident.models';
describe('IncidentsComponent', () => {
  const item: Incident = {
    id: 'i1',
    number: 'INC-1',
    version: 'v1',
    projectId: 'project',
    title: 'API outage',
    description: null,
    affectedService: 'API',
    severity: 'Critical',
    status: 'Open',
    reporterUserId: 'admin',
    reporterName: 'Admin',
    ownerUserId: 'engineer',
    ownerName: 'Engineer',
    startedAtUtc: '2026-11-02T00:00:00Z',
    updatedAtUtc: '2026-11-02T00:00:00Z',
    endedAtUtc: null,
    resolvedByUserId: null,
    resolution: null,
    rootCause: null,
  };
  let api: Record<string, ReturnType<typeof vi.fn>>;
  let org: Record<string, ReturnType<typeof vi.fn>>;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  beforeEach(() => {
    params = new BehaviorSubject(
      convertToParamMap({ organizationId: 'org', projectId: 'project' }),
    );
    const page = { items: [], page: 1, pageSize: 50, totalCount: 0 };
    api = {
      list: vi.fn().mockReturnValue(of({ items: [item], totalCount: 1 })),
      get: vi.fn().mockReturnValue(of(item)),
      create: vi.fn().mockReturnValue(of(item)),
      update: vi.fn().mockReturnValue(of(item)),
      assign: vi.fn().mockReturnValue(of(item)),
      transition: vi.fn().mockReturnValue(of(item)),
      comment: vi.fn().mockReturnValue(of({})),
      comments: vi.fn().mockReturnValue(of(page)),
      timeline: vi.fn().mockReturnValue(of(page)),
      sla: vi.fn().mockReturnValue(of({ sla: null, serverTimeUtc: '2026-11-02T00:00:00Z' })),
      attach: vi.fn().mockReturnValue(of({})),
      summary: vi
        .fn()
        .mockReturnValue(
          of({
            activeCount: 1,
            criticalCount: 1,
            resolvedCount: 0,
            atRiskCount: 0,
            breachedCount: 0,
          }),
        ),
    };
    org = {
      get: vi
        .fn()
        .mockReturnValue(of({ id: 'org', name: 'Operations', currentUserRole: 'Engineer' })),
      project: vi
        .fn()
        .mockReturnValue(of({ id: 'project', organizationId: 'org', key: 'API', name: 'API' })),
      members: vi.fn().mockReturnValue(of(page)),
    };
    TestBed.configureTestingModule({
      imports: [IncidentsComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { paramMap: params, queryParamMap: of(convertToParamMap({})) },
        },
        { provide: IncidentService, useValue: api },
        { provide: OrganizationService, useValue: org },
        {
          provide: AuthService,
          useValue: { currentUser: () => ({ userId: 'engineer', fullName: 'Engineer' }) },
        },
        { provide: NotificationService, useValue: { unreadCount: () => 0, reload: vi.fn() } },
        { provide: SlaService, useValue: { policies: () => of(page) } },
      ],
    });
  });
  it('validates incident creation and strips an injected owner for non-leads', () => {
    const fixture = TestBed.createComponent(IncidentsComponent);
    fixture.detectChanges();
    const c = fixture.componentInstance;
    c.create();
    expect(api['create']).not.toHaveBeenCalled();
    c.createForm.setValue({
      title: '  Service outage  ',
      description: ' context ',
      affectedService: ' API ',
      severity: 'High',
      ownerUserId: 'admin',
    });
    c.create();
    expect(api['create']).toHaveBeenCalledWith('org', 'project', {
      title: 'Service outage',
      description: 'context',
      affectedService: 'API',
      severity: 'High',
      ownerUserId: null,
    });
    expect(c.selected()?.id).toBe('i1');
  });
  it('hides write actions for viewers while leaving the register and timeline readable', () => {
    org['get'].mockReturnValue(of({ id: 'org', currentUserRole: 'Viewer' }));
    const fixture = TestBed.createComponent(IncidentsComponent);
    fixture.detectChanges();
    const c = fixture.componentInstance;
    c.open('i1');
    fixture.detectChanges();
    expect(c.canWrite).toBe(false);
    expect(c.nextStatus).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Report incident');
    c.commentForm.setValue({ body: 'Attempt write' });
    c.addComment();
    expect(api['comment']).not.toHaveBeenCalled();
  });
  it('requires a resolution before the owner can resolve and does not expose closure to an engineer', () => {
    api['get'].mockReturnValue(of({ ...item, status: 'Mitigated' }));
    const fixture = TestBed.createComponent(IncidentsComponent);
    fixture.detectChanges();
    const c = fixture.componentInstance;
    c.open('i1');
    c.transition();
    expect(api['transition']).not.toHaveBeenCalled();
    c.resolutionForm.setValue({ resolution: 'Recovered API', rootCause: 'Bad config' });
    c.transition();
    expect(api['transition']).toHaveBeenCalledWith(
      'org',
      'project',
      expect.objectContaining({ version: 'v1' }),
      'Resolved',
      'Recovered API',
      'Bad config',
    );
    c.selected.set({ ...item, status: 'Resolved' });
    expect(c.nextStatus).toBeNull();
  });
  it('ignores responses from a previously selected incident', () => {
    const pending = new Subject<Incident>();
    api['get'].mockImplementation((_org: string, _project: string, id: string) =>
      id === 'i1' ? pending : of({ ...item, id }),
    );
    const fixture = TestBed.createComponent(IncidentsComponent);
    fixture.detectChanges();
    const c = fixture.componentInstance;
    c.open('i1');
    c.open('i2');
    pending.next(item);
    pending.complete();
    expect(c.selected()?.id).toBe('i2');
  });
  it('cancels an old tenant mutation and clears its draft on a route change', () => {
    const pending = new Subject<Incident>();
    api['create'].mockReturnValue(pending);
    const fixture = TestBed.createComponent(IncidentsComponent);
    fixture.detectChanges();
    const c = fixture.componentInstance;
    c.createForm.setValue({
      title: 'Old tenant draft',
      description: '',
      affectedService: 'API',
      severity: 'High',
      ownerUserId: '',
    });
    c.create();
    params.next(convertToParamMap({ organizationId: 'other-org', projectId: 'other-project' }));
    pending.next(item);
    pending.complete();
    expect(c.selected()).toBeNull();
    expect(c.createForm.controls.title.value).toBe('');
    expect(api['list']).toHaveBeenLastCalledWith('other-org', 'other-project', expect.anything());
  });
  it('renders hostile incident text as text and reports version conflicts without losing the draft', () => {
    const payload = '<img src=x onerror=alert(1)>';
    api['get'].mockReturnValue(of({ ...item, title: payload }));
    const fixture = TestBed.createComponent(IncidentsComponent);
    fixture.detectChanges();
    const c = fixture.componentInstance;
    c.open('i1');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(payload);
    expect(fixture.nativeElement.querySelector('img[onerror]')).toBeNull();
    api['update'].mockReturnValue(throwError(() => ({ status: 409 })));
    c.editForm.controls.title.setValue('Unsaved title');
    c.update();
    expect(c.detailError()).toContain('Refresh');
    expect(c.editForm.controls.title.value).toBe('Unsaved title');
  });
  it('requests further comment and timeline pages', () => {
    const fixture = TestBed.createComponent(IncidentsComponent);
    fixture.detectChanges();
    const c = fixture.componentInstance;
    c.open('i1');
    c.commentTotal.set(70);
    c.timelineTotal.set(80);
    c.moveComments(1);
    c.moveTimeline(1);
    expect(api['comments']).toHaveBeenLastCalledWith('org', 'project', 'i1', 2);
    expect(api['timeline']).toHaveBeenLastCalledWith('org', 'project', 'i1', 2);
  });
  it('shows a live update hint without overwriting an in-progress draft', () => {
    const changes = new Subject<void>();
    TestBed.overrideProvider(NotificationService, { useValue: { unreadCount: () => 0, reload: vi.fn(), changes$: changes } });
    const fixture = TestBed.createComponent(IncidentsComponent); fixture.detectChanges();
    const c = fixture.componentInstance; c.open('i1'); c.editForm.controls.title.setValue('Draft response');
    changes.next(); fixture.detectChanges();
    expect(c.updatesAvailable()).toBe(true); expect(c.editForm.controls.title.value).toBe('Draft response');
    c.refresh(); expect(c.updatesAvailable()).toBe(false); expect(c.editForm.controls.title.value).toBe(item.title);
  });

  it('shows the persisted owner when dynamically rendering assignment options', () => {
    org['get'].mockReturnValue(of({id:'org',currentUserRole:'Admin'}));
    org['members'].mockReturnValue(of({items:[{userId:'admin',fullName:'Admin',role:'Admin',isActive:true},{userId:'engineer',fullName:'Engineer',role:'Engineer',isActive:true}],totalCount:2}));
    const fixture=TestBed.createComponent(IncidentsComponent);fixture.detectChanges();fixture.componentInstance.open('i1');fixture.detectChanges();
    expect((fixture.nativeElement.querySelector('select[aria-label="Incident owner"]') as HTMLSelectElement).value).toBe('engineer');
  });

});
