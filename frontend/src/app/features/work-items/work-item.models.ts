export type WorkItemType = 'Task' | 'Bug';
export type WorkItemPriority = 'Low' | 'Medium' | 'High' | 'Critical';
export type WorkItemStatus = 'New' | 'Triaged' | 'Assigned' | 'InProgress' | 'CodeReview' | 'QA' | 'Resolved' | 'Closed';

export interface WorkItem {
  id: string;
  projectId: string;
  type: WorkItemType;
  title: string;
  description: string | null;
  priority: WorkItemPriority;
  status: WorkItemStatus;
  reporterUserId: string;
  reporterName: string;
  assigneeUserId: string | null;
  assigneeName: string | null;
  dueAtUtc: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  commentCount: number;
}

export interface WorkItemComment {
  id: string;
  authorUserId: string;
  authorName: string;
  body: string;
  createdAtUtc: string;
}

export interface WorkItemHistory {
  id: string;
  actorUserId: string;
  actorName: string;
  action: string;
  detailsJson: string | null;
  occurredAtUtc: string;
}

export interface WorkItemPage<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface WorkItemFilters {
  type?: string;
  status?: string;
  priority?: string;
  assigneeUserId?: string;
  search?: string;
  page: number;
  pageSize: number;
  sortBy: string;
  sortDirection: string;
}

export const WORK_ITEM_TYPES: WorkItemType[] = ['Task', 'Bug'];
export const WORK_ITEM_PRIORITIES: WorkItemPriority[] = ['Low', 'Medium', 'High', 'Critical'];
export const WORK_ITEM_STATUSES: WorkItemStatus[] = ['New', 'Triaged', 'Assigned', 'InProgress', 'CodeReview', 'QA', 'Resolved', 'Closed'];

export const WORK_ITEM_TRANSITIONS: Record<WorkItemStatus, WorkItemStatus[]> = {
  New: ['Triaged'],
  Triaged: ['Assigned'],
  Assigned: ['InProgress'],
  InProgress: ['CodeReview'],
  CodeReview: ['InProgress', 'QA'],
  QA: ['InProgress', 'Resolved'],
  Resolved: ['InProgress', 'Closed'],
  Closed: ['InProgress'],
};
