import { ActivatedRoute } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { OrganizationService } from '../organizations/organization.service';
import { WorkItemService } from './work-item.service';
import { WorkItemsComponent } from './work-items.component';
import { WorkItem } from './work-item.models';

describe('WorkItemsComponent', () => {
  const item: WorkItem = {
    id: 'item-1', projectId: 'project-1', type: 'Task', title: 'Ship the API', description: null,
    priority: 'Medium', status: 'Assigned', reporterUserId: 'admin-id', reporterName: 'Admin',
    assigneeUserId: 'engineer-id', assigneeName: 'Engineer', dueAtUtc: null,
    createdAtUtc: '2026-10-30T00:00:00Z', updatedAtUtc: '2026-10-30T00:00:00Z', commentCount: 0,
  };
  let workItems: Record<string, ReturnType<typeof vi.fn>>;
  let organizations: Record<string, ReturnType<typeof vi.fn>>;
  let role: string;
  let userId: string;

  beforeEach(() => {
    role = 'Engineer'; userId = 'engineer-id';
    workItems = {
      list: vi.fn().mockReturnValue(of({ items: [item], page: 1, pageSize: 20, totalCount: 1 })),
      comments: vi.fn().mockReturnValue(of({ items: [], page: 1, pageSize: 50, totalCount: 0 })),
      history: vi.fn().mockReturnValue(of({ items: [], page: 1, pageSize: 50, totalCount: 0 })),
      create: vi.fn().mockReturnValue(of(item)), update: vi.fn().mockReturnValue(of(item)),
      assign: vi.fn().mockReturnValue(of(item)), transition: vi.fn().mockReturnValue(of(item)),
      addComment: vi.fn().mockReturnValue(of({})),
    };
    organizations = {
      get: vi.fn().mockReturnValue(of({ id: 'org-1', name: 'Platform', slug: 'platform', currentUserRole: 'Engineer' })),
      project: vi.fn().mockReturnValue(of({ id: 'project-1', organizationId: 'org-1', name: 'Reliability', key: 'REL' })),
      members: vi.fn().mockReturnValue(of({ items: [{ userId: 'engineer-id', fullName: 'Engineer', role: 'Engineer', isActive: true }] })),
    };
    TestBed.configureTestingModule({
      imports: [WorkItemsComponent],
      providers: [
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: (key: string) => key === 'organizationId' ? 'org-1' : 'project-1' } } } },
        { provide: OrganizationService, useValue: organizations },
        { provide: WorkItemService, useValue: workItems },
        { provide: AuthService, useValue: { currentUser: () => ({ userId, fullName: 'Engineer' }) } },
      ],
    });
  });

  it('loads project metadata and a paged work item list', () => {
    const fixture = TestBed.createComponent(WorkItemsComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    expect(component.project()?.key).toBe('REL');
    expect(component.items()).toEqual([item]);
    expect(workItems['list']).toHaveBeenCalledWith('org-1', 'project-1', expect.objectContaining({ page: 1, pageSize: 20 }));
  });

  it('validates and creates a work item, then selects the created item', () => {
    const fixture = TestBed.createComponent(WorkItemsComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.create();
    expect(workItems['create']).not.toHaveBeenCalled();
    component.createForm.setValue({ type: 'Task', title: '  Add metrics  ', description: '', priority: 'High', assigneeUserId: '', dueAtUtc: '' });
    component.create();
    expect(workItems['create']).toHaveBeenCalledWith('org-1', 'project-1', expect.objectContaining({ title: 'Add metrics', assigneeUserId: null }));
    expect(component.selected()?.id).toBe(item.id);
  });

  it('shows transition actions only for the assigned engineer and handles a server denial', () => {
    const fixture = TestBed.createComponent(WorkItemsComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.open(item);
    expect(component.canTransition('InProgress')).toBe(true);
    expect(component.canTransition('Closed')).toBe(false);
    userId = 'other-user';
    expect(component.canTransition('InProgress')).toBe(false);
    workItems['transition'].mockReturnValueOnce(throwError(() => ({ status: 403, error: {} })));
    component.transition('InProgress');
    expect(component.error()).toContain('does not allow');
  });
});
