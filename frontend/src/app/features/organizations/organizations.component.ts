import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ORGANIZATION_ROLES, Organization, OrganizationMember, Project } from './organization.models';
import { OrganizationService } from './organization.service';

@Component({
  selector: 'app-organizations',
  standalone: true,
  imports: [DatePipe, ReactiveFormsModule, RouterLink],
  templateUrl: './organizations.component.html',
  styleUrl: './organizations.component.css',
})
export class OrganizationsComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly organizationsApi = inject(OrganizationService);
  readonly authService = inject(AuthService);
  readonly organizations = signal<Organization[]>([]);
  readonly members = signal<OrganizationMember[]>([]);
  readonly projects = signal<Project[]>([]);
  readonly selectedProject = signal<Project | null>(null);
  readonly selectedOrganization = signal<Organization | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly savingProject = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly roles = ORGANIZATION_ROLES;

  readonly organizationForm = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(120)]],
    slug: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(80), Validators.pattern(/^[a-z0-9]+(?:-[a-z0-9]+)*$/)]],
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
    if (!organization || !this.canManage || !member.isActive) return;

    this.error.set('');
    this.organizationsApi.deactivateMember(organization.id, member).subscribe({
      next: () => {
        if (this.selectedOrganization()?.id === organization.id) {
          this.members.set(this.members().map((item) => item.userId === member.userId ? { ...item, isActive: false } : item));
        }
        this.notice.set(`${member.fullName} was deactivated in this organization.`);
      },
      error: (response: HttpErrorResponse) => this.error.set(this.describeError(response, 'Member could not be deactivated.')),
    });
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

    this.organizationsApi.deactivateProject(organization.id, project).subscribe({
      next: () => {
        if (this.selectedOrganization()?.id === organization.id) {
          this.projects.set(this.projects().filter((item) => item.id !== project.id));
        }
        this.notice.set(`${project.key} was archived.`);
      },
      error: (response: HttpErrorResponse) => this.error.set(this.describeError(response, 'Project could not be archived.')),
    });
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
