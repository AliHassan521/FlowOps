import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, of, Subject, throwError } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { OrganizationService } from '../organizations/organization.service';
import { NotificationService } from '../notifications/notification.service';
import { WorkItemService } from '../work-items/work-item.service';
import { IncidentService } from '../incidents/incident.service';
import { ReleaseService } from './release.service';
import { Release } from './release.models';
import { ReleasesComponent } from './releases.component';

describe('ReleasesComponent', () => {
  const release = {
    id: 'release',
    version: 'version',
    projectId: 'project',
    applicationName: '<img src=x onerror=alert(1)>',
    releaseVersion: 'v1',
    environmentId: 'env',
    environmentName: 'Production',
    environmentActive: true,
    status: 'Draft',
    createdByUserId: 'engineer',
    creatorName: 'Engineer',
    notes: 'Original notes',
    createdAtUtc: '2026-11-03T00:00:00Z',
    updatedAtUtc: '2026-11-03T00:00:00Z',
    workItemCount: 0,
    incidentCount: 0,
  } as Release;
  const env = {
    id: 'env',
    name: 'Production',
    isActive: true,
    createdAtUtc: '2026-11-03T00:00:00Z',
  };
  const page = { items: [], totalCount: 0 };
  let api: Record<string, ReturnType<typeof vi.fn>>;
  let org: Record<string, ReturnType<typeof vi.fn>>;
  let work: Record<string, ReturnType<typeof vi.fn>>;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  beforeEach(() => {
    params = new BehaviorSubject(
      convertToParamMap({ organizationId: 'org', projectId: 'project' }),
    );
    api = Object.fromEntries(
      [
        'list',
        'get',
        'create',
        'update',
        'setLinks',
        'transition',
        'links',
        'timeline',
        'environments',
        'createEnvironment',
        'archiveEnvironment',
      ].map((key) => [key, vi.fn()]),
    );
    api['list'].mockReturnValue(of({ items: [release], totalCount: 1 }));
    for (const name of ['get', 'create', 'update', 'setLinks', 'transition'])
      api[name].mockReturnValue(of(release));
    api['links'].mockReturnValue(of({ workItems: [], incidents: [] }));
    api['timeline'].mockReturnValue(of(page));
    api['environments'].mockReturnValue(of({ items: [env], totalCount: 1 }));
    api['archiveEnvironment'].mockReturnValue(of(null));
    org = {
      get: vi
        .fn()
        .mockReturnValue(of({ id: 'org', name: 'Operations', currentUserRole: 'Engineer' })),
      project: vi.fn().mockReturnValue(of({ id: 'project', name: 'API' })),
    };
    work = { list: vi.fn().mockReturnValue(of(page)) };
    TestBed.configureTestingModule({
      imports: [ReleasesComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { paramMap: params, queryParamMap: of(convertToParamMap({})) },
        },
        { provide: ReleaseService, useValue: api },
        { provide: OrganizationService, useValue: org },
        { provide: WorkItemService, useValue: work },
        { provide: IncidentService, useValue: { list: () => of(page) } },
        {
          provide: AuthService,
          useValue: { currentUser: () => ({ userId: 'engineer', fullName: 'Engineer' }) },
        },
        { provide: NotificationService, useValue: { unreadCount: () => 0, reload: vi.fn() } },
      ],
    });
  });
  function setup() {
    const fixture = TestBed.createComponent(ReleasesComponent);
    fixture.detectChanges();
    return fixture;
  }
  it('validates draft fields and sends trimmed input', () => {
    const c = setup().componentInstance;
    c.create();
    expect(api['create']).not.toHaveBeenCalled();
    c.createForm.setValue({
      applicationName: ' API ',
      releaseVersion: 'v1.2',
      environmentId: 'env',
      notes: '  runbook  ',
    });
    c.create();
    expect(api['create']).toHaveBeenCalledWith('org', 'project', {
      applicationName: 'API',
      releaseVersion: 'v1.2',
      environmentId: 'env',
      notes: 'runbook',
    });
    expect(c.selected()?.id).toBe('release');
  });
  it('hides mutations for viewers and escapes stored user text', () => {
    org['get'].mockReturnValue(of({ id: 'org', currentUserRole: 'Viewer' }));
    const fixture = setup();
    const c = fixture.componentInstance;
    c.open('release');
    c.createForm.setValue({
      applicationName: 'API',
      releaseVersion: 'v1',
      environmentId: 'env',
      notes: '',
    });
    c.create();
    c.transition('Approved');
    c.saveLinks();
    fixture.detectChanges();
    expect(c.canEdit).toBe(false);
    expect(api['create']).not.toHaveBeenCalled();
    expect(api['transition']).not.toHaveBeenCalled();
    expect(api['setLinks']).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain(release.applicationName);
    expect(fixture.nativeElement.querySelector('img')).toBeNull();
  });
  it('preserves notes and expected version when an update conflicts', () => {
    const c = setup().componentInstance;
    c.open('release');
    c.notesForm.setValue({ notes: 'Unsaved runbook' });
    api['update'].mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
    c.updateNotes();
    expect(api['update']).toHaveBeenCalledWith('org', 'project', release, 'Unsaved runbook');
    expect(c.notesForm.controls.notes.value).toBe('Unsaved runbook');
    expect(c.detailError()).toContain('changed');
    expect(c.saving()).toBe(false);
  });
  it('requires confirmation before environment archiving', () => {
    org['get'].mockReturnValue(of({ id: 'org', currentUserRole: 'Admin' }));
    const c = setup().componentInstance;
    c.archiveEnvironment(env);
    expect(api['archiveEnvironment']).not.toHaveBeenCalled();
    c.archiveEnvironment(env);
    expect(api['archiveEnvironment']).toHaveBeenCalledWith('org', 'project', 'env');
    expect(c.pendingArchive()).toBeNull();
  });
  it('requires a deployment outcome before terminal transitions', () => {
    org['get'].mockReturnValue(of({ id: 'org', currentUserRole: 'TeamLead' }));
    api['get'].mockReturnValue(of({ ...release, status: 'Deploying' }));
    const c = setup().componentInstance;
    c.open('release');
    c.transition('Failed');
    expect(api['transition']).not.toHaveBeenCalled();
    c.outcomeForm.setValue({ outcome: '  Rolled back  ' });
    c.transition('Failed');
    expect(api['transition']).toHaveBeenCalledWith(
      'org',
      'project',
      expect.objectContaining({ version: 'version' }),
      'Failed',
      'Rolled back',
    );
  });
  it('cancels a stale detail response when changing organizations', () => {
    const pending = new Subject<Release>();
    api['get'].mockReturnValue(pending);
    const c = setup().componentInstance;
    c.open('release');
    params.next(convertToParamMap({ organizationId: 'other', projectId: 'other-project' }));
    pending.next(release);
    pending.complete();
    expect(c.selected()).toBeNull();
    expect(c.createForm.controls.notes.value).toBe('');
    expect(api['list']).toHaveBeenLastCalledWith('other', 'other-project', expect.anything());
  });
  it('cancels earlier candidate paging responses within the same release', () => {
    const first = new Subject<typeof page>();
    work['list']
      .mockReturnValueOnce(first)
      .mockReturnValueOnce(
        of({ items: [{ id: 'new', title: 'Latest', status: 'New' }], totalCount: 100 }),
      );
    const c = setup().componentInstance;
    c.open('release');
    c.loadOptions(1);
    first.next(page);
    first.complete();
    expect(c.workPage()).toBe(2);
    expect(c.workOptions()[0].id).toBe('new');
  });
  it('blocks a 101st link without losing the existing selection', () => {
    const c = setup().componentInstance;
    c.open('release');
    c.linkedWork.set(
      Array.from({ length: 100 }, (_, i) => ({ id: String(i), title: 'Task', status: 'New' })),
    );
    const input = document.createElement('input');
    input.type = 'checkbox';
    input.checked = true;
    c.toggleLink('work', { id: 'overflow', title: 'Task', status: 'New' }, {
      target: input,
    } as unknown as Event);
    expect(c.linkedWork()).toHaveLength(100);
    expect(input.checked).toBe(false);
    expect(c.detailError()).toContain('100');
  });
});
