import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { SlaService } from './sla.service';
import { WorkItemSlaComponent } from './work-item-sla.component';

describe('WorkItemSlaComponent', () => {
  const empty = { sla: null, serverTimeUtc: '2026-11-02T10:00:00Z' };
  const active = { sla: { id: 'sla-1', policyId: 'policy-1', policyName: 'Critical', status: 'Active', remainingSeconds: 3600,
    deadlineAtUtc: '2026-11-02T11:00:00Z', warningAtUtc: null, completedAtUtc: null, breachedAtUtc: null }, serverTimeUtc: '2026-11-02T10:00:00Z' };
  let api: { state: ReturnType<typeof vi.fn>; policies: ReturnType<typeof vi.fn>; attach: ReturnType<typeof vi.fn> };
  beforeEach(() => {
    api = { state: vi.fn().mockReturnValue(of(empty)), policies: vi.fn().mockReturnValue(of({ items: [{ id: 'policy-1', name: 'Critical', durationMinutes: 60 }], totalCount: 1 })), attach: vi.fn().mockReturnValue(of(active)) };
    TestBed.configureTestingModule({ imports: [WorkItemSlaComponent], providers: [provideRouter([]), { provide: SlaService, useValue: api }] });
  });
  function fixture(role = 'Admin') {
    const fixture = TestBed.createComponent(WorkItemSlaComponent);
    fixture.componentRef.setInput('organizationId', 'org-1'); fixture.componentRef.setInput('projectId', 'project-1');
    fixture.componentRef.setInput('workItemId', 'item-1'); fixture.componentRef.setInput('role', role); fixture.componentRef.setInput('itemStatus', 'New');
    fixture.detectChanges(); return fixture;
  }
  it('starts an authorized measurement and displays its fixed deadline', () => {
    const view = fixture(); view.componentInstance.form.setValue({ policyId: 'policy-1' }); view.componentInstance.start(); view.detectChanges();
    expect(api.attach).toHaveBeenCalledWith('org-1', 'project-1', 'item-1', 'policy-1');
    expect(view.nativeElement.textContent).toContain('60 min remaining'); expect(view.nativeElement.textContent).toContain('Critical');
  });
  it('keeps viewer controls read-only and blocks starting an SLA on resolved work', () => {
    const view = fixture('Viewer'); expect(view.nativeElement.querySelector('select')).toBeNull();
    view.componentInstance.form.setValue({ policyId: 'policy-1' }); view.componentInstance.start(); expect(api.attach).not.toHaveBeenCalled();
    view.componentRef.setInput('role', 'Admin'); view.componentRef.setInput('itemStatus', 'Resolved'); view.detectChanges();
    expect(view.componentInstance.canStart()).toBe(false);
  });
  it('does not display a previous work item response after inputs change', () => {
    const delayed = new Subject<unknown>(); api.state.mockReturnValueOnce(delayed);
    const view = fixture(); view.componentRef.setInput('workItemId', 'item-2'); view.detectChanges();
    delayed.next(active); delayed.complete();
    expect(view.componentInstance.state()?.sla).toBeNull(); expect(api.state).toHaveBeenLastCalledWith('org-1', 'project-1', 'item-2');
  });
  it('shows an actionable server conflict instead of resetting an existing SLA', () => {
    api.attach.mockReturnValueOnce(throwError(() => ({ status: 409, error: { message: 'This work item already has an SLA.' } })));
    const view = fixture(); view.componentInstance.form.setValue({ policyId: 'policy-1' }); view.componentInstance.start(); view.detectChanges();
    expect(view.nativeElement.textContent).toContain('already has an SLA'); expect(view.componentInstance.saving()).toBe(false);
  });
});
