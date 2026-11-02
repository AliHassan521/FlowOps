import { TestBed } from '@angular/core/testing';
import { of, Subject } from 'rxjs';
import { IncidentService } from './incident.service';
import { IncidentSummary } from './incident.models';
import { IncidentSummaryComponent } from './incident-summary.component';
describe('IncidentSummaryComponent', () => {
  it('cancels obsolete tenant metrics and uses server aggregates', () => {
    const first = new Subject<IncidentSummary>();
    const summary = vi
      .fn()
      .mockImplementation((org: string) =>
        org === 'old'
          ? first
          : of({
              activeCount: 4,
              criticalCount: 2,
              atRiskCount: 1,
              breachedCount: 3,
              resolvedCount: 5,
            }),
      );
    TestBed.configureTestingModule({
      imports: [IncidentSummaryComponent],
      providers: [{ provide: IncidentService, useValue: { summary } }],
    });
    const fixture = TestBed.createComponent(IncidentSummaryComponent);
    fixture.componentRef.setInput('organizationId', 'old');
    fixture.detectChanges();
    fixture.componentRef.setInput('organizationId', 'new');
    fixture.detectChanges();
    first.next({
      activeCount: 99,
      criticalCount: 99,
      atRiskCount: 99,
      breachedCount: 99,
      resolvedCount: 99,
    });
    first.complete();
    fixture.detectChanges();
    expect(fixture.componentInstance.summary()?.activeCount).toBe(4);
    expect(fixture.nativeElement.textContent).not.toContain('99');
  });
});
