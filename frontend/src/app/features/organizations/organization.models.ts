export interface Organization {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  currentUserRole: string;
  createdAtUtc: string;
  updatedAtUtc: string | null;
}

export interface OrganizationMember {
  userId: string;
  email: string;
  fullName: string;
  role: string;
  joinedAtUtc: string;
  isActive: boolean;
}

export interface Project {
  id: string;
  organizationId: string;
  name: string;
  key: string;
  description: string | null;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
}

export interface PagedResponse<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export const ORGANIZATION_ROLES = ['Admin', 'TeamLead', 'Engineer', 'QA', 'Viewer'] as const;
