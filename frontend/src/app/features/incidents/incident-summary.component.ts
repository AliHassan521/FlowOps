import { Component, effect, inject, input, signal } from '@angular/core';

import { IncidentService } from './incident.service';

import { IncidentSummary } from './incident.models';

@Component({
  selector: 'app-incident-summary',
  standalone: true,

  template: `@if (summary(); as counts) {
      <div class="metrics" aria-label="Incident summary">
        <span
          ><strong>{{ counts.activeCount }}</strong> Active incidents</span
        ><span class="critical"
          ><strong>{{ counts.criticalCount }}</strong> Critical</span
        ><span
          ><strong>{{ counts.atRiskCount }}</strong> SLA at risk</span
        ><span
          ><strong>{{ counts.breachedCount }}</strong> Breached SLAs</span
        ><span
          ><strong>{{ counts.resolvedCount }}</strong> Resolved</span
        >
      </div>
    } @else if (error()) {
      <p role="status">
        Incident metrics unavailable. <button type="button" (click)="reload()">Retry</button>
      </p>
    } @else {
      <p role="status">Loading incident metrics…</p>
    }`,

  styles: [
    `
      :host {
        display: block;
      }
      .metrics {
        display: flex;
        flex-wrap: wrap;
        gap: 14px 24px;
        padding: 18px 22px;
        background: #f7f9fc;
        border: 1px solid #e8edf4;
        border-radius: 12px;
        color: #617086;
        font-size: 12px;
      }
      .metrics span {
        display: grid;
        gap: 4px;
      }
      .metrics strong {
        font-size: 23px;
        color: #253650;
      }
      .metrics .critical strong {
        color: #b6384c;
      }
      p {
        font-size: 12px;
        color: #65748a;
      }
      button {
        border: 0;
        background: none;
        color: #4b5fc5;
        cursor: pointer;
      }
    `,
  ],
})
export class IncidentSummaryComponent {
  readonly organizationId = input.required<string>();
  readonly projectId = input<string>();
  readonly refreshKey = input(0);

  private readonly api = inject(IncidentService);
  readonly summary = signal<IncidentSummary | null>(null);
  readonly error = signal(false);

  private reloadKey = signal(0);

  constructor() {
    effect((onCleanup) => {
      this.refreshKey();
      this.reloadKey();
      this.summary.set(null);
      this.error.set(false);

      const request = this.api
        .summary(this.organizationId(), this.projectId())
        .subscribe({ next: (value) => this.summary.set(value), error: () => this.error.set(true) });

      onCleanup(() => request.unsubscribe());
    });
  }

  reload(): void {
    this.reloadKey.update((value) => value + 1);
  }
}
