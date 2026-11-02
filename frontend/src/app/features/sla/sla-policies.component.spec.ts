import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { NotificationService } from '../notifications/notification.service';
import { OrganizationService } from '../organizations/organization.service';
import { SlaPoliciesComponent } from './sla-policies.component';
import { SlaService } from './sla.service';

describe('SlaPoliciesComponent', () => {
  const policy = { id: 'policy-1', name: 'Critical', durationMinutes: 240, warningBeforeMinutes: 60, isActive: true, createdAtUtc: '2026-11-02T10:00:00Z' };
  let api: { policies: ReturnType<typeof vi.fn>; summary: ReturnType<typeof vi.fn>; createPolicy: ReturnType<typeof vi.fn>; archivePolicy: ReturnType<typeof vi.fn> };
  beforeEach(() => {
    api = { policies: vi.fn().mockReturnValue(of({ items: [policy], totalCount: 1 })), summary: vi.fn().mockReturnValue(of({ activeCount: 2, atRiskCount: 1, breachedCount: 3, metCount: 5 })),
      createPolicy: vi.fn().mockReturnValue(of(policy)), archivePolicy: vi.fn().mockReturnValue(of(undefined)) };
    TestBed.configureTestingModule({ imports: [SlaPoliciesComponent], providers: [provideRouter([]),
      { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ organizationId: 'org-1' })) } },
      { provide: SlaService, useValue: api }, { provide: OrganizationService, useValue: { get: () => of({ id: 'org-1', name: 'Team', currentUserRole: 'Admin' }) } },
      { provide: NotificationService, useValue: { unreadCount: () => 0, reload: vi.fn() } },
    ] });
  });
  it('validates the warning threshold and submits a policy within the selected tenant', () => {
    const fixture = TestBed.createComponent(SlaPoliciesComponent); fixture.detectChanges(); const component = fixture.componentInstance;
    component.form.setValue({ name: 'Critical', durationMinutes: 60, warningBeforeMinutes: 60 }); component.create();
    expect(api.createPolicy).not.toHaveBeenCalled(); expect(component.form.hasError('warningTooLarge')).toBe(true);
    component.form.setValue({ name: '  Critical  ', durationMinutes: 240, warningBeforeMinutes: 60 }); component.create();
    expect(api.createPolicy).toHaveBeenCalledWith('org-1', { name: 'Critical', durationMinutes: 240, warningBeforeMinutes: 60 });
  });
  it('requires confirmation before archiving and preserves the original policy rules', () => {
    const fixture = TestBed.createComponent(SlaPoliciesComponent); fixture.detectChanges(); const component = fixture.componentInstance;
    component.archive(policy); expect(api.archivePolicy).not.toHaveBeenCalled(); expect(component.pendingArchive()).toBe(policy.id);
    component.archive(policy); expect(api.archivePolicy).toHaveBeenCalledWith('org-1', policy.id);
    expect(component.notice()).toContain('Existing measurements keep their deadlines');
  });
});
