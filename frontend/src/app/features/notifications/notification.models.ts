export interface Notification {
  id: string;
  organizationId: string;
  projectId: string;
  workItemId: string;
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
