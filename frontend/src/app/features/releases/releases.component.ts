import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { combineLatest, finalize, forkJoin, Observable, Subscription } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { WorkspaceHeaderComponent } from '../../shared/workspace-header.component';
import { OrganizationService } from '../organizations/organization.service';
import { Organization, Project } from '../organizations/organization.models';
import { NotificationService } from '../notifications/notification.service';
import { WorkItemService } from '../work-items/work-item.service';
import { IncidentService } from '../incidents/incident.service';
import {
  Release,
  ReleaseEnvironment,
  ReleaseEvent,
  ReleaseResource,
  RELEASE_STATUSES,
} from './release.models';
import { ReleaseService } from './release.service';
@Component({
  selector: 'app-releases',
  standalone: true,
  imports: [DatePipe, ReactiveFormsModule, RouterLink, WorkspaceHeaderComponent],
  templateUrl: './releases.component.html',
  styleUrl: './releases.component.css',
})
export class ReleasesComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly destroy = inject(DestroyRef);
  private readonly forms = inject(FormBuilder);
  private readonly api = inject(ReleaseService);
  private readonly orgApi = inject(OrganizationService);
  private readonly workApi = inject(WorkItemService);
  private readonly incidentApi = inject(IncidentService);
  private readonly inbox = inject(NotificationService);
  readonly auth = inject(AuthService);
  readonly organization = signal<Organization | null>(null);
  readonly project = signal<Project | null>(null);
  readonly releases = signal<Release[]>([]);
  readonly selected = signal<Release | null>(null);
  readonly environments = signal<ReleaseEnvironment[]>([]);
  readonly environmentTotal = signal(0);
  readonly pendingArchive = signal<string | null>(null);
  readonly linkedWork = signal<ReleaseResource[]>([]);
  readonly linkedIncidents = signal<ReleaseResource[]>([]);
  readonly workOptions = signal<ReleaseResource[]>([]);
  readonly incidentOptions = signal<ReleaseResource[]>([]);
  readonly workPage = signal(1);
  readonly workTotal = signal(0);
  readonly incidentPage = signal(1);
  readonly incidentTotal = signal(0);
  readonly timeline = signal<ReleaseEvent[]>([]);
  readonly timelinePage = signal(1);
  readonly timelineTotal = signal(0);
  readonly loading = signal(false);
  readonly loadingDetail = signal(false);
  readonly saving = signal(false);
  readonly loadingEnvironments = signal(false);
  readonly page = signal(1);
  readonly total = signal(0);
  readonly error = signal('');
  readonly detailError = signal('');
  readonly updatesAvailable = signal(false);
  readonly statuses = RELEASE_STATUSES;
  readonly filters = this.forms.nonNullable.group({
    search: [''],
    status: [''],
    environmentId: [''],
  });
  readonly createForm = this.forms.nonNullable.group({
    applicationName: [
      '',
      [Validators.required, Validators.minLength(2), Validators.maxLength(120)],
    ],
    releaseVersion: [
      '',
      [Validators.required, Validators.pattern(/^[A-Za-z0-9][A-Za-z0-9._+-]{0,79}$/)],
    ],
    environmentId: ['', Validators.required],
    notes: ['', Validators.maxLength(8000)],
  });
  readonly environmentForm = this.forms.nonNullable.group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(80)]],
  });
  readonly notesForm = this.forms.nonNullable.group({ notes: ['', Validators.maxLength(8000)] });
  readonly outcomeForm = this.forms.nonNullable.group({
    outcome: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(4000)]],
  });
  private optionsRequest?: Subscription;
  private timelineRequest?: Subscription;
  private scope = new Subscription();
  private detail = new Subscription();
  private listing?: Subscription;
  private mutation?: Subscription;
  private environmentPage = 1;
  private org = '';
  private projectId = '';
  get role() {
    return this.organization()?.currentUserRole ?? '';
  }
  get canWrite() {
    return !!this.role && this.role !== 'Viewer';
  }
  get canManage() {
    return ['Admin', 'TeamLead'].includes(this.role);
  }
  get canEdit() {
    const r = this.selected();
    return (
      this.canWrite &&
      r?.status === 'Draft' &&
      (this.canManage || r.createdByUserId === this.auth.currentUser()?.userId)
    );
  }
  get activeEnvironments() {
    return this.environments().filter((e) => e.isActive);
  }
  get pages() {
    return Math.max(1, Math.ceil(this.total() / 20));
  }
  ngOnInit() {
    this.destroy.onDestroy(() => {
      this.scope.unsubscribe();
      this.detail.unsubscribe();
      this.listing?.unsubscribe();
      this.mutation?.unsubscribe();
    });
    this.inbox.changes$
      ?.pipe(takeUntilDestroyed(this.destroy))
      .subscribe(() => this.updatesAvailable.set(true));
    combineLatest([this.route.paramMap, this.route.queryParamMap])
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe(([params, query]) => {
        this.scope.unsubscribe();
        this.scope = new Subscription();
        this.close();
        this.listing?.unsubscribe();
        this.organization.set(null);
        this.project.set(null);
        this.environments.set([]);
        this.releases.set([]);
        this.environmentTotal.set(0);
        this.environmentPage = 1;
        this.pendingArchive.set(null);
        this.updatesAvailable.set(false);
        this.error.set('');
        this.page.set(1);
        this.total.set(0);
        this.createForm.reset({
          applicationName: '',
          releaseVersion: '',
          environmentId: '',
          notes: '',
        });
        this.environmentForm.reset({ name: '' });
        this.filters.reset({ search: '', status: '', environmentId: '' });
        this.org = params.get('organizationId') ?? '';
        this.projectId = params.get('projectId') ?? '';
        if (!this.org || !this.projectId) {
          this.error.set('This project link is incomplete.');
          return;
        }
        this.scope.add(
          forkJoin({
            organization: this.orgApi.get(this.org),
            project: this.orgApi.project(this.org, this.projectId),
          }).subscribe({
            next: (r) => {
              this.organization.set(r.organization);
              this.project.set(r.project);
              this.loadEnvironments();
              this.load();
              const id = query.get('releaseId');
              if (id) this.open(id);
            },
            error: (e) => this.error.set(this.describe(e)),
          }),
        );
      });
  }
  load() {
    this.listing?.unsubscribe();
    this.loading.set(true);
    this.error.set('');
    this.listing = this.api
      .list(this.org, this.projectId, {
        ...this.filters.getRawValue(),
        search: this.filters.controls.search.value.trim(),
        page: this.page(),
      })
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (r) => {
          this.releases.set(r.items);
          this.total.set(r.totalCount);
        },
        error: (e) => {
          this.releases.set([]);
          this.total.set(0);
          this.error.set(this.describe(e));
        },
      });
  }
  refresh() {
    this.updatesAvailable.set(false);
    this.load();
    this.loadEnvironments();
    const id = this.selected()?.id;
    if (id) this.open(id);
  }
  applyFilters() {
    this.page.set(1);
    this.load();
  }
  movePage(delta: number) {
    this.page.set(Math.min(this.pages, Math.max(1, this.page() + delta)));
    this.load();
  }
  create() {
    this.createForm.markAllAsTouched();
    if (!this.canWrite || this.createForm.invalid || this.saving()) return;
    const v = this.createForm.getRawValue();
    this.run(
      this.api.create(this.org, this.projectId, {
        applicationName: v.applicationName.trim(),
        releaseVersion: v.releaseVersion.trim(),
        environmentId: v.environmentId,
        notes: v.notes.trim() || null,
      }),
      (r) => {
        this.createForm.reset({
          applicationName: '',
          releaseVersion: '',
          environmentId: '',
          notes: '',
        });
        this.open(r.id);
        this.load();
      },
      false,
    );
  }
  createEnvironment() {
    this.environmentForm.markAllAsTouched();
    if (!this.canManage || this.environmentForm.invalid || this.saving()) return;
    this.run(
      this.api.createEnvironment(
        this.org,
        this.projectId,
        this.environmentForm.controls.name.value.trim(),
      ),
      () => {
        this.environmentForm.reset({ name: '' });
        this.loadEnvironments();
      },
      false,
    );
  }
  archiveEnvironment(env: ReleaseEnvironment) {
    if (!this.canManage || this.saving()) return;
    if (this.pendingArchive() !== env.id) {
      this.pendingArchive.set(env.id);
      return;
    }
    this.run(
      this.api.archiveEnvironment(this.org, this.projectId, env.id),
      () => {
        this.pendingArchive.set(null);
        this.loadEnvironments();
        const id = this.selected()?.id;
        if (id) this.open(id);
      },
      false,
    );
  }
  loadEnvironments(more = false) {
    if (this.loadingEnvironments()) return;
    this.loadingEnvironments.set(true);
    const page = more ? this.environmentPage + 1 : 1;
    this.scope.add(
      this.api
        .environments(this.org, this.projectId, page)
        .pipe(finalize(() => this.loadingEnvironments.set(false)))
        .subscribe({
          next: (r) => {
            this.environmentPage = page;
            this.environments.set(
              more
                ? [...new Map([...this.environments(), ...r.items].map((e) => [e.id, e])).values()]
                : r.items,
            );
            this.environmentTotal.set(r.totalCount);
          },
          error: (e) => this.error.set(this.describe(e)),
        }),
    );
  }
  open(id: string) {
    if (this.saving()) return;
    this.detail.unsubscribe();
    this.detail = new Subscription();
    this.selected.set(null);
    this.timeline.set([]);
    this.linkedWork.set([]);
    this.linkedIncidents.set([]);
    this.workOptions.set([]);
    this.incidentOptions.set([]);
    this.workPage.set(1);
    this.incidentPage.set(1);
    this.timelinePage.set(1);
    this.workTotal.set(0);
    this.incidentTotal.set(0);
    this.timelineTotal.set(0);
    this.loadingDetail.set(true);
    this.detailError.set('');
    this.outcomeForm.reset({ outcome: '' });
    this.detail.add(
      forkJoin({
        release: this.api.get(this.org, this.projectId, id),
        links: this.api.links(this.org, this.projectId, id),
        timeline: this.api.timeline(this.org, this.projectId, id),
      })
        .pipe(finalize(() => this.loadingDetail.set(false)))
        .subscribe({
          next: (r) => {
            this.selected.set(r.release);
            this.notesForm.reset({ notes: r.release.notes ?? '' });
            this.linkedWork.set(r.links.workItems);
            this.linkedIncidents.set(r.links.incidents);
            this.timeline.set(r.timeline.items);
            this.timelineTotal.set(r.timeline.totalCount);
            if (this.canEdit) this.loadOptions();
          },
          error: (e) => this.detailError.set(this.describe(e)),
        }),
    );
  }
  close() {
    this.mutation?.unsubscribe();
    this.saving.set(false);
    this.detail.unsubscribe();
    this.detail = new Subscription();
    this.selected.set(null);
    this.loadingDetail.set(false);
    this.detailError.set('');
  }
  updateNotes() {
    const r = this.selected();
    if (!r || !this.canEdit || this.notesForm.invalid || this.saving()) return;
    this.run(
      this.api.update(
        this.org,
        this.projectId,
        r,
        this.notesForm.controls.notes.value.trim() || null,
      ),
      (value) => this.changed(value),
    );
  }
  transition(status: string) {
    const r = this.selected();
    if (!r || !this.canManage || this.saving()) return;
    const terminal = ['Successful', 'Failed'].includes(status);
    if (terminal) {
      this.outcomeForm.markAllAsTouched();
      if (this.outcomeForm.invalid) return;
    }
    this.run(
      this.api.transition(
        this.org,
        this.projectId,
        r,
        status,
        terminal ? this.outcomeForm.controls.outcome.value.trim() : null,
      ),
      (value) => this.changed(value),
    );
  }
  toggleLink(kind: 'work' | 'incident', resource: ReleaseResource, event: Event) {
    if (!this.canEdit || this.saving()) return;
    const target = kind === 'work' ? this.linkedWork : this.linkedIncidents;
    const checked = (event.target as HTMLInputElement).checked;
    if (checked && !target().some((r) => r.id === resource.id)) {
      if (target().length >= 100) {
        this.detailError.set('Select at most 100 resources of each type.');
        (event.target as HTMLInputElement).checked = false;
        return;
      }
      target.update((items) => [...items, resource]);
    }
    if (!checked) target.update((items) => items.filter((r) => r.id !== resource.id));
  }
  linked(kind: 'work' | 'incident', id: string) {
    return (kind === 'work' ? this.linkedWork() : this.linkedIncidents()).some((r) => r.id === id);
  }
  saveLinks() {
    const r = this.selected();
    if (!r || !this.canEdit || this.saving()) return;
    this.run(
      this.api.setLinks(
        this.org,
        this.projectId,
        r,
        this.linkedWork().map((i) => i.id),
        this.linkedIncidents().map((i) => i.id),
      ),
      (value) => this.changed(value),
    );
  }
  loadOptions(workDelta = 0, incidentDelta = 0) {
    const selected = this.selected();
    if (!selected || !this.canEdit) return;
    const workPage = Math.max(1, this.workPage() + workDelta);
    const incidentPage = Math.max(1, this.incidentPage() + incidentDelta);
    this.optionsRequest?.unsubscribe();
    this.optionsRequest = forkJoin({
      work: this.workApi.list(this.org, this.projectId, {
        page: workPage,
        pageSize: 50,
        sortBy: 'CreatedAtUtc',
        sortDirection: 'desc',
      }),
      incidents: this.incidentApi.list(this.org, this.projectId, {
        page: incidentPage,
        severity: '',
        status: '',
        search: '',
      }),
    }).subscribe({
      next: (r) => {
        if (this.selected()?.id !== selected.id) return;
        this.workPage.set(workPage);
        this.incidentPage.set(incidentPage);
        this.workOptions.set(r.work.items);
        this.incidentOptions.set(r.incidents.items);
        this.workTotal.set(r.work.totalCount);
        this.incidentTotal.set(r.incidents.totalCount);
      },
      error: (e) => this.detailError.set(this.describe(e)),
    });
    this.detail.add(this.optionsRequest);
  }
  moveTimeline(delta: number) {
    const r = this.selected();
    const page = this.timelinePage() + delta;
    if (!r || page < 1 || page > Math.max(1, Math.ceil(this.timelineTotal() / 50))) return;
    this.timelineRequest?.unsubscribe();
    this.timelineRequest = this.api.timeline(this.org, this.projectId, r.id, page).subscribe({
      next: (r) => {
        this.timelinePage.set(page);
        this.timeline.set(r.items);
      },
      error: (e) => this.detailError.set(this.describe(e)),
    });
    this.detail.add(this.timelineRequest);
  }
  eventLabel(action: string) {
    return (
      (
        {
          'Release.Created': 'Draft created',
          'Release.NotesUpdated': 'Draft notes updated',
          'Release.LinksUpdated': 'Linked resources updated',
          'Release.StatusChanged': 'Release status changed',
        } as Record<string, string>
      )[action] ?? 'Release event'
    );
  }
  private changed(release: Release) {
    if (this.selected()?.id !== release.id) return;
    this.open(release.id);
    this.load();
  }
  private run<T>(request: Observable<T>, next: (r: T) => void, detail = true) {
    this.saving.set(true);
    (detail ? this.detailError : this.error).set('');
    this.mutation = request
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: (r) => {
          this.saving.set(false);
          next(r);
        },
        error: (e) => (detail ? this.detailError : this.error).set(this.describe(e)),
      });
  }
  private describe(e: HttpErrorResponse) {
    if (e.status === 409)
      return 'The record changed or this action is no longer valid. Refresh before trying again.';
    if (e.status === 403) return 'Your current role does not allow this action.';
    if (e.status === 404)
      return 'This release, project, environment or linked resource is no longer available.';
    return e.error?.message ?? 'Release information could not be loaded. Try again.';
  }
}
