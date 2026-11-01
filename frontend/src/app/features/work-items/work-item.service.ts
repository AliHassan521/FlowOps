import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../core/config/api.config';
import { WorkItem, WorkItemComment, WorkItemFilters, WorkItemHistory, WorkItemPage } from './work-item.models';

@Injectable({ providedIn: 'root' })
export class WorkItemService {
  private readonly http = inject(HttpClient);

  list(organizationId: string, projectId: string, filters: WorkItemFilters): Observable<WorkItemPage<WorkItem>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filters)) {
      if (value !== undefined && value !== '') params = params.set(key, String(value));
    }
    return this.http.get<WorkItemPage<WorkItem>>(this.endpoint(organizationId, projectId), { params });
  }

  get(organizationId: string, projectId: string, workItemId: string): Observable<WorkItem> {
    return this.http.get<WorkItem>(`${this.endpoint(organizationId, projectId)}/${workItemId}`);
  }

  create(organizationId: string, projectId: string, input: {
    type: string; title: string; description: string | null; priority: string;
    assigneeUserId: string | null; dueAtUtc: string | null;
  }): Observable<WorkItem> {
    return this.http.post<WorkItem>(this.endpoint(organizationId, projectId), input);
  }

  update(organizationId: string, projectId: string, workItemId: string, input: {
    title: string; description: string | null; priority: string; dueAtUtc: string | null;
  }): Observable<WorkItem> {
    return this.http.put<WorkItem>(`${this.endpoint(organizationId, projectId)}/${workItemId}`, input);
  }

  assign(organizationId: string, projectId: string, workItemId: string, assigneeUserId: string | null): Observable<WorkItem> {
    return this.http.put<WorkItem>(`${this.endpoint(organizationId, projectId)}/${workItemId}/assignee`, { assigneeUserId });
  }

  transition(organizationId: string, projectId: string, workItemId: string, status: string): Observable<WorkItem> {
    return this.http.post<WorkItem>(`${this.endpoint(organizationId, projectId)}/${workItemId}/transitions`, { status });
  }

  comments(organizationId: string, projectId: string, workItemId: string, page = 1): Observable<WorkItemPage<WorkItemComment>> {
    return this.http.get<WorkItemPage<WorkItemComment>>(`${this.endpoint(organizationId, projectId)}/${workItemId}/comments`, { params: new HttpParams().set('page', page).set('pageSize', 50) });
  }

  addComment(organizationId: string, projectId: string, workItemId: string, body: string): Observable<WorkItemComment> {
    return this.http.post<WorkItemComment>(`${this.endpoint(organizationId, projectId)}/${workItemId}/comments`, { body });
  }

  history(organizationId: string, projectId: string, workItemId: string, page = 1): Observable<WorkItemPage<WorkItemHistory>> {
    return this.http.get<WorkItemPage<WorkItemHistory>>(`${this.endpoint(organizationId, projectId)}/${workItemId}/history`, { params: new HttpParams().set('page', page).set('pageSize', 50) });
  }

  private endpoint(organizationId: string, projectId: string): string {
    return `${API_BASE_URL}/organizations/${organizationId}/projects/${projectId}/work-items`;
  }
}
