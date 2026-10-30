import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { WorkspaceHeaderComponent } from '../../shared/workspace-header.component';
import { finalize } from 'rxjs';
import { ORGANIZATION_ROLES, Organization, OrganizationMember, Project } from './organization.models';
import { OrganizationService } from './organization.service';

@Component({
  selector: 'app-organizations',
  standalone: true,
  imports: [DatePipe, ReactiveFormsModule, RouterLink, WorkspaceHeaderComponent],
  templateUrl: './organizations.component.html',
  styleUrl: './organizations.component.css',
})
export class OrganizationsComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly organizationsApi = inject(OrganizationService);
  readonly organizations = signal<Organization[]>([]);
  readonly members = signal<OrganizationMember[]>([]);
  readonly projects = signal<Project[]>([]);
  readonly selectedProject = signal<Project | null>(null);
  readonly editingOrganization = signal(false);
  readonly confirmOrganizationDeactivation = signal(false);
  readonly editingProject = signal(false);
  readonly pendingProjectDeactivation = signal<Project | null>(null);
  readonly pendingMemberDeactivation = signal<OrganizationMember | null>(null);
  readonly selectedOrganization = signal<Organization | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly savingProject = signal(false);
  readonly savingOrganization = signal(false);
  readonly savingProjectEdit = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly roles = ORGANIZATION_ROLES;

  readonly organizationForm = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(120)]],
    slug: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(80), Validators.pattern(/^[a-z0-9]+(?:-[a-z0-9]+)*$/)]],
    description: ['', Validators.maxLength(500)],
  });
  readonly organizationEditForm = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(120)]],
    description: ['', Validators.maxLength(500)],
  });
  readonly memberForm = this.formBuilder.nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    role: ['Engineer', Validators.required],
  });
  readonly projectForm = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(120)]],
    key: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(10), Validators.pattern(/^[A-Z][A-Z0-9]*$/)]],
    description: ['', Validators.maxLength(1000)],
  });
  readonly projectEditForm = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(120)]],
    description: ['', Validators.maxLength(1000)],
  });

  ngOnInit(): void {
    this.loadOrganizations();
  }

  get canManage(): boolean {
    return this.selectedOrganization()?.currentUserRole === 'Admin';
  }

  get canManageProjects(): boolean {
    return ['Admin', 'TeamLead'].includes(this.selectedOrganization()?.currentUserRole ?? '');
  }

  loadOrganizations(): void {
    this.loading.set(true);
    this.error.set('');
    this.organizationsApi.list().pipe(finalize(() => this.loading.set(false))).subscribe({
      next: (response) => {
        this.organizations.set(response.items);
        const current = this.selectedOrganization();
        const next = response.items.find((organization) => organization.id === current?.id) ?? response.items[0] ?? null;
        this.selectOrganization(next);
      },
      error: () => this.error.set('Organizations could not be loaded. Check your connection and try again.'),
    });
  }

  selectOrganization(organization: Organization | null): void {
    this.selectedOrganization.set(organization);
    this.editingOrganization.set(false);
    this.confirmOrganizationDeactivation.set(false);
    this.editingProject.set(false);
    this.pendingProjectDeactivation.set(null);
    this.pendingMemberDeactivation.set(null);
    this.members.set([]);
    this.projects.set([]);
    this.selectedProject.set(null);
    if (!organization) return;

    this.organizationsApi.members(organization.id).subscribe({
      next: (response) => {
        if (this.selectedOrganization()?.id === organization.id) this.members.set(response.items);
      },
      error: () => {
        if (this.selectedOrganization()?.id === organization.id) this.error.set('Members could not be loaded for this organization.');
      },
    });
    this.loadProjects(organization.id);
  }

  createOrganization(): void {
    this.organizationForm.markAllAsTouched();
    if (this.organizationForm.invalid || this.saving()) return;

    const value = this.organizationForm.getRawValue();
    this.saving.set(true);
    this.error.set('');
    this.organizationsApi.create({
      name: value.name.trim(),
      slug: value.slug.trim(),
      description: value.description.trim() || null,
    }).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (organization) => {
        this.organizationForm.reset({ name: '', slug: '', description: '' });
        this.organizations.set([...this.organizations(), organization].sort((a, b) => a.name.localeCompare(b.name)));
        this.selectOrganization(organization);
        this.notice.set('Organization created. You are its administrator.');
      },
      error: (response: HttpErrorResponse) => this.error.set(this.describeError(response, 'Organization could not be created.')),
    });
  }

  startEditingOrganization(): void {
    const organization = this.selectedOrganization();
    if (!organization || !this.canManage) return;
    this.organizationEditForm.reset({ name: organization.name, description: organization.description ?? '' });
    this.editingOrganization.set(true);
    this.confirmOrganizationDeactivation.set(false);
    this.error.set('');
  }

  cancelEditingOrganization(): void {
    this.editingOrganization.set(false);
  }

  saveOrganization(): void {
    const organization = this.selectedOrganization();
    this.organizationEditForm.markAllAsTouched();
    if (!organization || !this.canManage || this.organizationEditForm.invalid || this.savingOrganization()) return;
    const value = this.organizationEditForm.getRawValue();
    this.savingOrganization.set(true);
    this.error.set('');
    this.organizationsApi.update(organization.id, {
      name: value.name.trim(), description: value.description.trim() || null,
    }).pipe(finalize(() => this.savingOrganization.set(false))).subscribe({
      next: (updated) => {
        if (this.selectedOrganization()?.id === organization.id) this.selectedOrganization.set(updated);
        this.organizations.set(this.organizations().map((item) => item.id === updated.id ? updated : item)
          .sort((a, b) => a.name.localeCompare(b.name)));
        this.editingOrganization.set(false);
        this.notice.set('Organization details were updated.');
      },
      error: (response: HttpErrorResponse) => this.error.set(this.describeError(response, 'Organization could not be updated.')),
    });
  }

  requestOrganizationDeactivation(): void {
    if (!this.canManage) return;
    this.confirmOrganizationDeactivation.set(true);
    this.editingOrganization.set(false);
    this.error.set('');
  }

  cancelOrganizationDeactivation(): void {
    this.confirmOrganizationDeactivation.set(false);
  }

  deactivateOrganization(): void {
    const organization = this.selectedOrganization();
    if (!organization || !this.canManage || !this.confirmOrganizationDeactivation()) return;
    this.savingOrganization.set(true);
    this.error.set('');
    this.organizationsApi.deactivate(organization.id).pipe(finalize(() => this.savingOrganization.set(false))).subscribe({
      next: () => {
        const remaining = this.organizations().filter((item) => item.id !== organization.id);
        this.organizations.set(remaining);
        this.selectOrganization(remaining[0] ?? null);
        this.notice.set(`${organization.name} was deactivated. Its data remains in the system.`);
      },
      error: (response: HttpErrorResponse) => this.error.set(this.describeError(response, 'Organization could not be deactivated.')),
    });
  }

  addMember(): void {
    const organization = this.selectedOrganization();
    this.memberForm.markAllAsTouched();
    if (!organization || !this.canManage || this.memberForm.invalid || this.saving()) return;

    const value = this.memberForm.getRawValue();
    this.saving.set(true);
    this.error.set('');
    this.organizationsApi.addMember(organization.id, value.email.trim(), value.role)
      .pipe(finalize(() => this.saving.set(false))).subscribe({
        next: (member) => {
          if (this.selectedOrganization()?.id === organization.id) {
            this.members.set([...this.members().filter((item) => item.userId !== member.userId), member]);
          }
          this.memberForm.reset({ email: '', role: 'Engineer' });
          this.notice.set(`${member.fullName} was added to the organization.`);
        },
        error: (response: HttpErrorResponse) => this.error.set(this.describeError(response, 'Member could not be added.')),
      });
  }

  changeRole(member: OrganizationMember, event: Event): void {
    const organization = this.selectedOrganization();
    const role = (event.target as HTMLSelectElement).value;
    if (!organization || !this.canManage || role === member.role) return;

    this.error.set('');
    this.organizationsApi.updateRole(organization.id, member, role).subscribe({
      next: (updated) => {
        if (this.selectedOrganization()?.id === organization.id) {
          this.members.set(this.members().map((item) => item.userId === updated.userId ? updated : item));
        }
        this.notice.set(`${updated.fullName}'s role was updated.`);
      },
      error: (response: HttpErrorResponse) => {
        this.error.set(this.describeError(response, 'Role could not be updated.'));
        this.loadMembers(organization.id);
      },
    });
  }

  deactivateMember(member: OrganizationMember): void {
    const organization = this.selectedOrganization();
    if (!organization || !this.canManage || !member.isActive || this.isLastActiveAdmin(member)) return;

    if (this.pendingMemberDeactivation()?.userId !== member.userId) {
      this.pendingMemberDeactivation.set(member);
      return;
    }

    this.error.set('');
    this.organizationsApi.deactivateMember(organization.id, member).subscribe({
      next: () => {
        this.pendingMemberDeactivation.set(null);
        if (this.selectedOrganization()?.id === organization.id) {
          this.members.set(this.members().map((item) => item.userId === member.userId ? { ...item, isActive: false } : item));
        }
        this.notice.set(`${member.fullName} was deactivated in this organization.`);
      },
      error: (response: HttpErrorResponse) => this.error.set(this.describeError(response, 'Member could not be deactivated.')),
    });
  }

  cancelMemberDeactivation(): void {
    this.pendingMemberDeactivation.set(null);
  }

  createProject(): void {
    const organization = this.selectedOrganization();
    this.projectForm.markAllAsTouched();
    if (!organization || !this.canManageProjects || this.projectForm.invalid || this.savingProject()) return;

    const value = this.projectForm.getRawValue();
    this.savingProject.set(true);
    this.error.set('');
    this.organizationsApi.createProject(organization.id, {
      name: value.name.trim(), key: value.key, description: value.description.trim() || null,
    }).pipe(finalize(() => this.savingProject.set(false))).subscribe({
      next: (project) => {
        if (this.selectedOrganization()?.id === organization.id) {
          this.projects.set([...this.projects(), project].sort((a, b) => a.name.localeCompare(b.name)));
        }
        this.projectForm.reset({ name: '', key: '', description: '' });
        this.notice.set(`${project.key} was created.`);
      },
      error: (response: HttpErrorResponse) => this.error.set(this.describeError(response, 'Project could not be created.')),
    });
  }

  deactivateProject(project: Project): void {
    const organization = this.selectedOrganization();
    if (!organization || !this.canManage || !project.isActive) return;

    if (this.pendingProjectDeactivation()?.id !== project.id) {
      this.pendingProjectDeactivation.set(project);
      this.error.set('');
      return;
    }

    this.organizationsApi.deactivateProject(organization.id, project).subscribe({
      next: () => {
        this.pendingProjectDeactivation.set(null);
        this.selectedProject.set(this.selectedProject()?.id === project.id ? null : this.selectedProject());
        if (this.selectedOrganization()?.id === organization.id) {
          this.projects.set(this.projects().filter((item) => item.id !== project.id));
        }
        this.notice.set(`${project.key} was archived.`);
      },
      error: (response: HttpErrorResponse) => this.error.set(this.describeError(response, 'Project could not be archived.')),
    });
  }

  cancelProjectDeactivation(): void {
    this.pendingProjectDeactivation.set(null);
  }

  viewProject(project: Project): void {
    const organization = this.selectedOrganization();
    if (!organization) return;
    this.organizationsApi.project(organization.id, project.id).subscribe({
      next: (details) => {
        if (this.selectedOrganization()?.id === organization.id) this.selectedProject.set(details);
      },
      error: (response: HttpErrorResponse) => this.error.set(this.describeError(response, 'Project details could not be loaded.')),
    });
  }

  startEditingProject(): void {
    const project = this.selectedProject();
    if (!project || !this.canManageProjects) return;
    this.projectEditForm.reset({ name: project.name, description: project.description ?? '' });
    this.editingProject.set(true);
    this.error.set('');
  }

  cancelEditingProject(): void {
    this.editingProject.set(false);
  }

  saveProject(): void {
    const organization = this.selectedOrganization();
    const project = this.selectedProject();
    this.projectEditForm.markAllAsTouched();
    if (!organization || !project || !this.canManageProjects || this.projectEditForm.invalid || this.savingProjectEdit()) return;
    const value = this.projectEditForm.getRawValue();
    this.savingProjectEdit.set(true);
    this.error.set('');
    this.organizationsApi.updateProject(organization.id, project, {
      name: value.name.trim(), description: value.description.trim() || null,
    }).pipe(finalize(() => this.savingProjectEdit.set(false))).subscribe({
      next: (updated) => {
        if (this.selectedOrganization()?.id === organization.id) {
          this.selectedProject.set(updated);
          this.projects.set(this.projects().map((item) => item.id === updated.id ? updated : item)
            .sort((a, b) => a.name.localeCompare(b.name)));
        }
        this.editingProject.set(false);
        this.notice.set(`${updated.key} was updated.`);
      },
      error: (response: HttpErrorResponse) => this.error.set(this.describeError(response, 'Project could not be updated.')),
    });
  }

  isLastActiveAdmin(member: OrganizationMember): boolean {
    return member.isActive && member.role === 'Admin'
      && this.members().filter((candidate) => candidate.isActive && candidate.role === 'Admin').length === 1;
  }

  private loadMembers(organizationId: string): void {
    this.organizationsApi.members(organizationId).subscribe({
      next: (response) => {
        if (this.selectedOrganization()?.id === organizationId) this.members.set(response.items);
      },
      error: () => {
        if (this.selectedOrganization()?.id === organizationId) this.error.set('Members could not be reloaded.');
      },
    });
  }

  private loadProjects(organizationId: string): void {
    this.organizationsApi.projects(organizationId).subscribe({
      next: (response) => {
        if (this.selectedOrganization()?.id === organizationId) this.projects.set(response.items);
      },
      error: () => {
        if (this.selectedOrganization()?.id === organizationId) this.error.set('Projects could not be loaded for this organization.');
      },
    });
  }

  private describeError(response: HttpErrorResponse, fallback: string): string {
    if (response.status === 409) return response.error?.message ?? 'This change conflicts with the current organization state.';
    if (response.status === 403) return 'Your organization role does not allow this change.';
    if (response.status === 404) return 'The account or organization could not be found.';
    if (response.status === 0 || response.status >= 500) return 'The server could not complete the request. Try again shortly.';
    return response.error?.message ?? fallback;
  }
}
