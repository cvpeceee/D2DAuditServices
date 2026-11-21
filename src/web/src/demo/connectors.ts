import connectorsSeed from "../../../seed/connectors.json";
import type {
  ConnectorActionResponse,
  ConnectorCatalog,
  ConnectorCheck,
  ConnectorConfigValue,
  ConnectorDetail,
  ConnectorField,
  ConnectorRun,
  ConnectorSummary,
} from "../shared/api/client";
import type { DemoWorld } from "./catalog";

type Actor = { email: string; name: string; role: string };

type SeedResource = {
  provider: string;
  kind: string;
  externalId: string;
  name: string;
  attributes: Record<string, string>;
};

type SeedCheck = {
  key: string;
  name: string;
  controlCode: string;
  requirements: string[];
  remediation: string;
};

type SeedConnector = {
  key: string;
  name: string;
  category: string;
  authType: string;
  description: string;
  fields: ConnectorField[];
  resources: string[];
  tests: string[];
  controls: string[];
  docsUrl: string;
  builtin: boolean;
  managedAt: string | null;
  envKeys: string[];
  harborline: boolean;
  fixtures: SeedResource[];
};

type SeedDocument = {
  categories: string[];
  checks: SeedCheck[];
  connectors: SeedConnector[];
};

const catalogData = connectorsSeed as unknown as SeedDocument;
const connectorByKey = new Map<string, SeedConnector>(
  catalogData.connectors.map((item) => [item.key, item]),
);
const rowBuiltins = new Set(["aws", "azure", "gcp", "google", "microsoft", "github"]);

const builtinCheckMeta: Record<string, { name: string; controlCode: string }> = {
  "mfa-all-users": { name: "MFA enforced for all users", controlCode: "AC-07" },
  "root-mfa": { name: "Root account MFA", controlCode: "AC-07" },
  "s3-public-access": { name: "S3 public access blocked", controlCode: "CFG-01" },
  "cloudtrail-enabled": { name: "CloudTrail enabled", controlCode: "LG-03" },
  "encryption-at-rest": { name: "Encryption at rest", controlCode: "CR-02" },
  "branch-protection": { name: "Branch protection on the default branch", controlCode: "CM-11" },
  "offboarded-removed": { name: "Offboarded users removed", controlCode: "HR-08" },
  "azure-storage-public": { name: "Azure storage public access blocked", controlCode: "CFG-01" },
  "azure-key-vault": { name: "Azure Key Vault is in use", controlCode: "CR-02" },
  "azure-activity-log": { name: "Azure activity log alerts are on", controlCode: "LG-03" },
  "azure-network-rules": { name: "Azure network rules are not wide open", controlCode: "CFG-01" },
  "entra-mfa": { name: "Entra ID MFA for active users", controlCode: "AC-07" },
  "gcp-bucket-public": { name: "GCP bucket public access blocked", controlCode: "CFG-01" },
  "gcp-kms": { name: "GCP KMS keys are present", controlCode: "CR-02" },
  "gcp-audit-log": { name: "GCP audit logs are exported", controlCode: "LG-03" },
  "gcp-iam-public": { name: "GCP IAM is not public", controlCode: "AC-07" },
};

const checkByKey = new Map<string, { name: string; controlCode: string }>([
  ...Object.entries(builtinCheckMeta),
  ...catalogData.checks.map(
    (item) => [item.key, { name: item.name, controlCode: item.controlCode }] as const,
  ),
]);

type ConnectorConnectionState = {
  status: string;
  mode: string;
  config: Record<string, string>;
  lastSyncedAt: string | null;
  lastError: string | null;
  resources: SeedResource[];
};

type DemoConnectorState = {
  connections: Map<string, ConnectorConnectionState>;
  history: Map<string, ConnectorRun[]>;
  runSeq: number;
};

const stateByWorld = new WeakMap<DemoWorld, DemoConnectorState>();

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function canWrite(actor: Actor) {
  return actor.role !== "Auditor";
}

function formatAttributes(attributes: Record<string, string>) {
  const priority = [
    "email",
    "status",
    "mfa",
    "healthy",
    "encrypted",
    "public",
    "protected",
    "withinSla",
    "severity",
    "completed",
    "cleared",
    "approved",
    "covered",
    "triaged",
    "muted",
    "retentionDays",
    "tls",
    "backups",
    "open",
    "enabled",
    "offboarded",
  ];
  const parts: string[] = [];
  for (const key of priority) {
    const value = attributes[key];
    if (value) parts.push(`${key} ${value}`);
  }
  if (parts.length === 0) {
    for (const [key, value] of Object.entries(attributes).slice(0, 4)) {
      if (value) parts.push(`${key} ${value}`);
    }
  }
  return parts.join(", ");
}

function syncWorldResources(world: DemoWorld, provider: string, fixtures: SeedResource[]) {
  world.resources = world.resources.filter((item) => item.provider !== provider);
  for (const item of fixtures) {
    world.resources.push({
      provider: item.provider,
      kind: item.kind,
      name: item.name,
      detail: formatAttributes(item.attributes),
    });
  }
}

function nextRunId(state: DemoConnectorState) {
  state.runSeq += 1;
  return `00000000-0000-4000-8000-${String(500000 + state.runSeq).padStart(12, "0")}`;
}

function getState(world: DemoWorld): DemoConnectorState {
  let state = stateByWorld.get(world);
  if (!state) {
    const now = new Date().toISOString();
    state = {
      connections: new Map(),
      history: new Map(),
      runSeq: 0,
    };
    for (const descriptor of catalogData.connectors) {
      if (!descriptor.builtin && descriptor.harborline) {
        const fixtures = descriptor.fixtures.map((item) => ({
          ...item,
          attributes: { ...item.attributes },
        }));
        state.connections.set(descriptor.key, {
          status: "connected",
          mode: "mock",
          config: {},
          lastSyncedAt: now,
          lastError: null,
          resources: fixtures,
        });
        state.history.set(descriptor.key, [
          {
            id: nextRunId(state),
            kind: "sync",
            mode: "mock",
            status: "ok",
            resourceCount: fixtures.length,
            error: null,
            startedAt: now,
            finishedAt: now,
            trigger: "schedule",
          },
        ]);
        syncWorldResources(world, descriptor.key, fixtures);
      }
    }
    stateByWorld.set(world, state);
  }
  return state;
}

function mergeConfig(
  descriptor: SeedConnector,
  stored: Record<string, string>,
  submitted: Record<string, unknown> | undefined,
) {
  const merged: Record<string, string> = {};
  for (const field of descriptor.fields) {
    const raw = submitted?.[field.key];
    const submittedText = typeof raw === "string" ? raw.trim() : "";
    const existingText = stored[field.key]?.trim() ?? "";
    const value = submittedText || existingText;
    if (value) {
      merged[field.key] = value.length > 1024 ? value.slice(0, 1024) : value;
    }
  }
  return merged;
}

function redactConfig(
  descriptor: SeedConnector,
  config: Record<string, string>,
): ConnectorConfigValue[] {
  return descriptor.fields.map((field) => {
    const raw = config[field.key];
    const configured = Boolean(raw && raw.trim().length > 0);
    return {
      key: field.key,
      value: field.secret || !configured ? null : raw,
      configured,
    };
  });
}

function activeCatalogResources(state: DemoConnectorState): SeedResource[] {
  const all: SeedResource[] = [];
  for (const connection of state.connections.values()) {
    if (connection.status === "connected") {
      all.push(...connection.resources);
    }
  }
  return all;
}

function flag(resource: SeedResource, key: string) {
  return resource.attributes[key] ?? "";
}

function emailOf(resource: SeedResource) {
  return flag(resource, "email").toLowerCase();
}

function evaluateMatching(
  resources: SeedResource[],
  kind: string,
  include: (item: SeedResource) => boolean,
  passes: (item: SeedResource) => boolean,
  empty: string,
  failure: string,
) {
  const rows = resources.filter((item) => item.kind === kind && include(item));
  if (rows.length === 0) return { outcome: "notRun", detail: empty };
  const bad = [...new Set(rows.filter((item) => !passes(item)).map((item) => item.name))];
  return bad.length === 0
    ? { outcome: "pass", detail: `${rows.length} ${kind} resources match.` }
    : { outcome: "fail", detail: `${failure}: ${bad.join(", ")}.` };
}

function evaluateAllOf(
  resources: SeedResource[],
  kind: string,
  attribute: string,
  expected: string,
  empty: string,
  failure: string,
) {
  return evaluateMatching(
    resources,
    kind,
    () => true,
    (item) => flag(item, attribute) === expected,
    empty,
    failure,
  );
}

function evaluateConnectorCheck(
  key: string,
  world: DemoWorld,
  resources: SeedResource[],
): { outcome: string; detail: string } {
  switch (key) {
    case "endpoint-edr":
      return evaluateAllOf(
        resources,
        "edrAgent",
        "healthy",
        "true",
        "No EDR agents were collected.",
        "These endpoints have a degraded or missing sensor",
      );
    case "endpoint-disk-encryption":
      return evaluateAllOf(
        resources,
        "device",
        "encrypted",
        "true",
        "No managed devices were collected.",
        "Disk encryption is off on",
      );
    case "vulnerability-sla":
      return evaluateMatching(
        resources,
        "vulnerability",
        (item) => {
          const status = flag(item, "status");
          return status === "open" || status === "";
        },
        (item) => flag(item, "withinSla") === "true",
        "No open vulnerabilities were collected.",
        "Open findings are past their SLA",
      );
    case "hris-leavers-deprovisioned": {
      const employees = resources.filter((item) => item.kind === "employee");
      if (employees.length === 0) {
        return { outcome: "notRun", detail: "No HRIS employees were collected." };
      }
      const terminated = new Set(
        employees
          .filter((item) => flag(item, "status") === "terminated")
          .map(emailOf)
          .filter((email) => email.length > 0),
      );
      if (terminated.size === 0) {
        return { outcome: "pass", detail: "The HRIS has no terminated employees." };
      }
      const active = [
        ...new Set(
          resources
            .filter(
              (item) =>
                (item.kind === "user" || item.kind === "vaultMember") &&
                flag(item, "status") !== "suspended" &&
                flag(item, "offboarded") !== "true" &&
                terminated.has(emailOf(item)),
            )
            .map((item) => `${item.name} (${item.provider})`),
        ),
      ];
      return active.length === 0
        ? {
            outcome: "pass",
            detail: `${terminated.size} terminated employees have no active accounts.`,
          }
        : {
            outcome: "fail",
            detail: `Terminated employees still have active accounts: ${active.join(", ")}.`,
          };
    }
    case "alerts-triaged":
      return evaluateAllOf(
        resources,
        "alert",
        "triaged",
        "true",
        "No alerts were collected.",
        "These alerts are not triaged",
      );
    case "monitors-active":
      return evaluateAllOf(
        resources,
        "monitor",
        "muted",
        "false",
        "No monitors were collected.",
        "These monitors are muted",
      );
    case "log-retention":
      return evaluateMatching(
        resources,
        "logIndex",
        (item) => flag(item, "audit") === "true",
        (item) => Number.parseInt(flag(item, "retentionDays"), 10) >= 365,
        "No audit log indexes were collected.",
        "Audit indexes keep logs for under 365 days",
      );
    case "training-completed":
      return evaluateMatching(
        resources,
        "training",
        (item) => flag(item, "status") !== "terminated",
        (item) => flag(item, "completed") === "true",
        "No training assignments were collected.",
        "These people have not completed training",
      );
    case "background-screened":
      return evaluateAllOf(
        resources,
        "backgroundCheck",
        "cleared",
        "true",
        "No background checks were collected.",
        "These screenings are not cleared",
      );
    case "database-hardening":
      return evaluateMatching(
        resources,
        "database",
        () => true,
        (item) => flag(item, "encrypted") === "true" && flag(item, "public") === "false",
        "No managed databases were collected.",
        "These databases are unencrypted or public",
      );
    case "tls-enforced":
      return evaluateAllOf(
        resources,
        "domain",
        "tls",
        "true",
        "No domains were collected.",
        "TLS is not enforced on",
      );
    case "password-manager-mfa":
      return evaluateMatching(
        resources,
        "vaultMember",
        (item) => flag(item, "status") !== "suspended",
        (item) => flag(item, "mfa") === "true",
        "No password manager members were collected.",
        "These members have no two-factor",
      );
    case "server-backups":
      return evaluateAllOf(
        resources,
        "server",
        "backups",
        "true",
        "No servers were collected.",
        "Backups are off on",
      );
    case "network-exposure":
      return evaluateAllOf(
        resources,
        "firewall",
        "open",
        "false",
        "No firewalls were collected.",
        "These firewalls allow unrestricted inbound",
      );
    case "change-approved":
      return evaluateAllOf(
        resources,
        "change",
        "approved",
        "true",
        "No changes were collected.",
        "These changes have no approver",
      );
    case "oncall-coverage":
      return evaluateAllOf(
        resources,
        "schedule",
        "covered",
        "true",
        "No on-call schedules were collected.",
        "These schedules have gaps",
      );
    default: {
      const existing = world.checks.find((item) => item.key === key);
      if (existing) {
        return { outcome: existing.outcome, detail: existing.detail };
      }
      return { outcome: "notRun", detail: "" };
    }
  }
}

function buildSummary(
  descriptor: SeedConnector,
  world: DemoWorld,
  state: DemoConnectorState,
): ConnectorSummary {
  if (descriptor.builtin) {
    const count = rowBuiltins.has(descriptor.key)
      ? world.resources.filter((item) => item.provider === descriptor.key).length
      : 0;
    return {
      key: descriptor.key,
      name: descriptor.name,
      category: descriptor.category,
      authType: descriptor.authType,
      description: descriptor.description,
      resources: descriptor.resources,
      tests: descriptor.tests,
      controls: descriptor.controls,
      docsUrl: descriptor.docsUrl,
      builtin: true,
      managedAt: descriptor.managedAt,
      status: "connected",
      mode: "mock",
      resourceCount: count,
      lastSyncedAt: rowBuiltins.has(descriptor.key) ? new Date().toISOString() : null,
      lastError: null,
    };
  }

  const connection = state.connections.get(descriptor.key);
  const status = connection ? (connection.lastError ? "error" : connection.status) : "available";
  return {
    key: descriptor.key,
    name: descriptor.name,
    category: descriptor.category,
    authType: descriptor.authType,
    description: descriptor.description,
    resources: descriptor.resources,
    tests: descriptor.tests,
    controls: descriptor.controls,
    docsUrl: descriptor.docsUrl,
    builtin: false,
    managedAt: descriptor.managedAt,
    status,
    mode: connection ? connection.mode : null,
    resourceCount: connection ? connection.resources.length : 0,
    lastSyncedAt: connection?.lastSyncedAt ?? null,
    lastError: connection?.lastError ?? null,
  };
}

function buildDetail(
  descriptor: SeedConnector,
  world: DemoWorld,
  state: DemoConnectorState,
): ConnectorDetail {
  const connection = state.connections.get(descriptor.key);
  const allResources = activeCatalogResources(state);
  const checks: ConnectorCheck[] = descriptor.tests.map((testKey) => {
    const meta = checkByKey.get(testKey);
    const evaluated = evaluateConnectorCheck(testKey, world, allResources);
    return {
      key: testKey,
      name: meta?.name ?? testKey,
      controlCode: meta?.controlCode ?? "",
      outcome: evaluated.outcome,
      detail: evaluated.detail,
    };
  });
  const history = (state.history.get(descriptor.key) ?? []).slice(0, 20);
  return {
    connector: buildSummary(descriptor, world, state),
    fields: descriptor.fields,
    config: redactConfig(descriptor, connection?.config ?? {}),
    envKeys: descriptor.envKeys,
    checks,
    history,
  };
}

function recordRun(
  state: DemoConnectorState,
  key: string,
  kind: string,
  mode: string,
  status: string,
  resourceCount: number,
  error: string | null,
  trigger: string,
): ConnectorRun {
  const now = new Date().toISOString();
  const run: ConnectorRun = {
    id: nextRunId(state),
    kind,
    mode,
    status,
    resourceCount,
    error,
    startedAt: now,
    finishedAt: now,
    trigger,
  };
  const existing = state.history.get(key) ?? [];
  state.history.set(key, [run, ...existing].slice(0, 20));
  return run;
}

function matches(item: ConnectorSummary, term: string) {
  const q = term.toLowerCase();
  return (
    item.name.toLowerCase().includes(q) ||
    item.key.toLowerCase().includes(q) ||
    item.category.toLowerCase().includes(q) ||
    item.description.toLowerCase().includes(q) ||
    item.resources.some((resource) => resource.toLowerCase().includes(q)) ||
    item.tests.some((test) => test.toLowerCase().includes(q)) ||
    item.controls.some((control) => control.toLowerCase().includes(q))
  );
}

export function handleConnectors(
  method: string,
  path: string,
  query: URLSearchParams,
  body: Record<string, unknown>,
  world: DemoWorld,
  actor: Actor,
  log: (action: string, detail: string) => void,
): Response | null {
  if (
    path !== "/api/v1/integrations/catalog" &&
    !path.startsWith("/api/v1/integrations/catalog/")
  ) {
    return null;
  }

  const state = getState(world);

  if (path === "/api/v1/integrations/catalog" && method === "GET") {
    const category = query.get("category")?.trim() ?? "";
    const search = query.get("search")?.trim() ?? "";
    const all = catalogData.connectors.map((descriptor) => buildSummary(descriptor, world, state));
    const filtered = all
      .filter((item) => !category || item.category.toLowerCase() === category.toLowerCase())
      .filter((item) => !search || matches(item, search))
      .sort(
        (left, right) =>
          Number(left.status !== "connected") - Number(right.status !== "connected") ||
          left.name.localeCompare(right.name),
      );
    const payload: ConnectorCatalog = {
      categories: catalogData.categories,
      total: all.length,
      connected: all.filter((item) => item.status === "connected").length,
      connectors: filtered,
    };
    return json(payload);
  }

  const tail = path.slice("/api/v1/integrations/catalog/".length);
  const [rawKey, action] = tail.split("/");
  const key = decodeURIComponent(rawKey ?? "");
  const descriptor = connectorByKey.get(key);
  if (!descriptor) {
    return json({ code: "not_found" }, 404);
  }

  if (!action && method === "GET") {
    return json(buildDetail(descriptor, world, state));
  }

  if (action === "history" && method === "GET") {
    return json((state.history.get(descriptor.key) ?? []).slice(0, 20));
  }

  if (
    method === "POST" &&
    (action === "connect" || action === "test" || action === "sync" || action === "disconnect")
  ) {
    if (!canWrite(actor)) {
      return json({ code: "forbidden" }, 403);
    }
    if (descriptor.builtin) {
      return json({ code: "managed_elsewhere" }, 400);
    }

    const submittedConfig =
      body.config && typeof body.config === "object"
        ? (body.config as Record<string, unknown>)
        : undefined;

    if (action === "connect") {
      const existing = state.connections.get(descriptor.key);
      const merged = mergeConfig(descriptor, existing?.config ?? {}, submittedConfig);
      const fixtures = descriptor.fixtures.map((item) => ({
        ...item,
        attributes: { ...item.attributes },
      }));
      const now = new Date().toISOString();
      state.connections.set(descriptor.key, {
        status: "connected",
        mode: "mock",
        config: merged,
        lastSyncedAt: now,
        lastError: null,
        resources: fixtures,
      });
      syncWorldResources(world, descriptor.key, fixtures);
      const run = recordRun(
        state,
        descriptor.key,
        "sync",
        "mock",
        "ok",
        fixtures.length,
        null,
        "connect",
      );
      log("integration.connector.connected", `${descriptor.key} (mock)`);
      const response: ConnectorActionResponse = {
        detail: buildDetail(descriptor, world, state),
        run,
        jobId: null,
        message: "mock",
      };
      return json(response);
    }

    if (action === "test") {
      const existing = state.connections.get(descriptor.key);
      const now = new Date().toISOString();
      const run = existing
        ? recordRun(state, descriptor.key, "test", "mock", "ok", 0, null, "manual")
        : {
            id: "00000000-0000-0000-0000-000000000000",
            kind: "test",
            mode: "mock",
            status: "ok",
            resourceCount: 0,
            error: null,
            startedAt: now,
            finishedAt: now,
            trigger: "manual",
          };
      log("integration.connector.tested", `${descriptor.key} (mock)`);
      const response: ConnectorActionResponse = {
        detail: buildDetail(descriptor, world, state),
        run,
        jobId: null,
        message: "ok",
      };
      return json(response);
    }

    if (action === "sync") {
      const existing = state.connections.get(descriptor.key);
      if (!existing || existing.status !== "connected") {
        return json({ code: "not_connected" }, 400);
      }
      const fixtures = descriptor.fixtures.map((item) => ({
        ...item,
        attributes: { ...item.attributes },
      }));
      existing.resources = fixtures;
      existing.lastSyncedAt = new Date().toISOString();
      existing.lastError = null;
      syncWorldResources(world, descriptor.key, fixtures);
      const run = recordRun(
        state,
        descriptor.key,
        "sync",
        existing.mode,
        "ok",
        fixtures.length,
        null,
        "manual",
      );
      log("integration.connector.synced", `${descriptor.key} (${existing.mode})`);
      const response: ConnectorActionResponse = {
        detail: buildDetail(descriptor, world, state),
        run,
        jobId: null,
        message: null,
      };
      return json(response);
    }

    if (action === "disconnect") {
      const existing = state.connections.get(descriptor.key);
      if (!existing) {
        return json({ code: "not_connected" }, 400);
      }
      state.connections.delete(descriptor.key);
      syncWorldResources(world, descriptor.key, []);
      log("integration.connector.disconnected", descriptor.key);
      return json(buildDetail(descriptor, world, state));
    }
  }

  return json({ code: "not_found" }, 404);
}
