import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../core/config/api.config';
import { Organization, OrganizationMember, PagedResponse, Project } from './organization.models';

@Injectable({ providedIn: 'root' })
export class OrganizationService {
  private readonly http = inject(HttpClient);
  private readonly endpoint = `${API_BASE_URL}/organizations`;

  list(): Observable<PagedResponse<Organization>> {
    return this.http.get<PagedResponse<Organization>>(this.endpoint);
  }

  get(organizationId: string): Observable<Organization> {
    return this.http.get<Organization>(`${this.endpoint}/${organizationId}`);
  }

  create(input: { name: string; slug: string; description: string | null }): Observable<Organization> {
    return this.http.post<Organization>(this.endpoint, input);
  }

  update(organizationId: string, input: { name: string; description: string | null }): Observable<Organization> {
    return this.http.put<Organization>(`${this.endpoint}/${organizationId}`, input);
  }

  deactivate(organizationId: string): Observable<void> {
    return this.http.delete<void>(`${this.endpoint}/${organizationId}`);
  }

  members(organizationId: string): Observable<PagedResponse<OrganizationMember>> {
    return this.http.get<PagedResponse<OrganizationMember>>(`${this.endpoint}/${organizationId}/members`);
  }

  addMember(organizationId: string, email: string, role: string): Observable<OrganizationMember> {
    return this.http.post<OrganizationMember>(`${this.endpoint}/${organizationId}/members`, { email, role });
  }

  updateRole(organizationId: string, member: OrganizationMember, role: string): Observable<OrganizationMember> {
    return this.http.put<OrganizationMember>(
      `${this.endpoint}/${organizationId}/members/${member.userId}/role`, { role });
  }

  deactivateMember(organizationId: string, member: OrganizationMember): Observable<void> {
    return this.http.delete<void>(`${this.endpoint}/${organizationId}/members/${member.userId}`);
  }

  projects(organizationId: string): Observable<PagedResponse<Project>> {
    return this.http.get<PagedResponse<Project>>(`${this.endpoint}/${organizationId}/projects`);
  }

  createProject(organizationId: string, input: { name: string; key: string; description: string | null }): Observable<Project> {
    return this.http.post<Project>(`${this.endpoint}/${organizationId}/projects`, input);
  }

  project(organizationId: string, projectId: string): Observable<Project> {
    return this.http.get<Project>(`${this.endpoint}/${organizationId}/projects/${projectId}`);
  }

  updateProject(organizationId: string, project: Project, input: { name: string; description: string | null }): Observable<Project> {
    return this.http.put<Project>(`${this.endpoint}/${organizationId}/projects/${project.id}`, input);
  }

  deactivateProject(organizationId: string, project: Project): Observable<void> {
    return this.http.delete<void>(`${this.endpoint}/${organizationId}/projects/${project.id}`);
  }
}
