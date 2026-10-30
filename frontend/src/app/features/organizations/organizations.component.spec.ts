import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { Organization, Project } from './organization.models';
import { OrganizationService } from './organization.service';
import { OrganizationsComponent } from './organizations.component';

describe('OrganizationsComponent', () => {
  const adminOrganization: Organization = {
    id: 'org-1', name: 'Platform', slug: 'platform', description: 'Core team',
    currentUserRole: 'Admin', createdAtUtc: '2026-10-30T00:00:00Z', updatedAtUtc: null,
  };
  const project: Project = {
    id: 'project-1', organizationId: 'org-1', name: 'Reliability', key: 'REL', description: null,
    isActive: true, createdAtUtc: '2026-10-30T00:00:00Z', updatedAtUtc: null,
  };
  const engineer = {
    userId: 'user-2', email: 'engineer@example.com', fullName: 'Engineer', role: 'Engineer',
    joinedAtUtc: '2026-10-30T00:00:00Z', isActive: true,
  };

  let api: OrganizationService;

  beforeEach(() => {
    api = {
      list: vi.fn().mockReturnValue(of({ items: [adminOrganization], page: 1, pageSize: 50, totalCount: 1 })),
      members: vi.fn().mockReturnValue(of({ items: [], page: 1, pageSize: 50, totalCount: 0 })),
      projects: vi.fn().mockReturnValue(of({ items: [project], page: 1, pageSize: 50, totalCount: 1 })),
      update: vi.fn().mockReturnValue(of({ ...adminOrganization, name: 'Platform Engineering' })),
      deactivate: vi.fn().mockReturnValue(of(void 0)),
      deactivateMember: vi.fn().mockReturnValue(of(void 0)),
      project: vi.fn().mockReturnValue(of(project)),
      updateProject: vi.fn().mockReturnValue(of({ ...project, name: 'Reliability Engineering' })),
      deactivateProject: vi.fn().mockReturnValue(of(void 0)),
    } as unknown as OrganizationService;

    TestBed.configureTestingModule({
      imports: [OrganizationsComponent],
      providers: [provideRouter([]), { provide: OrganizationService, useValue: api }],
    });
  });

  it('updates organization details and refreshes selected and listed state', () => {
    const fixture = TestBed.createComponent(OrganizationsComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.startEditingOrganization();
    component.organizationEditForm.setValue({ name: 'Platform Engineering', description: 'Core systems' });

    component.saveOrganization();

    expect(api.update).toHaveBeenCalledWith('org-1', { name: 'Platform Engineering', description: 'Core systems' });
    expect(component.selectedOrganization()?.name).toBe('Platform Engineering');
    expect(component.organizations()[0].name).toBe('Platform Engineering');
    expect(component.editingOrganization()).toBe(false);
  });

  it('requires an explicit confirmation step before deactivating an organization', () => {
    const fixture = TestBed.createComponent(OrganizationsComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.requestOrganizationDeactivation();

    expect(api.deactivate).not.toHaveBeenCalled();
    expect(component.confirmOrganizationDeactivation()).toBe(true);

    component.deactivateOrganization();

    expect(api.deactivate).toHaveBeenCalledWith('org-1');
    expect(component.selectedOrganization()).toBeNull();
    expect(component.organizations()).toEqual([]);
  });

  it('allows a TeamLead to edit a project but does not expose organization-admin actions', () => {
    const fixture = TestBed.createComponent(OrganizationsComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.selectOrganization({ ...adminOrganization, currentUserRole: 'TeamLead' });
    component.viewProject(project);

    expect(component.canManageProjects).toBe(true);
    expect(component.canManage).toBe(false);
    component.startEditingProject();
    component.projectEditForm.setValue({ name: 'Reliability Engineering', description: 'Service ownership' });
    component.saveProject();

    expect(api.updateProject).toHaveBeenCalledWith('org-1', project, {
      name: 'Reliability Engineering', description: 'Service ownership',
    });
    expect(component.selectedProject()?.name).toBe('Reliability Engineering');
    expect(api.deactivate).not.toHaveBeenCalled();
  });

  it('requires confirmation before removing a member and protects the final active admin', () => {
    const fixture = TestBed.createComponent(OrganizationsComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.members.set([engineer]);
    component.deactivateMember(engineer);

    expect(api.deactivateMember).not.toHaveBeenCalled();
    expect(component.pendingMemberDeactivation()?.userId).toBe(engineer.userId);

    component.deactivateMember(engineer);

    expect(api.deactivateMember).toHaveBeenCalledWith('org-1', engineer);
    expect(component.members()[0].isActive).toBe(false);

    const soleAdmin = { ...engineer, userId: 'user-1', role: 'Admin' };
    component.members.set([soleAdmin]);
    component.deactivateMember(soleAdmin);
    expect(api.deactivateMember).toHaveBeenCalledTimes(1);
  });

  it('requires confirmation before archiving a project and removes it from active state', () => {
    const fixture = TestBed.createComponent(OrganizationsComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.deactivateProject(project);

    expect(api.deactivateProject).not.toHaveBeenCalled();
    expect(component.pendingProjectDeactivation()?.id).toBe(project.id);

    component.deactivateProject(project);

    expect(api.deactivateProject).toHaveBeenCalledWith('org-1', project);
    expect(component.pendingProjectDeactivation()).toBeNull();
    expect(component.projects()).toEqual([]);
  });
});
