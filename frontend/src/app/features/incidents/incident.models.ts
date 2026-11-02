export const INCIDENT_SEVERITIES = ['Low', 'Medium', 'High', 'Critical'] as const;
export const INCIDENT_STATUSES = [
  'Open',
  'Investigating',
  'Mitigated',
  'Resolved',
  'Closed',
] as const;
export type IncidentStatus = (typeof INCIDENT_STATUSES)[number];
export interface Incident {
  id: string;
  number: string;
  version: string;
  projectId: string;
  title: string;
  description: string | null;
  affectedService: string;
  severity: string;
  status: IncidentStatus;
  reporterUserId: string;
  reporterName: string;
  ownerUserId: string | null;
  ownerName: string | null;
  startedAtUtc: string;
  updatedAtUtc: string;
  endedAtUtc: string | null;
  resolvedByUserId: string | null;
  resolution: string | null;
  rootCause: string | null;
}
export interface IncidentInput {
  title: string;
  description: string | null;
  affectedService: string;
  severity: string;
}
export interface IncidentComment {
  id: string;
  authorUserId: string;
  authorName: string;
  body: string;
  createdAtUtc: string;
}
export interface IncidentEvent {
  id: string;
  actorUserId: string | null;
  actorName: string;
  action: string;
  detailsJson: string | null;
  occurredAtUtc: string;
}
export interface IncidentSummary {
  activeCount: number;
  criticalCount: number;
  resolvedCount: number;
  atRiskCount: number;
  breachedCount: number;
}
export interface IncidentSla {
  id: string;
  policyId: string;
  policyName: string;
  deadlineAtUtc: string;
  warningAtUtc: string | null;
  warnedAtUtc: string | null;
  breachedAtUtc: string | null;
  completedAtUtc: string | null;
  status: string;
  remainingSeconds: number;
}
export interface IncidentSlaState {
  sla: IncidentSla | null;
  serverTimeUtc: string;
}
