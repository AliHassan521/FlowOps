import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../core/config/api.config';
import { PagedResponse } from '../organizations/organization.models';
import { Release, ReleaseEnvironment, ReleaseLinks, ReleaseEvent } from './release.models';
@Injectable({ providedIn: 'root' })
export class ReleaseService {
  private readonly http = inject(HttpClient);
  list(
    org: string,
    project: string,
    filters: { page: number; status: string; search: string; environmentId: string },
  ): Observable<PagedResponse<Release>> {
    let params = new HttpParams().set('pageSize', 20);
    for (const [key, value] of Object.entries(filters))
      if (value !== '') params = params.set(key, value);
    return this.http.get<PagedResponse<Release>>(this.path(org, project), { params });
  }
  get(org: string, project: string, id: string): Observable<Release> {
    return this.http.get<Release>(`${this.path(org, project)}/${id}`);
  }
  create(
    org: string,
    project: string,
    input: {
      applicationName: string;
      releaseVersion: string;
      environmentId: string;
      notes: string | null;
    },
  ): Observable<Release> {
    return this.http.post<Release>(this.path(org, project), input);
  }
  update(org: string, project: string, item: Release, notes: string | null): Observable<Release> {
    return this.http.put<Release>(`${this.path(org, project)}/${item.id}`, {
      expectedVersion: item.version,
      notes,
    });
  }
  links(org: string, project: string, id: string): Observable<ReleaseLinks> {
    return this.http.get<ReleaseLinks>(`${this.path(org, project)}/${id}/links`);
  }
  setLinks(
    org: string,
    project: string,
    item: Release,
    workItemIds: string[],
    incidentIds: string[],
  ): Observable<Release> {
    return this.http.put<Release>(`${this.path(org, project)}/${item.id}/links`, {
      expectedVersion: item.version,
      workItemIds,
      incidentIds,
    });
  }
  transition(
    org: string,
    project: string,
    item: Release,
    status: string,
    outcome: string | null,
  ): Observable<Release> {
    return this.http.post<Release>(`${this.path(org, project)}/${item.id}/transitions`, {
      expectedVersion: item.version,
      status,
      outcome,
    });
  }
  timeline(
    org: string,
    project: string,
    id: string,
    page = 1,
  ): Observable<PagedResponse<ReleaseEvent>> {
    return this.http.get<PagedResponse<ReleaseEvent>>(`${this.path(org, project)}/${id}/timeline`, {
      params: { page, pageSize: 50 },
    });
  }
  environments(
    org: string,
    project: string,
    page = 1,
  ): Observable<PagedResponse<ReleaseEnvironment>> {
    return this.http.get<PagedResponse<ReleaseEnvironment>>(
      `${this.path(org, project)}/environments`,
      { params: { page, pageSize: 50, activeOnly: false } },
    );
  }
  createEnvironment(org: string, project: string, name: string): Observable<ReleaseEnvironment> {
    return this.http.post<ReleaseEnvironment>(`${this.path(org, project)}/environments`, { name });
  }
  archiveEnvironment(org: string, project: string, id: string): Observable<void> {
    return this.http.delete<void>(`${this.path(org, project)}/environments/${id}`);
  }
  private path(org: string, project: string) {
    return `${API_BASE_URL}/organizations/${org}/projects/${project}/releases`;
  }
}
