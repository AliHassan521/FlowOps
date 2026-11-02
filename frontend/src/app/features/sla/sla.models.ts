export interface SlaPolicy {
  id: string; name: string; durationMinutes: number; warningBeforeMinutes: number;
  isActive: boolean; createdAtUtc: string;
}
export interface SlaPolicyPage { items: SlaPolicy[]; page: number; pageSize: number; totalCount: number; }
export interface WorkItemSla {
  id: string; policyId: string; policyName: string; durationMinutes: number; warningBeforeMinutes: number;
  startedAtUtc: string; deadlineAtUtc: string; warningAtUtc: string | null; warnedAtUtc: string | null;
  breachedAtUtc: string | null; completedAtUtc: string | null;
  status: 'Active' | 'AtRisk' | 'Overdue' | 'Breached' | 'Met'; remainingSeconds: number;
}
export interface SlaState { sla: WorkItemSla | null; serverTimeUtc: string; }
export interface SlaSummary { activeCount: number; atRiskCount: number; breachedCount: number; metCount: number; }
