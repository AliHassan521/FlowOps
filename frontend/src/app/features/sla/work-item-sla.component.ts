import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize, forkJoin, of, Subscription } from 'rxjs';
import { SlaPolicy, SlaState } from './sla.models';
import { SlaService } from './sla.service';

@Component({
  selector: 'app-work-item-sla', standalone: true, imports: [DatePipe, ReactiveFormsModule, RouterLink],
  templateUrl: './work-item-sla.component.html', styleUrl: './work-item-sla.component.css',
})
export class WorkItemSlaComponent {
  readonly organizationId = input.required<string>();
  readonly projectId = input.required<string>();
  readonly workItemId = input.required<string>();
  readonly role = input('');
  readonly itemStatus = input('');
  private readonly api = inject(SlaService);
  private readonly forms = inject(FormBuilder);
  readonly state = signal<SlaState | null>(null);
  readonly policies = signal<SlaPolicy[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly morePolicies = signal(false);
  readonly loadingPolicies = signal(false);
  readonly canManage = computed(() => ['Admin', 'TeamLead'].includes(this.role()));
  readonly canStart = computed(() => this.canManage() && !['Resolved', 'Closed'].includes(this.itemStatus()));
  readonly form = this.forms.nonNullable.group({ policyId: ['', Validators.required] });
  private initial?: Subscription;
  private statusRequest?: Subscription;
  private mutation?: Subscription;
  private policyRequest?: Subscription;
  private policyPage = 1;

  constructor() {
    effect(onCleanup => {
      this.organizationId(); this.projectId(); this.workItemId(); this.role(); this.itemStatus();
      untracked(() => this.load());
      const timer = setInterval(() => this.refreshStatus(), 30000);
      onCleanup(() => { clearInterval(timer); this.initial?.unsubscribe(); this.statusRequest?.unsubscribe(); this.mutation?.unsubscribe(); this.policyRequest?.unsubscribe(); });
    });
  }

  load(): void {
    this.initial?.unsubscribe(); this.statusRequest?.unsubscribe(); this.policyRequest?.unsubscribe();
    this.state.set(null); this.error.set(''); this.loading.set(true); this.policyPage = 1;
    this.initial = forkJoin({ state: this.api.state(this.organizationId(), this.projectId(), this.workItemId()),
      policies: this.canManage() ? this.api.policies(this.organizationId()) : of({ items: [], totalCount: 0 }),
    }).pipe(finalize(() => this.loading.set(false))).subscribe({
      next: result => { this.state.set(result.state); this.policies.set(result.policies.items); this.morePolicies.set(result.policies.totalCount > 50); },
      error: error => this.error.set(this.describe(error)),
    });
  }

  refreshStatus(): void {
    if (this.saving() || this.loading()) return;
    this.statusRequest?.unsubscribe();
    this.statusRequest = this.api.state(this.organizationId(), this.projectId(), this.workItemId()).subscribe({
      next: state => { this.state.set(state); this.error.set(''); },
      error: error => { this.state.set(null); this.error.set(this.describe(error)); },
    });
  }

  start(): void {
    this.form.markAllAsTouched();
    if (!this.canStart() || this.form.invalid || this.saving() || this.loading() || !this.state() || this.state()?.sla) return;
    this.statusRequest?.unsubscribe(); this.saving.set(true); this.error.set('');
    this.mutation = this.api.attach(this.organizationId(), this.projectId(), this.workItemId(), this.form.controls.policyId.value)
      .pipe(finalize(() => this.saving.set(false))).subscribe({
        next: state => this.state.set(state), error: error => this.error.set(this.describe(error)),
      });
  }

  loadMorePolicies(): void {
    if (this.loadingPolicies() || !this.morePolicies()) return;
    this.loadingPolicies.set(true);
    this.policyRequest = this.api.policies(this.organizationId(), this.policyPage + 1).pipe(finalize(() => this.loadingPolicies.set(false)))
      .subscribe({ next: result => { this.policyPage++; this.policies.update(items => [...new Map([...items, ...result.items].map(item => [item.id, item])).values()]); this.morePolicies.set(this.policyPage * 50 < result.totalCount); }, error: error => this.error.set(this.describe(error)) });
  }

  timing(): string {
    const sla = this.state()?.sla;
    if (!sla) return '';
    if (sla.status === 'Met') return 'Completed before the deadline';
    if (sla.completedAtUtc) return 'Completed after the deadline';
    return sla.remainingSeconds > 0 ? `${Math.ceil(sla.remainingSeconds / 60)} min remaining`
      : `Overdue by ${Math.ceil(Math.abs(sla.remainingSeconds) / 60)} min`;
  }
  private describe(error: HttpErrorResponse): string {
    if (error.status === 403) return 'Your organization role cannot manage SLA measurements.';
    if (error.status === 404) return 'The work item or SLA policy is no longer available.';
    return error.error?.message ?? 'SLA information could not be loaded. Try again.';
  }
}
