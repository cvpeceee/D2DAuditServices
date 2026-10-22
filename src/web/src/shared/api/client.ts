import type { components } from "./schema";

export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
  ) {
    super(code);
  }
}

type Schemas = components["schemas"];

/**
 * OpenAPI 3.1 emits int32 as `number | string`. The API sends JSON numbers.
 * A plain string (email, status, id) is left alone.
 */
type JsonInt<T> = number extends T
  ? string extends T
    ? null extends T
      ? number | null
      : number
    : T
  : T;

type Normalize<T> = T extends (infer U)[]
  ? Normalize<U>[]
  : T extends object
    ? { [K in keyof T]: Normalize<JsonInt<T[K]>> }
    : T;

type Model<K extends keyof Schemas> = Normalize<Schemas[K]>;

export type User = Model<"UserSummary">;
export type TenantMembership = Model<"TenantMembershipSummary">;
export type AuthResponse = Model<"AuthBody">;
export type AuthMode = Model<"AuthModeResponse">;
export type Workspace = Model<"WorkspaceResponse">;
export type Dashboard = Model<"DashboardResponse">;
export type FrameworkListItem = Model<"FrameworkListItem">;
export type RequirementNode = Model<"RequirementNodeDto">;
export type FrameworkDetail = Model<"FrameworkDetail">;
export type ControlListItem = Model<"ControlListItem">;
export type ControlDetail = Model<"ControlDetail">;
export type Job = Model<"JobDto">;
export type ActivityEvent = Model<"ActivityDto">;
export type Integration = Model<"IntegrationDto">;
export type ConnectResult = Model<"ConnectResponse">;
export type AutomatedTest = Model<"AutomatedTestDto">;
export type EvidenceItem = Model<"EvidenceListItem">;
export type EvidencePage = Model<"EvidencePage">;
export type VerifyReport = Model<"VerifyReport">;
export type PolicyListItem = Model<"PolicyListItem">;
export type PolicyDetail = Model<"PolicyDetail">;
export type AuditPortalPage = Model<"AuditPortalPage">;
export type RiskListItem = Model<"RiskListItem">;
export type PersonListItem = Model<"PersonListItem">;
export type Notification = Model<"NotificationDto">;
export type Device = Model<"DeviceDto">;
export type Resource = Model<"ResourceDto">;
export type PersonDetail = Model<"PersonDetail">;
export type SoaRow = Model<"SoaRow">;
export type VendorListItem = Model<"VendorListItem">;
export type AccessCampaignListItem = Model<"AccessCampaignListItem">;
export type AccessCampaignDetail = Model<"AccessCampaignDetail">;

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
  options: {
    method?: string;
    body?: unknown;
    tenantId?: string | null;
    skipRefresh?: boolean;
  } = {},
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
      const problem = (await response.json()) as {
        code?: string;
        errorCode?: string;
        title?: string;
      };
      code = problem.code || problem.errorCode || problem.title || code;
    } catch {
      // The body was not JSON.
    }
    throw new ApiError(response.status, code);
  }
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export async function download(path: string, tenantId: string | null) {
  const headers: Record<string, string> = { "X-D2D-Request": "1" };
  if (tenantId) headers["X-Tenant-Id"] = tenantId;
  const response = await fetch(path, { headers, credentials: "include" });
  if (!response.ok) throw new ApiError(response.status, "request_failed");
  const blob = await response.blob();
  const match = /filename="?([^";]+)"?/.exec(response.headers.get("Content-Disposition") ?? "");
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = match?.[1] ?? "download";
  link.click();
  URL.revokeObjectURL(url);
}
