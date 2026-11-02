import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../core/config/api.config';
import { SlaPolicy, SlaPolicyPage, SlaState, SlaSummary } from './sla.models';

@Injectable({ providedIn: 'root' })
export class SlaService {
  private readonly http = inject(HttpClient);
  policies(organizationId: string, page = 1, activeOnly = true): Observable<SlaPolicyPage> {
    return this.http.get<SlaPolicyPage>(`${this.organization(organizationId)}/sla-policies`,
      { params: new HttpParams().set('page', page).set('pageSize', 50).set('activeOnly', activeOnly) });
  }
  createPolicy(organizationId: string, input: { name: string; durationMinutes: number; warningBeforeMinutes: number }): Observable<SlaPolicy> {
    return this.http.post<SlaPolicy>(`${this.organization(organizationId)}/sla-policies`, input);
  }
  archivePolicy(organizationId: string, policyId: string): Observable<void> {
    return this.http.delete<void>(`${this.organization(organizationId)}/sla-policies/${policyId}`);
  }
  summary(organizationId: string): Observable<SlaSummary> {
    return this.http.get<SlaSummary>(`${this.organization(organizationId)}/sla-summary`);
  }
  state(organizationId: string, projectId: string, itemId: string): Observable<SlaState> {
    return this.http.get<SlaState>(this.item(organizationId, projectId, itemId));
  }
  attach(organizationId: string, projectId: string, itemId: string, policyId: string): Observable<SlaState> {
    return this.http.post<SlaState>(this.item(organizationId, projectId, itemId), { policyId });
  }
  private organization(id: string): string { return `${API_BASE_URL}/organizations/${id}`; }
  private item(orgId: string, projectId: string, itemId: string): string {
    return `${this.organization(orgId)}/projects/${projectId}/work-items/${itemId}/sla`;
  }
}
