export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
  ) {
    super(code);
  }
}

export interface User {
  id: string;
  email: string;
  displayName: string;
}

export interface TenantMembership {
  tenantId: string;
  name: string;
  slug: string;
  region: string;
  role: "Admin" | "ControlOwner" | "Employee" | "Auditor";
  exampleData: boolean;
}

export interface AuthResponse {
  ok: boolean;
  errorCode?: string | null;
  user?: User | null;
  tenants?: TenantMembership[] | null;
  mfaRequired: boolean;
  pendingAuthenticationToken?: string | null;
  challengeId?: string | null;
  factors?: { id: string; type: string }[] | null;
}

export interface AuthMode {
  mode: "local" | "workos";
  localBanner: boolean;
  exampleEmails: string[];
}

export interface Workspace {
  tenantId: string;
  name: string;
  region: string;
  role: TenantMembership["role"];
  exampleData: boolean;
  canWrite: boolean;
  displayName: string;
  email: string;
  failingControls: number;
}

export interface Dashboard {
  displayName: string;
  tenantName: string;
  region: string;
  exampleData: boolean;
  readinessPercent: number;
  frameworkCount: number;
  frameworks: { key: string; name: string; passing: number; total: number; percent: number }[];
  audit: { name: string; firm: string; frameworkKey: string; fieldworkOn: string; openItems: number; samplesDue: number } | null;
  failingTests: { testId: string; testName: string; controlCode: string; detail?: string | null; requirementCodes: string[] }[];
  health: { passing: number; failing: number; dueSoon: number; needsEvidence: number; manual: number; notStarted: number };
}

export interface FrameworkListItem {
  key: string;
  name: string;
  version: string;
  description: string;
  requirementCount: number;
  mappedControlCount: number;
  passingControlCount: number;
  percent: number;
}

export interface RequirementNode {
  code: string;
  title: string;
  summary: string;
  controls: { code: string; title: string; coverage: string; status: string }[];
  children: RequirementNode[];
}

export interface FrameworkDetail {
  key: string;
  name: string;
  version: string;
  description: string;
  requirements: RequirementNode[];
}

export interface ControlListItem {
  code: string;
  title: string;
  status: string;
  ownerName?: string | null;
  ownerEmail?: string | null;
  frequency: string;
  testsPassing: number;
  testsTotal: number;
  frameworks: string[];
}

export interface ControlDetail {
  code: string;
  title: string;
  description: string;
  status: string;
  ownerName?: string | null;
  ownerEmail?: string | null;
  frequency: string;
  mappings: { frameworkKey: string; frameworkName: string; requirementCode: string; requirementTitle: string; coverage: string }[];
  tests: { id: string; name: string; kind: string; outcome: string; detail?: string | null }[];
  evidence: { id: string; title: string; source: string; collectedAt?: string | null; integrity: string }[];
}

export interface Job {
  id: string;
  kind: string;
  status: string;
  step: number;
  totalSteps: number;
  currentStep: string;
  percent?: number | null;
  error?: string | null;
  updatedAt: string;
}

export interface ActivityEvent {
  at: string;
  action: string;
  result: string;
  detail?: string | null;
  actorEmail?: string | null;
  ipAddress?: string | null;
}

let refreshInFlight: Promise<boolean> | null = null;

async function refreshSession(): Promise<boolean> {
  refreshInFlight ??= fetch("/api/v1/auth/refresh", {
    method: "POST",
    credentials: "include",
    headers: { "X-D2D-Request": "1" },
  })
    .then((response) => response.ok)
    .finally(() => {
      refreshInFlight = null;
    });
  return refreshInFlight;
}

export async function api<T>(
  path: string,
  options: { method?: string; body?: unknown; tenantId?: string | null; skipRefresh?: boolean } = {},
): Promise<T> {
  const headers: Record<string, string> = { Accept: "application/json", "X-D2D-Request": "1" };
  if (options.body !== undefined) headers["Content-Type"] = "application/json";
  if (options.tenantId) headers["X-Tenant-Id"] = options.tenantId;
  const response = await fetch(path, {
    method: options.method ?? (options.body !== undefined ? "POST" : "GET"),
    headers,
    body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
    credentials: "include",
  });
  if (response.status === 401 && !options.skipRefresh && !path.startsWith("/api/v1/auth/")) {
    const refreshed = await refreshSession();
    if (refreshed) return api<T>(path, { ...options, skipRefresh: true });
  }
  if (!response.ok) {
    let code = response.status === 401 ? "unauthorized" : "request_failed";
    try {
      const problem = (await response.json()) as { code?: string; errorCode?: string; title?: string };
      code = problem.code || problem.errorCode || problem.title || code;
    } catch {
      // The body was not JSON.
    }
    throw new ApiError(response.status, code);
  }
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}
