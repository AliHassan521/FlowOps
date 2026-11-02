export interface Notification {
  id: string;
  organizationId: string;
  projectId: string;
  workItemId: string | null;
  incidentId?: string | null;
  kind: string;
  message: string;
  createdAtUtc: string;
  readAtUtc: string | null;
}

export interface NotificationPage {
  items: Notification[];
  page: number;
  pageSize: number;
  totalCount: number;
}
