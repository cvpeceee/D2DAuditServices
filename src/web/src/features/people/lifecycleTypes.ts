export type LifecycleSettings = {
  offboardingAccessSlaHours: number;
  offboardingCompleteSlaHours: number;
  onboardingDays: number;
};

export type EmployeeGroup = {
  code: string;
  name: string;
  description: string;
  departments: string[];
  roleKeywords: string[];
  policyCodes: string[];
  trainingCodes: string[];
  memberCount: number;
};

export type LifecycleTemplateStep = {
  id: string;
  kind: string;
  code: string;
  title: string;
  category: string;
  slaHours: number;
  order: number;
  groupCode: string;
  revokesAccess: boolean;
  perAccount: boolean;
};

export type LifecycleStep = {
  id: string;
  code: string;
  title: string;
  category: string;
  provider: string;
  accountName: string;
  revokesAccess: boolean;
  dueAt: string;
  done: boolean;
  completedAt: string | null;
  completedBy: string;
  result: string;
  evidenceId: string | null;
  sla: string;
};

export type LifecycleChecklist = {
  id: string;
  personId: string;
  personName: string;
  personEmail: string;
  kind: string;
  trigger: string;
  reason: string;
  effectiveOn: string;
  triggeredAt: string;
  dueAt: string;
  accessDueAt: string | null;
  accessRevokedAt: string | null;
  completedAt: string | null;
  state: string;
  sla: string;
  accessSla: string;
  doneCount: number;
  totalCount: number;
  groups: string[];
  steps: LifecycleStep[];
};

export type LifecycleTest = { name: string; controlCode: string; outcome: string; detail: string };

export type LifecycleOverview = {
  settings: LifecycleSettings;
  groups: EmployeeGroup[];
  templates: LifecycleTemplateStep[];
  checklists: LifecycleChecklist[];
  tests: LifecycleTest[];
  revokerMode: string;
};

export type PersonLifecycle = {
  personId: string;
  name: string;
  email: string;
  state: string;
  hireOn: string | null;
  terminationOn: string | null;
  groups: string[];
  requiredPolicies: string[];
  requiredTraining: string[];
  checklists: LifecycleChecklist[];
};

export type LifecycleSyncResult = {
  onboardingStarted: number;
  offboardingStarted: number;
  tests: LifecycleTest[];
};

export type RevokeAllResult = { revoked: number; failed: number; checklist: LifecycleChecklist };

export const selfServiceCategories = ["Policy", "Training", "Device", "Identity"];

export function slaTone(value: string) {
  if (value === "Missed" || value === "Overdue" || value === "Fail") return "fail";
  if (value === "DueSoon") return "warn";
  if (value === "Met" || value === "Pass" || value === "Complete") return "pass";
  return "info";
}
