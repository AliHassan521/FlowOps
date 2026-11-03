export type ReleaseStatus = 'Draft' | 'Approved' | 'Deploying' | 'Successful' | 'Failed';
export const RELEASE_STATUSES: ReleaseStatus[] = [
  'Draft',
  'Approved',
  'Deploying',
  'Successful',
  'Failed',
];
export interface ReleaseEnvironment {
  id: string;
  name: string;
  isActive: boolean;
  createdAtUtc: string;
}
export interface Release {
  id: string;
  version: string;
  projectId: string;
  applicationName: string;
  releaseVersion: string;
  environmentId: string;
  environmentName: string;
  environmentActive: boolean;
  notes: string | null;
  status: ReleaseStatus;
  createdByUserId: string;
  creatorName: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  approvedByUserId: string | null;
  approvedAtUtc: string | null;
  deploymentStartedAtUtc: string | null;
  completedAtUtc: string | null;
  outcome: string | null;
  workItemCount: number;
  incidentCount: number;
}
export interface ReleaseResource {
  id: string;
  title: string;
  status: string;
}
export interface ReleaseLinks {
  workItems: ReleaseResource[];
  incidents: ReleaseResource[];
}
export interface ReleaseEvent {
  id: string;
  actorUserId: string | null;
  actorName: string;
  action: string;
  occurredAtUtc: string;
}
