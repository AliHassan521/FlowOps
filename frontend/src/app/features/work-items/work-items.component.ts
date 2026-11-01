import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { combineLatest, finalize, forkJoin, of, Subscription } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { Organization, OrganizationMember, Project } from '../organizations/organization.models';
import { OrganizationService } from '../organizations/organization.service';
import { WorkspaceHeaderComponent } from '../../shared/workspace-header.component';
import { WorkItem, WorkItemHistory, WorkItemStatus, WORK_ITEM_PRIORITIES, WORK_ITEM_STATUSES, WORK_ITEM_TRANSITIONS, WORK_ITEM_TYPES } from './work-item.models';
import { WorkItemService } from './work-item.service';

@Component({
  selector: 'app-work-items', standalone: true,
  imports: [DatePipe, ReactiveFormsModule, RouterLink, WorkspaceHeaderComponent],
  templateUrl: './work-items.component.html', styleUrl: './work-items.component.css',
})
export class WorkItemsComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private workspaceRequest?: Subscription;
  private listRequest?: Subscription;
  private detailRequest?: Subscription;
  private readonly formBuilder = inject(FormBuilder);
  private readonly organizationsApi = inject(OrganizationService);
  private readonly api = inject(WorkItemService);
  readonly auth = inject(AuthService);
  readonly organization = signal<Organization | null>(null);
  readonly project = signal<Project | null>(null);
  readonly members = signal<OrganizationMember[]>([]);
  readonly items = signal<WorkItem[]>([]);
  readonly selected = signal<WorkItem | null>(null);
  readonly comments = signal<{ id: string; authorName: string; body: string; createdAtUtc: string }[]>([]);
  readonly history = signal<WorkItemHistory[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly page = signal(1);
  readonly totalCount = signal(0);
  readonly commentPage = signal(1);
  readonly commentTotal = signal(0);
  readonly historyPage = signal(1);
  readonly historyTotal = signal(0);
  readonly detailLoading = signal(false);
  readonly moreMembers = signal(false);
  readonly loadingMembers = signal(false);
  private memberPage = 1;
  readonly pageSize = 20;
  readonly types = WORK_ITEM_TYPES;
  readonly priorities = WORK_ITEM_PRIORITIES;
  readonly statuses = WORK_ITEM_STATUSES;
  readonly transitions = WORK_ITEM_TRANSITIONS;
  readonly filters = this.formBuilder.nonNullable.group({ search: [''], type: [''], status: [''], priority: [''], assigneeUserId: [''] });
  readonly createForm = this.formBuilder.nonNullable.group({
    type: ['Task', Validators.required], title: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(200)]],
    description: ['', Validators.maxLength(8000)], priority: ['Medium', Validators.required], assigneeUserId: [''], dueAtUtc: [''],
  });
  readonly editForm = this.formBuilder.nonNullable.group({
    title: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(200)]],
    description: ['', Validators.maxLength(8000)], priority: ['Medium', Validators.required], dueAtUtc: [''],
  });
  readonly commentForm = this.formBuilder.nonNullable.group({ body: ['', [Validators.required, Validators.maxLength(4000)]] });
  private organizationId = '';
  private projectId = '';

  ngOnInit(): void {
    combineLatest([this.route.paramMap, this.route.queryParamMap]).pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(([params, query]) => {
        this.workspaceRequest?.unsubscribe(); this.listRequest?.unsubscribe(); this.closeDetail();
        this.organizationId = params.get('organizationId') ?? '';
        this.projectId = params.get('projectId') ?? '';
        this.organization.set(null); this.project.set(null); this.members.set([]); this.items.set([]);
        this.memberPage = 1; this.moreMembers.set(false);
        this.page.set(1); this.error.set(''); this.notice.set('');
        if (!this.organizationId || !this.projectId) { this.error.set('The project link is incomplete.'); return; }
        const linkedItemId = query.get('workItemId');
        this.workspaceRequest = forkJoin({
          organization: this.organizationsApi.get(this.organizationId),
          project: this.organizationsApi.project(this.organizationId, this.projectId),
          members: this.organizationsApi.members(this.organizationId),
          linkedItem: linkedItemId ? this.api.get(this.organizationId, this.projectId, linkedItemId) : of(null),
        }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
          next: value => {
            this.organization.set(value.organization); this.project.set(value.project);
            this.members.set(value.members.items.filter(member => member.isActive && member.role !== 'Viewer'));
            this.moreMembers.set(value.members.totalCount > 50);
            if (value.linkedItem) this.open(value.linkedItem);
          },
          error: e => this.error.set(this.describeError(e)),
        });
        this.load();
      });
  }

  get role(): string { return this.organization()?.currentUserRole ?? ''; }
  get canManage(): boolean { return ['Admin', 'TeamLead'].includes(this.role); }
  get canEdit(): boolean {
    const item = this.selected();
    const userId = this.auth.currentUser()?.userId;
    return this.canComment && (this.canManage || (!!item && !!userId && (item.reporterUserId === userId || item.assigneeUserId === userId)));
  }
  get canComment(): boolean { return !!this.role && this.role !== 'Viewer'; }
  get pages(): number { return Math.max(1, Math.ceil(this.totalCount() / this.pageSize)); }

  load(): void {
    this.listRequest?.unsubscribe();
    this.loading.set(true); this.error.set('');
    const value = this.filters.getRawValue();
    this.listRequest = this.api.list(this.organizationId, this.projectId, {
      ...value, page: this.page(), pageSize: this.pageSize, sortBy: 'createdAtUtc', sortDirection: 'desc',
    }).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false))).subscribe({
      next: result => { this.items.set(result.items); this.totalCount.set(result.totalCount); },
      error: e => this.error.set(this.describeError(e)),
    });
  }

  applyFilters(): void { this.page.set(1); this.load(); }
  movePage(delta: number): void { this.page.update(value => Math.min(this.pages, Math.max(1, value + delta))); this.load(); }

  create(): void {
    this.createForm.markAllAsTouched();
    if (this.createForm.invalid || this.saving() || !this.canComment) return;
    const value = this.createForm.getRawValue(); this.saving.set(true); this.error.set('');
    this.api.create(this.organizationId, this.projectId, {
      type: value.type, title: value.title.trim(), description: value.description.trim() || null,
      priority: value.priority, assigneeUserId: value.assigneeUserId || null, dueAtUtc: this.toUtc(value.dueAtUtc),
    }).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: item => { if (item.projectId !== this.projectId) return; this.createForm.reset({ type: 'Task', title: '', description: '', priority: 'Medium', assigneeUserId: '', dueAtUtc: '' }); this.notice.set('Work item created.'); this.open(item); this.load(); },
      error: e => this.error.set(this.describeError(e)),
    });
  }

  open(item: WorkItem): void { this.commentPage.set(1); this.historyPage.set(1); this.selected.set(item); this.loadDetail(item); this.error.set(''); }
  closeDetail(): void { this.detailRequest?.unsubscribe(); this.selected.set(null); this.comments.set([]); this.history.set([]); }

  saveDetails(): void {
    const item = this.selected(); this.editForm.markAllAsTouched();
    if (!item || !this.editForm.valid || !this.canEdit || this.saving()) return;
    const value = this.editForm.getRawValue(); this.saving.set(true);
    this.api.update(this.organizationId, this.projectId, item.id, { title: value.title.trim(), description: value.description.trim() || null, priority: value.priority, dueAtUtc: this.toUtc(value.dueAtUtc) })
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.saving.set(false))).subscribe({ next: updated => { if (this.selected()?.id !== item.id) return; this.notice.set('Work item updated.'); this.refreshSelected(updated); }, error: e => this.error.set(this.describeError(e)) });
  }

  assign(event: Event): void {
    const item = this.selected(); if (!item || !this.canManage) return;
    const assigneeUserId = (event.target as HTMLSelectElement).value || null;
    this.api.assign(this.organizationId, this.projectId, item.id, assigneeUserId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: value => { if (this.selected()?.id === item.id) this.refreshSelected(value); }, error: e => this.error.set(this.describeError(e)) });
  }

  transition(status: WorkItemStatus): void {
    const item = this.selected(); if (!item) return;
    this.api.transition(this.organizationId, this.projectId, item.id, status).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: value => { if (this.selected()?.id !== item.id) return; this.refreshSelected(value); this.notice.set(`Moved to ${status.replace(/([A-Z])/g, ' $1').trim()}.`); }, error: e => this.error.set(this.describeError(e)) });
  }

  addComment(): void {
    const item = this.selected(); this.commentForm.markAllAsTouched();
    if (!item || !this.canComment || this.commentForm.invalid) return;
    const body = this.commentForm.controls.body.value.trim(); if (!body) return;
    this.api.addComment(this.organizationId, this.projectId, item.id, body).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: () => { if (this.selected()?.id !== item.id) return; this.commentForm.reset({ body: '' }); this.commentPage.set(Math.max(1, Math.ceil((this.commentTotal() + 1) / 50))); this.historyPage.set(1); this.loadDetail(item); this.load(); }, error: e => this.error.set(this.describeError(e)) });
  }

  startEdit(): void {
    const item = this.selected(); if (!item || !this.canEdit) return;
    this.editForm.reset({ title: item.title, description: item.description ?? '', priority: item.priority, dueAtUtc: this.toLocal(item.dueAtUtc) });
  }

  memberName(id: string | null): string { return this.members().find(member => member.userId === id)?.fullName ?? 'Unassigned'; }
  statusLabel(status: string): string { return status.replace(/([A-Z])/g, ' $1').trim(); }
  canTransition(status: WorkItemStatus): boolean {
    const item = this.selected();
    if (!item || !this.transitions[item.status].includes(status)) return false;
    if (this.canManage) return true;
    const userId = this.auth.currentUser()?.userId;
    const engineer = this.role === 'Engineer' && item.assigneeUserId === userId;
    const qa = this.role === 'QA';
    return ((item.status === 'New' && status === 'Triaged' && qa)
      || (item.status === 'Assigned' && status === 'InProgress' && engineer)
      || ((item.status === 'InProgress' || item.status === 'CodeReview') && status === 'CodeReview' && engineer)
      || (item.status === 'CodeReview' && status === 'InProgress' && engineer)
      || (item.status === 'CodeReview' && status === 'QA' && qa)
      || (item.status === 'QA' && (status === 'InProgress' || status === 'Resolved') && qa));
  }

  private refreshSelected(item: WorkItem): void { this.selected.set(item); this.loadDetail(item); this.load(); }
  private loadDetail(item: WorkItem): void {
    this.detailRequest?.unsubscribe(); this.comments.set([]); this.history.set([]);
    this.detailLoading.set(true);
    this.editForm.reset({ title: item.title, description: item.description ?? '', priority: item.priority, dueAtUtc: this.toLocal(item.dueAtUtc) });
    this.detailRequest = forkJoin({ comments: this.api.comments(this.organizationId, this.projectId, item.id, this.commentPage()), history: this.api.history(this.organizationId, this.projectId, item.id, this.historyPage()) })
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.detailLoading.set(false))).subscribe({ next: result => { if (this.selected()?.id !== item.id) return; this.comments.set(result.comments.items); this.history.set(result.history.items); this.commentTotal.set(result.comments.totalCount); this.historyTotal.set(result.history.totalCount); }, error: e => this.error.set(this.describeError(e)) });
  }
  moveComments(delta: number): void { const item = this.selected(); if (!item || this.detailLoading()) return; this.commentPage.set(Math.max(1, Math.min(Math.ceil(this.commentTotal() / 50), this.commentPage() + delta))); this.loadDetail(item); }
  moveHistory(delta: number): void { const item = this.selected(); if (!item || this.detailLoading()) return; this.historyPage.set(Math.max(1, Math.min(Math.ceil(this.historyTotal() / 50), this.historyPage() + delta))); this.loadDetail(item); }
  loadMoreMembers(): void {
    if (this.loadingMembers() || !this.moreMembers()) return;
    const organizationId = this.organizationId; const page = this.memberPage + 1;
    this.loadingMembers.set(true);
    this.organizationsApi.members(organizationId, page).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loadingMembers.set(false))).subscribe({
      next: result => { if (organizationId !== this.organizationId) return; this.memberPage = page; this.moreMembers.set(page * 50 < result.totalCount); this.members.update(items => [...new Map([...items, ...result.items.filter(member => member.isActive && member.role !== 'Viewer')].map(member => [member.userId, member])).values()]); },
      error: e => this.error.set(this.describeError(e)),
    });
  }
  private toUtc(value: string): string | null { if (!value) return null; const date = new Date(value); return Number.isNaN(date.getTime()) ? null : date.toISOString(); }
  private toLocal(value: string | null): string { if (!value) return ''; const date = new Date(value); const adjusted = new Date(date.getTime() - date.getTimezoneOffset() * 60000); return adjusted.toISOString().slice(0, 16); }
  private describeError(error: HttpErrorResponse): string {
    if (error.status === 403) return 'Your organization role or work item assignment does not allow this action.';
    if (error.status === 404) return 'This project or work item is no longer available to you.';
    if (error.status === 409) return error.error?.message ?? 'The work item changed. Refresh and try again.';
    if (error.status === 0 || error.status >= 500) return 'The server could not complete the request. Try again shortly.';
    return error.error?.message ?? 'The request could not be completed.';
  }
}
