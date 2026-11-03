import { DatePipe } from '@angular/common';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { auditTime, finalize, Subscription } from 'rxjs';
import { API_BASE_URL } from '../../core/config/api.config';
import { WorkspaceHeaderComponent } from '../../shared/workspace-header.component';
import { OrganizationService } from '../organizations/organization.service';
import { Organization, Project } from '../organizations/organization.models';
import { NotificationService } from '../notifications/notification.service';
import { Release } from '../releases/release.models';

export interface EngineeringMetrics {
  serverTimeUtc: string;
  periodDays: number;
  openWorkItems: number;
  criticalWorkItems: number;
  activeIncidents: number;
  criticalIncidents: number;
  unassignedWorkItems: number;
  slaAtRisk: number;
  slaBreached: number;
  slaMet: number;
  resolvedWorkItemsInPeriod: number;
  resolvedIncidentsInPeriod: number;
  successfulReleasesInPeriod: number;
  failedReleasesInPeriod: number;
  recentReleases: Release[];
  teamWorkload: {
    userId: string;
    fullName: string;
    openWorkItems: number;
    criticalWorkItems: number;
    activeIncidents: number;
  }[];
  workloadPage: number;
  workloadTotalCount: number;
}

@Component({
  selector: 'app-engineering-dashboard',
  standalone: true,
  imports: [DatePipe, RouterLink, WorkspaceHeaderComponent],
  templateUrl: './engineering-dashboard.component.html',
  styleUrl: './engineering-dashboard.component.css',
})
export class EngineeringDashboardComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly orgApi = inject(OrganizationService);
  private readonly inbox = inject(NotificationService);
  private readonly destroy = inject(DestroyRef);
  readonly organization = signal<Organization | null>(null);
  readonly projects = signal<Project[]>([]);
  readonly projectTotal = signal(0);
  readonly loadingProjects = signal(false);
  readonly metrics = signal<EngineeringMetrics | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly projectId = signal('');
  readonly periodDays = signal(30);
  readonly workloadPage = signal(1);
  private org = '';
  private scope = new Subscription();
  private request?: Subscription;
  private projectPage = 0;
  ngOnInit() {
    this.destroy.onDestroy(() => {
      this.scope.unsubscribe();
      this.request?.unsubscribe();
    });
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroy)).subscribe((params) => {
      this.scope.unsubscribe();
      this.scope = new Subscription();
      this.request?.unsubscribe();
      this.org = params.get('organizationId') ?? '';
      this.organization.set(null);
      this.projects.set([]);
      this.projectPage = 0;
      this.projectTotal.set(0);
      this.projectId.set('');
      this.workloadPage.set(1);
      this.metrics.set(null);
      this.error.set('');
      if (!this.org) {
        this.error.set('Select an organization to view engineering metrics.');
        return;
      }
      this.scope.add(
        this.orgApi.get(this.org).subscribe({
          next: (organization) => {
            this.organization.set(organization);
            this.loadProjects();
            this.refresh();
          },
          error: () => this.error.set('This organization is no longer available.'),
        }),
      );
    });
    this.inbox.changes$?.pipe(auditTime(750), takeUntilDestroyed(this.destroy)).subscribe(() => {
      if (this.organization()) this.refresh();
    });
  }
  loadProjects() {
    if (this.loadingProjects()) return;
    this.loadingProjects.set(true);
    const page = this.projectPage + 1;
    this.scope.add(
      this.orgApi
        .projects(this.org, page)
        .pipe(finalize(() => this.loadingProjects.set(false)))
        .subscribe({
          next: (response) => {
            this.projectPage = page;
            this.projects.update((items) => [
              ...new Map([...items, ...response.items].map((p) => [p.id, p])).values(),
            ]);
            this.projectTotal.set(response.totalCount);
          },
          error: () => this.error.set('Project choices could not be loaded. Try again.'),
        }),
    );
  }
  selectProject(event: Event) {
    this.projectId.set((event.target as HTMLSelectElement).value);
    this.workloadPage.set(1);
    this.refresh();
  }
  selectPeriod(event: Event) {
    this.periodDays.set(Number((event.target as HTMLSelectElement).value));
    this.workloadPage.set(1);
    this.refresh();
  }
  moveWorkload(delta: number) {
    const page = this.workloadPage() + delta;
    if (page < 1 || page > Math.max(1, Math.ceil((this.metrics()?.workloadTotalCount ?? 0) / 20)))
      return;
    this.workloadPage.set(page);
    this.refresh();
  }
  refresh() {
    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set('');
    this.metrics.set(null);
    let params = new HttpParams()
      .set('periodDays', this.periodDays())
      .set('workloadPage', this.workloadPage());
    if (this.projectId()) params = params.set('projectId', this.projectId());
    this.request = this.http
      .get<EngineeringMetrics>(`${API_BASE_URL}/organizations/${this.org}/dashboard`, { params })
      .pipe(
        takeUntilDestroyed(this.destroy),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (metrics) => this.metrics.set(metrics),
        error: () =>
          this.error.set(
            'Metrics could not be loaded for your current access. Retry or choose another project.',
          ),
      });
  }
}
