import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../core/config/api.config';
import { PagedResponse } from '../organizations/organization.models';
import {
  Incident,
  IncidentComment,
  IncidentEvent,
  IncidentInput,
  IncidentSlaState,
  IncidentSummary,
} from './incident.models';
@Injectable({ providedIn: 'root' })
export class IncidentService {
  private readonly http = inject(HttpClient);
  list(
    org: string,
    project: string,
    filters: { page: number; severity: string; status: string; search: string },
  ): Observable<PagedResponse<Incident>> {
    let params = new HttpParams().set('pageSize', 20);
    for (const [key, value] of Object.entries(filters))
      if (value !== '') params = params.set(key, value);
    return this.http.get<PagedResponse<Incident>>(this.path(org, project), { params });
  }
  get(org: string, project: string, id: string): Observable<Incident> {
    return this.http.get<Incident>(`${this.path(org, project)}/${id}`);
  }
  create(
    org: string,
    project: string,
    input: IncidentInput & { ownerUserId: string | null },
  ): Observable<Incident> {
    return this.http.post<Incident>(this.path(org, project), input);
  }
  update(org: string, project: string, item: Incident, input: IncidentInput): Observable<Incident> {
    return this.http.put<Incident>(`${this.path(org, project)}/${item.id}`, {
      ...input,
      expectedVersion: item.version,
    });
  }
  assign(
    org: string,
    project: string,
    item: Incident,
    ownerUserId: string | null,
  ): Observable<Incident> {
    return this.http.put<Incident>(`${this.path(org, project)}/${item.id}/owner`, {
      expectedVersion: item.version,
      ownerUserId,
    });
  }
  transition(
    org: string,
    project: string,
    item: Incident,
    status: string,
    resolution: string | null,
    rootCause: string | null,
  ): Observable<Incident> {
    return this.http.post<Incident>(`${this.path(org, project)}/${item.id}/transitions`, {
      expectedVersion: item.version,
      status,
      resolution,
      rootCause,
    });
  }
  comments(
    org: string,
    project: string,
    id: string,
    page = 1,
  ): Observable<PagedResponse<IncidentComment>> {
    return this.http.get<PagedResponse<IncidentComment>>(
      `${this.path(org, project)}/${id}/comments`,
      { params: { page, pageSize: 50 } },
    );
  }
  comment(org: string, project: string, id: string, body: string): Observable<IncidentComment> {
    return this.http.post<IncidentComment>(`${this.path(org, project)}/${id}/comments`, { body });
  }
  timeline(
    org: string,
    project: string,
    id: string,
    page = 1,
  ): Observable<PagedResponse<IncidentEvent>> {
    return this.http.get<PagedResponse<IncidentEvent>>(
      `${this.path(org, project)}/${id}/timeline`,
      { params: { page, pageSize: 50 } },
    );
  }
  sla(org: string, project: string, id: string): Observable<IncidentSlaState> {
    return this.http.get<IncidentSlaState>(`${this.path(org, project)}/${id}/sla`);
  }
  attach(
    org: string,
    project: string,
    item: Incident,
    policyId: string,
  ): Observable<IncidentSlaState> {
    return this.http.post<IncidentSlaState>(`${this.path(org, project)}/${item.id}/sla`, {
      expectedVersion: item.version,
      policyId,
    });
  }
  summary(org: string, project?: string): Observable<IncidentSummary> {
    return this.http.get<IncidentSummary>(
      project
        ? `${this.path(org, project)}/summary`
        : `${API_BASE_URL}/organizations/${org}/incident-summary`,
    );
  }
  private path(org: string, project: string): string {
    return `${API_BASE_URL}/organizations/${org}/projects/${project}/incidents`;
  }
}
