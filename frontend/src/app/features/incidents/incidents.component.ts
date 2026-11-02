import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { combineLatest, finalize, forkJoin, Observable, of, Subscription } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { WorkspaceHeaderComponent } from '../../shared/workspace-header.component';
import { Organization, OrganizationMember, Project } from '../organizations/organization.models';
import { OrganizationService } from '../organizations/organization.service';
import { NotificationService } from '../notifications/notification.service';
import { SlaService } from '../sla/sla.service';
import { SlaPolicy } from '../sla/sla.models';
import {
  Incident,
  IncidentComment,
  IncidentEvent,
  IncidentInput,
  IncidentSlaState,
  IncidentStatus,
  INCIDENT_SEVERITIES,
  INCIDENT_STATUSES,
} from './incident.models';
import { IncidentService } from './incident.service';
import { IncidentSummaryComponent } from './incident-summary.component';
@Component({
  selector: 'app-incidents',
  standalone: true,
  imports: [
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
    WorkspaceHeaderComponent,
    IncidentSummaryComponent,
  ],
  templateUrl: './incidents.component.html',
  styleUrl: './incidents.component.css',
})
export class IncidentsComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly destroy = inject(DestroyRef);
  private readonly forms = inject(FormBuilder);
  private readonly api = inject(IncidentService);
  private readonly orgApi = inject(OrganizationService);
  private readonly slaApi = inject(SlaService);
  readonly auth = inject(AuthService);
  private readonly inbox = inject(NotificationService);
  readonly updatesAvailable = signal(false);
  readonly organization = signal<Organization | null>(null);
  readonly project = signal<Project | null>(null);
  readonly members = signal<OrganizationMember[]>([]);
  readonly policies = signal<SlaPolicy[]>([]);
  readonly items = signal<Incident[]>([]);
  readonly selected = signal<Incident | null>(null);
  readonly comments = signal<IncidentComment[]>([]);
  readonly timeline = signal<IncidentEvent[]>([]);
  readonly sla = signal<IncidentSlaState | null>(null);
  readonly loading = signal(false);
  readonly detailLoading = signal(false);
  readonly saving = signal(false);
  readonly showCreate = signal(false);
  readonly error = signal('');
  readonly detailError = signal('');
  readonly notice = signal('');
  readonly page = signal(1);
  readonly total = signal(0);
  readonly commentPage = signal(1);
  readonly commentTotal = signal(0);
  readonly timelinePage = signal(1);
  readonly timelineTotal = signal(0);
  readonly moreMembers = signal(false);
  readonly morePolicies = signal(false);
  readonly loadingOptions = signal(false);
  readonly refreshKey = signal(0);
  readonly severities = INCIDENT_SEVERITIES;
  readonly statuses = INCIDENT_STATUSES;
  readonly filters = this.forms.nonNullable.group({ search: [''], severity: [''], status: [''] });
  readonly createForm = this.forms.nonNullable.group({ ...this.fields(), ownerUserId: [''] });
  readonly editForm = this.forms.nonNullable.group(this.fields());
  readonly resolutionForm = this.forms.nonNullable.group({
    resolution: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(4000)]],
    rootCause: ['', Validators.maxLength(4000)],
  });
  readonly commentForm = this.forms.nonNullable.group({
    body: ['', [Validators.required, Validators.maxLength(4000)]],
  });
  readonly slaForm = this.forms.nonNullable.group({ policyId: ['', Validators.required] });
  private scope = new Subscription();
  private detail = new Subscription();
  private mutation?: Subscription;
  private listing?: Subscription;
  private memberPage = 1;
  private policyPage = 1;
  private organizationId = '';
  private projectId = '';
  get role(): string {
    return this.organization()?.currentUserRole ?? '';
  }
  get canWrite(): boolean {
    return !!this.role && this.role !== 'Viewer';
  }
  get canManage(): boolean {
    return ['Admin', 'TeamLead'].includes(this.role);
  }
  get terminal(): boolean {
    return ['Resolved', 'Closed'].includes(this.selected()?.status ?? '');
  }
  get canEdit(): boolean {
    const item = this.selected();
    const user = this.auth.currentUser()?.userId;
    return (
      this.canWrite &&
      !this.terminal &&
      !!item &&
      (this.canManage || item.ownerUserId === user || item.reporterUserId === user)
    );
  }
  get nextStatus(): IncidentStatus | null {
    const item = this.selected();
    if (!item || !this.canWrite) return null;
    if (item.status === 'Resolved') return this.canManage ? 'Closed' : null;
    if (
      item.status === 'Closed' ||
      (!this.canManage && item.ownerUserId !== this.auth.currentUser()?.userId)
    )
      return null;
    return (
      (
        { Open: 'Investigating', Investigating: 'Mitigated', Mitigated: 'Resolved' } as Record<
          string,
          IncidentStatus
        >
      )[item.status] ?? null
    );
  }
  get pages(): number {
    return Math.max(1, Math.ceil(this.total() / 20));
  }
  ngOnInit(): void {
    this.inbox.changes$
      ?.pipe(takeUntilDestroyed(this.destroy))
      .subscribe(() => this.updatesAvailable.set(true));
    this.destroy.onDestroy(() => {
      this.scope.unsubscribe();
      this.detail.unsubscribe();
      this.listing?.unsubscribe();
      this.mutation?.unsubscribe();
    });
    combineLatest([this.route.paramMap, this.route.queryParamMap])
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe(([params, query]) => {
        this.scope.unsubscribe();
        this.scope = new Subscription();
        this.close();
        this.listing?.unsubscribe();
        this.organizationId = params.get('organizationId') ?? '';
        this.projectId = params.get('projectId') ?? '';
        this.loading.set(true);
      this.organization.set(null);
        this.project.set(null);
        this.items.set([]);
        this.members.set([]);
        this.policies.set([]);
        this.error.set('');
        this.notice.set('');
        this.updatesAvailable.set(false);
        this.createForm.reset({
          title: '',
          description: '',
          affectedService: '',
          severity: 'High',
          ownerUserId: '',
        });
        this.filters.reset({ search: '', severity: '', status: '' });
        this.page.set(1);
        this.total.set(0);
        this.memberPage = 1;
        this.policyPage = 1;
        this.moreMembers.set(false);
        this.morePolicies.set(false);
        this.loadingOptions.set(false);
        this.showCreate.set(false);
        if (!this.organizationId || !this.projectId) {
          this.error.set('This incident link is incomplete.');
          return;
        }
        this.scope.add(
          forkJoin({
            organization: this.orgApi.get(this.organizationId),
            project: this.orgApi.project(this.organizationId, this.projectId),
          }).subscribe({
            next: (value) => {
              this.organization.set(value.organization);
              this.project.set(value.project);
              if (this.canManage) this.loadOptions();
              this.load();
              const linked = query.get('incidentId');
              if (linked) this.open(linked);
            },
            error: (e) => { this.loading.set(false); this.error.set(this.describe(e)); },
          }),
        );
      });
  }
  refresh(): void {
    this.updatesAvailable.set(false);
    this.load();
    const id = this.selected()?.id;
    if (id) this.open(id);
  }
  load(): void {
    this.listing?.unsubscribe();
    this.loading.set(true);
    this.error.set('');
    this.listing = this.api
      .list(this.organizationId, this.projectId, {
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
          this.items.set(r.items);
          this.total.set(r.totalCount);
          this.refreshKey.update((n) => n + 1);
        },
        error: (e) => {
          this.items.set([]);
          this.total.set(0);
          this.error.set(this.describe(e));
        },
      });
  }
  applyFilters(): void {
    this.page.set(1);
    this.load();
  }
  movePage(delta: number): void {
    this.page.set(Math.min(this.pages, Math.max(1, this.page() + delta)));
    this.load();
  }
  create(): void {
    this.createForm.markAllAsTouched();
    if (!this.canWrite || this.createForm.invalid || this.saving()) return;
    const value = this.createForm.getRawValue();
    const input = {
      ...this.input(value),
      ownerUserId: this.canManage ? value.ownerUserId || null : null,
    };
    this.run(
      this.api.create(this.organizationId, this.projectId, input),
      (item) => {
        this.showCreate.set(false);
        this.createForm.reset({
          title: '',
          description: '',
          affectedService: '',
          severity: 'High',
          ownerUserId: '',
        });
        this.open(item.id);
        this.load();
      },
      false,
    );
  }
  open(id: string): void {
    if (this.saving()) return;
    this.detail.unsubscribe();
    this.detail = new Subscription();
    this.selected.set(null);
    this.sla.set(null);
    this.comments.set([]);
    this.timeline.set([]);
    this.commentPage.set(1);
    this.timelinePage.set(1);
    this.commentTotal.set(0);
    this.timelineTotal.set(0);
    this.detailError.set('');
    this.detailLoading.set(true);
    this.resolutionForm.reset({ resolution: '', rootCause: '' });
    this.commentForm.reset({ body: '' });
    this.slaForm.reset({ policyId: '' });
    this.detail.add(
      forkJoin({
        item: this.api.get(this.organizationId, this.projectId, id),
        comments: this.api.comments(this.organizationId, this.projectId, id),
        timeline: this.api.timeline(this.organizationId, this.projectId, id),
        sla: this.api.sla(this.organizationId, this.projectId, id),
      })
        .pipe(finalize(() => this.detailLoading.set(false)))
        .subscribe({
          next: (r) => {
            this.setSelected(r.item);
            this.comments.set(r.comments.items);
            this.commentTotal.set(r.comments.totalCount);
            this.timeline.set(r.timeline.items);
            this.timelineTotal.set(r.timeline.totalCount);
            this.sla.set(r.sla);
          },
          error: (e) => this.detailError.set(this.describe(e)),
        }),
    );
  }
  close(): void {
    this.mutation?.unsubscribe();
    this.saving.set(false);
    this.detail.unsubscribe();
    this.detail = new Subscription();
    this.selected.set(null);
    this.detailLoading.set(false);
    this.detailError.set('');
  }
  update(): void {
    const item = this.selected();
    this.editForm.markAllAsTouched();
    if (!item || !this.canEdit || this.editForm.invalid || this.saving()) return;
    this.run(
      this.api.update(
        this.organizationId,
        this.projectId,
        item,
        this.input(this.editForm.getRawValue()),
      ),
      (value) => this.changed(value),
    );
  }
  assign(event: Event): void {
    const item = this.selected();
    if (!item || !this.canManage || this.terminal || this.saving()) return;
    this.run(
      this.api.assign(
        this.organizationId,
        this.projectId,
        item,
        (event.target as HTMLSelectElement).value || null,
      ),
      (value) => this.changed(value),
    );
  }
  transition(): void {
    const item = this.selected();
    const next = this.nextStatus;
    if (!item || !next || this.saving()) return;
    if (next === 'Resolved') {
      this.resolutionForm.markAllAsTouched();
      if (this.resolutionForm.invalid) return;
    }
    const value = this.resolutionForm.getRawValue();
    this.run(
      this.api.transition(
        this.organizationId,
        this.projectId,
        item,
        next,
        next === 'Resolved' ? value.resolution.trim() : null,
        next === 'Resolved' ? value.rootCause.trim() || null : null,
      ),
      (value) => this.changed(value),
    );
  }
  addComment(): void {
    const item = this.selected();
    this.commentForm.markAllAsTouched();
    const body = this.commentForm.controls.body.value.trim();
    if (!item || !this.canWrite || this.commentForm.invalid || !body || this.saving()) return;
    this.run(this.api.comment(this.organizationId, this.projectId, item.id, body), () => {
      this.commentForm.reset({ body: '' });
      this.openAfterMutation(item.id);
    });
  }
  attachSla(): void {
    const item = this.selected();
    this.slaForm.markAllAsTouched();
    if (
      !item ||
      !this.canManage ||
      this.terminal ||
      this.sla()?.sla ||
      this.slaForm.invalid ||
      this.saving()
    )
      return;
    this.run(
      this.api.attach(
        this.organizationId,
        this.projectId,
        item,
        this.slaForm.controls.policyId.value,
      ),
      () => {
        this.openAfterMutation(item.id);
        this.load();
      },
    );
  }
  moveComments(delta: number): void {
    const item = this.selected();
    if (!item) return;
    const page = Math.max(1, this.commentPage() + delta);
    if (page > Math.max(1, Math.ceil(this.commentTotal() / 50))) return;
    this.detail.add(
      this.api.comments(this.organizationId, this.projectId, item.id, page).subscribe({
        next: (r) => {
          if (this.selected()?.id !== item.id) return;
          this.commentPage.set(page);
          this.comments.set(r.items);
        },
        error: (e) => this.detailError.set(this.describe(e)),
      }),
    );
  }
  moveTimeline(delta: number): void {
    const item = this.selected();
    if (!item) return;
    const page = Math.max(1, this.timelinePage() + delta);
    if (page > Math.max(1, Math.ceil(this.timelineTotal() / 50))) return;
    this.detail.add(
      this.api.timeline(this.organizationId, this.projectId, item.id, page).subscribe({
        next: (r) => {
          if (this.selected()?.id !== item.id) return;
          this.timelinePage.set(page);
          this.timeline.set(r.items);
        },
        error: (e) => this.detailError.set(this.describe(e)),
      }),
    );
  }
  loadOptions(): void {
    if (this.loadingOptions()) return;
    this.loadingOptions.set(true);
    this.scope.add(
      forkJoin({
        members: this.orgApi.members(this.organizationId, this.memberPage),
        policies: this.slaApi.policies(this.organizationId, this.policyPage),
      })
        .pipe(finalize(() => this.loadingOptions.set(false)))
        .subscribe({
          next: (r) => {
            this.members.set(r.members.items.filter((m) => m.isActive && m.role !== 'Viewer'));
            this.policies.set(r.policies.items);
            this.moreMembers.set(this.memberPage * 50 < r.members.totalCount);
            this.morePolicies.set(this.policyPage * 50 < r.policies.totalCount);
          },
          error: (e) => this.error.set(this.describe(e)),
        }),
    );
  }
  loadMoreMembers(): void {
    if (!this.canManage || this.loadingOptions() || !this.moreMembers()) return;
    this.loadingOptions.set(true);
    this.scope.add(
      this.orgApi
        .members(this.organizationId, this.memberPage + 1)
        .pipe(finalize(() => this.loadingOptions.set(false)))
        .subscribe({
          next: (r) => {
            this.memberPage++;
            this.members.update((items) => [
              ...new Map(
                [...items, ...r.items.filter((m) => m.isActive && m.role !== 'Viewer')].map((m) => [
                  m.userId,
                  m,
                ]),
              ).values(),
            ]);
            this.moreMembers.set(this.memberPage * 50 < r.totalCount);
          },
          error: (e) => this.error.set(this.describe(e)),
        }),
    );
  }
  loadMorePolicies(): void {
    if (!this.canManage || this.loadingOptions() || !this.morePolicies()) return;
    this.loadingOptions.set(true);
    this.scope.add(
      this.slaApi
        .policies(this.organizationId, this.policyPage + 1)
        .pipe(finalize(() => this.loadingOptions.set(false)))
        .subscribe({
          next: (r) => {
            this.policyPage++;
            this.policies.update((items) => [
              ...new Map([...items, ...r.items].map((p) => [p.id, p])).values(),
            ]);
            this.morePolicies.set(this.policyPage * 50 < r.totalCount);
          },
          error: (e) => this.error.set(this.describe(e)),
        }),
    );
  }
  eventLabel(action: string): string {
    return (
      (
        {
          'Incident.Created': 'Incident reported',
          'Incident.Updated': 'Details updated',
          'Incident.Assigned': 'Owner changed',
          'Incident.StatusChanged': 'Status changed',
          'Incident.CommentAdded': 'Comment added',
          'Incident.SlaAttached': 'SLA started',
          'Incident.SlaWarning': 'SLA deadline approaching',
          'Incident.SlaBreached': 'SLA deadline breached',
          'Incident.SlaCompleted': 'SLA completed',
        } as Record<string, string>
      )[action] ?? 'Incident event'
    );
  }
  private run<T>(request: Observable<T>, next: (value: T) => void, detail = true): void {
    this.saving.set(true);
    (detail ? this.detailError : this.error).set('');
    this.mutation = request
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: (value) => {
          this.saving.set(false);
          next(value);
        },
        error: (e) => (detail ? this.detailError : this.error).set(this.describe(e)),
      });
  }
  private changed(item: Incident): void {
    if (this.selected()?.id !== item.id) return;
    this.openAfterMutation(item.id);
    this.load();
  }
  private openAfterMutation(id: string): void {
    this.saving.set(false);
    this.open(id);
  }
  private setSelected(item: Incident): void {
    this.selected.set(item);
    this.editForm.reset({
      title: item.title,
      description: item.description ?? '',
      affectedService: item.affectedService,
      severity: item.severity,
    });
  }
  private fields() {
    return {
      title: this.forms.nonNullable.control('', [
        Validators.required,
        Validators.minLength(2),
        Validators.maxLength(200),
      ]),
      description: this.forms.nonNullable.control('', Validators.maxLength(8000)),
      affectedService: this.forms.nonNullable.control('', [
        Validators.required,
        Validators.minLength(2),
        Validators.maxLength(120),
      ]),
      severity: this.forms.nonNullable.control('High', Validators.required),
    };
  }
  private input(value: {
    title: string;
    description: string;
    affectedService: string;
    severity: string;
  }): IncidentInput {
    return {
      title: value.title.trim(),
      description: value.description.trim() || null,
      affectedService: value.affectedService.trim(),
      severity: value.severity,
    };
  }
  private describe(error: HttpErrorResponse): string {
    if (error.status === 403) return 'Your current role does not allow this incident action.';
    if (error.status === 404) return 'This incident, project or policy is no longer available.';
    if (error.status === 409)
      return 'The incident changed or the action is no longer valid. Refresh the incident and try again.';
    return error.error?.message ?? 'Incident information could not be loaded. Please try again.';
  }
}
