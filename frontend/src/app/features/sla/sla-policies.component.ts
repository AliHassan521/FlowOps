import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { finalize, forkJoin, Subscription } from 'rxjs';
import { WorkspaceHeaderComponent } from '../../shared/workspace-header.component';
import { Organization } from '../organizations/organization.models';
import { OrganizationService } from '../organizations/organization.service';
import { SlaPolicy, SlaSummary } from './sla.models';
import { SlaService } from './sla.service';

@Component({
  selector: 'app-sla-policies', standalone: true,
  imports: [DatePipe, ReactiveFormsModule, RouterLink, WorkspaceHeaderComponent],
  templateUrl: './sla-policies.component.html', styleUrl: './sla-policies.component.css',
})
export class SlaPoliciesComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly api = inject(SlaService);
  private readonly organizations = inject(OrganizationService);
  private readonly forms = inject(FormBuilder);
  readonly organization = signal<Organization | null>(null);
  readonly policies = signal<SlaPolicy[]>([]);
  readonly summary = signal<SlaSummary | null>(null);
  readonly page = signal(1);
  readonly total = signal(0);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly pendingArchive = signal<string | null>(null);
  readonly form = this.forms.nonNullable.group({ name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(120)]],
    durationMinutes: [240, [Validators.required, Validators.min(1), Validators.max(43200), Validators.pattern(/^\d+$/)]],
    warningBeforeMinutes: [60, [Validators.required, Validators.min(0), Validators.max(43199), Validators.pattern(/^\d+$/)]],
  }, { validators: (form: AbstractControl) => form.get('warningBeforeMinutes')!.value >= form.get('durationMinutes')!.value ? { warningTooLarge: true } : null });
  private organizationId = '';
  private request?: Subscription;
  get canManage(): boolean { return ['Admin', 'TeamLead'].includes(this.organization()?.currentUserRole ?? ''); }
  get pages(): number { return Math.max(1, Math.ceil(this.total() / 50)); }

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      this.organizationId = params.get('organizationId') ?? ''; this.page.set(1); this.pendingArchive.set(null);
      this.organization.set(null); this.policies.set([]); this.summary.set(null); this.load();
    });
  }
  load(): void {
    this.request?.unsubscribe(); this.loading.set(true); this.error.set('');
    this.request = forkJoin({ organization: this.organizations.get(this.organizationId),
      policies: this.api.policies(this.organizationId, this.page(), false), summary: this.api.summary(this.organizationId),
    }).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false))).subscribe({
      next: value => { this.organization.set(value.organization); this.policies.set(value.policies.items); this.total.set(value.policies.totalCount); this.summary.set(value.summary); if (this.page() > this.pages) { this.page.set(this.pages); this.load(); } },
      error: error => { this.policies.set([]); this.summary.set(null); this.error.set(this.describe(error)); },
    });
  }
  movePage(delta: number): void { this.page.set(Math.max(1, Math.min(this.pages, this.page() + delta))); this.pendingArchive.set(null); this.load(); }
  create(): void {
    this.form.markAllAsTouched(); if (!this.canManage || this.form.invalid || this.saving()) return;
    const org = this.organizationId; const value = this.form.getRawValue(); this.saving.set(true);
    this.api.createPolicy(org, { ...value, name: value.name.trim() }).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.saving.set(false)))
      .subscribe({ next: () => { if (org !== this.organizationId) return; this.form.reset({ name: '', durationMinutes: 240, warningBeforeMinutes: 60 }); this.notice.set('SLA policy created. Attach it from a work item to start its clock.'); this.page.set(1); this.load(); }, error: error => this.error.set(this.describe(error)) });
  }
  archive(policy: SlaPolicy): void {
    if (!this.canManage || !policy.isActive || this.saving()) return;
    if (this.pendingArchive() !== policy.id) { this.pendingArchive.set(policy.id); return; }
    const org = this.organizationId; this.saving.set(true);
    this.api.archivePolicy(org, policy.id).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.saving.set(false)))
      .subscribe({ next: () => { if (org !== this.organizationId) return; this.pendingArchive.set(null); this.notice.set('Policy archived. Existing measurements keep their deadlines.'); this.load(); }, error: error => this.error.set(this.describe(error)) });
  }
  private describe(error: HttpErrorResponse): string {
    if (error.status === 403) return 'Your organization role cannot manage SLA policies.';
    if (error.status === 404) return 'This organization or policy is no longer available to you.';
    return error.error?.message ?? 'SLA policies could not be loaded. Try again.';
  }
}
