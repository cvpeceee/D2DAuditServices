import { exampleAccounts, examplePassword } from "./accounts";
import {
  createWorld,
  type DemoEvidence,
  type DemoFinding,
  type DemoPolicy,
  type DemoRequest,
  type DemoWorld,
} from "./catalog";
import { zipStore } from "./zip";

const tenantId = "a360c8ce-0988-4dfc-9722-bce45697b553";
const sessionKey = "d2d-demo-user";

type Session = (typeof exampleAccounts)[number];
type Job = {
  id: string;
  kind: string;
  status: string;
  step: number;
  totalSteps: number;
  currentStep: string;
  percent: number;
  error: string | null;
  updatedAt: string;
  zip?: ArrayBuffer;
};

type Activity = {
  at: string;
  action: string;
  result: string;
  detail: string | null;
  actorEmail: string | null;
  ipAddress: null;
};

let worldPromise: Promise<DemoWorld> | null = null;
let session: Session | null = null;
let jobs: Job[] = [];
let activity: Activity[] = [];
let stepDelayMs = 400;
let idCounter = 1;

export function setDemoStepDelay(milliseconds: number) {
  stepDelayMs = milliseconds;
}

export async function resetDemo() {
  worldPromise = createWorld();
  await worldPromise;
  session = null;
  jobs = [];
  activity = [];
  idCounter = 1;
  sessionStorage.removeItem(sessionKey);
}

function world() {
  worldPromise ??= createWorld();
  return worldPromise;
}

function restore() {
  if (session) return;
  const email = sessionStorage.getItem(sessionKey);
  session = exampleAccounts.find((account) => account.email === email) ?? null;
}

export function installDemoFetch() {
  const native = window.fetch.bind(window);
  window.fetch = (input, init) => {
    const path = requestPath(input);
    if (path.startsWith("/api/")) return demoResponse(path, init);
    return native(input, init);
  };
}

export async function demoResponse(path: string, init?: RequestInit) {
  restore();
  const url = new URL(path, "http://demo.local");
  const method = (init?.method ?? "GET").toUpperCase();
  const body = await readBody(init?.body);
  try {
    return await route(method, url.pathname, url.searchParams, body);
  } catch (error) {
    const code = error instanceof DemoError ? error.code : "request_failed";
    const status = error instanceof DemoError ? error.status : 500;
    return json({ code }, status);
  }
}

class DemoError extends Error {
  constructor(
    public status: number,
    public code: string,
  ) {
    super(code);
  }
}

async function route(
  method: string,
  path: string,
  query: URLSearchParams,
  body: Record<string, unknown>,
) {
  if (path === "/api/v1/auth/mode" && method === "GET") {
    return json({
      mode: "demo",
      localBanner: true,
      exampleEmails: exampleAccounts.map((account) => account.email),
    });
  }
  if (path === "/api/v1/auth/login" && method === "POST") return login(body);
  if (path === "/api/v1/auth/logout" && method === "POST") {
    session = null;
    sessionStorage.removeItem(sessionKey);
    return new Response(null, { status: 204 });
  }
  if (path === "/api/v1/auth/refresh" && method === "POST") {
    return session ? json(authBody()) : json({ errorCode: "invalid_refresh" }, 401);
  }
  if (path === "/api/v1/auth/authorize" && method === "POST") {
    throw new DemoError(400, "local_identity_mode");
  }
  if (path === "/api/v1/auth/forgot" && method === "POST") return json({ result: "sent" });
  if (path === "/api/v1/me" && method === "GET") {
    if (!session) return json({ errorCode: "unauthorized" }, 401);
    return json(authBody());
  }

  const actor = requireUser();
  const data = await world();
  const scoped = inScope(data, actor);

  if (path === "/api/v1/workspace") return json(workspace(scoped, actor));
  if (path === "/api/v1/dashboard") return json(dashboard(scoped, actor));
  if (path === "/api/v1/frameworks" && method === "GET") return json(frameworkList(scoped));
  if (path.startsWith("/api/v1/frameworks/"))
    return framework(scoped, decodeURIComponent(path.slice("/api/v1/frameworks/".length)));
  if (path === "/api/v1/controls" && method === "GET") return json(controlList(scoped, query));
  if (path.startsWith("/api/v1/controls/") && path.endsWith("/owner") && method === "PUT") {
    return json(
      assignOwner(data, actor, path.slice("/api/v1/controls/".length, -"/owner".length), body),
    );
  }
  if (path.startsWith("/api/v1/controls/")) {
    return json(controlDetail(scoped, decodeURIComponent(path.slice("/api/v1/controls/".length))));
  }
  if (path === "/api/v1/tests") return json(scoped.checks);
  if (path === "/api/v1/evidence" && method === "GET") return json(evidencePage(scoped));
  if (path === "/api/v1/evidence/verify" && method === "POST") return json(await verify(data));
  if (path === "/api/v1/evidence" && method === "POST")
    return json(await upload(data, actor, body));
  if (path === "/api/v1/integrations" && method === "GET") return json(integrations());
  if (path === "/api/v1/integrations/example" && method === "POST") {
    requireWrite(actor);
    return json(integrations());
  }
  if (path === "/api/v1/integrations/collect" && method === "POST") {
    requireWrite(actor);
    return json(
      startJob(
        "collection",
        [
          "Reading connections",
          "Collecting example fixtures",
          "Running automated tests",
          "Storing evidence",
          "Updating controls",
        ],
        "Collection finished",
      ),
    );
  }
  if (path.startsWith("/api/v1/integrations/") && path.endsWith("/connect")) {
    requireWrite(actor);
    throw new DemoError(400, "provider_not_configured");
  }
  if (path === "/api/v1/policies" && method === "GET") return json(policyList(scoped));
  if (path === "/api/v1/policies/reminders" && method === "POST") {
    requireWrite(actor);
    return json(remind(data, actor));
  }
  if (path.startsWith("/api/v1/policies/"))
    return policyRoute(data, scoped, actor, method, path.slice("/api/v1/policies/".length), body);
  if (path === "/api/v1/audits" && method === "GET") {
    if (actor.role === "Auditor") record(actor, "audit.portal.viewed", "ok", null);
    return json(auditPage(scoped));
  }
  if (path === "/api/v1/audits/requests" && method === "POST")
    return json(createRequest(data, actor, body));
  if (path.startsWith("/api/v1/audits/requests/") && path.endsWith("/respond")) {
    return json(respond(data, scoped, actor, path.split("/")[5], body));
  }
  if (path.startsWith("/api/v1/audits/requests/") && path.endsWith("/accept")) {
    return json(accept(data, actor, path.split("/")[5]));
  }
  if (path === "/api/v1/audits/findings" && method === "POST")
    return json(createFinding(data, actor, body));
  if (path.startsWith("/api/v1/audits/findings/") && path.endsWith("/response")) {
    return json(management(data, actor, path.split("/")[5], body));
  }
  if (path === "/api/v1/audits/package" && method === "POST") {
    requireAudit(actor);
    const jobId = startJob(
      "export",
      [
        "Reading controls and evidence",
        "Checking the hash chain",
        "Packing evidence files",
        "Writing the manifest and control matrix",
        "Storing the package",
      ],
      "Package ready",
      (job) => finishPackage(data, scoped, job),
    );
    record(actor, "audit.package.started", "ok", jobId);
    return json(jobId);
  }
  if (path.startsWith("/api/v1/audits/packages/")) {
    requireAudit(actor);
    return packageFile(path.slice("/api/v1/audits/packages/".length));
  }
  if (path === "/api/v1/jobs") return json(jobs.map(publicJob));
  if (path === "/api/v1/activity") {
    if (actor.role !== "Admin") throw new DemoError(403, "forbidden");
    return json(activity);
  }
  throw new DemoError(404, "not_found");
}

function policyRoute(
  data: DemoWorld,
  scoped: DemoWorld,
  actor: Session,
  method: string,
  tail: string,
  body: Record<string, unknown>,
) {
  const [code, action] = tail.split("/");
  const visible = scoped.policies.find((item) => item.code === code);
  const policy = data.policies.find((item) => item.code === code);
  if (!policy || !visible) throw new DemoError(404, "not_found");
  if (method === "GET") return json(policyDetail(policy));
  if (action === "acknowledge") return json(acknowledge(policy, actor));
  requireWrite(actor);
  if (action === "approve") {
    if (actor.role !== "Admin") throw new DemoError(403, "forbidden");
    return json(approve(policy, actor));
  }
  if (action === "draft") return json(draft(policy));
  if (action === "submit") return json(submit(policy));
  if (action === "acknowledgements") return json(assignAck(policy, body));
  throw new DemoError(404, "not_found");
}

function login(body: Record<string, unknown>) {
  const email = String(body.email ?? "").toLowerCase();
  const account = exampleAccounts.find((item) => item.email === email);
  if (!account || body.password !== examplePassword)
    return json({ errorCode: "invalid_credentials" }, 401);
  session = account;
  sessionStorage.setItem(sessionKey, account.email);
  record(account, "sign_in", "ok", "example account");
  return json(authBody());
}

function authBody() {
  if (!session)
    return { ok: false, errorCode: "unauthorized", user: null, tenants: null, mfaRequired: false };
  return {
    ok: true,
    errorCode: null,
    user: {
      id: session.role === "Admin" ? idOf(1) : idOf(2),
      email: session.email,
      displayName: session.name,
    },
    tenants: [
      {
        tenantId,
        name: "Acme Robotics",
        slug: "acme-robotics",
        region: "US",
        role: session.role,
        exampleData: true,
      },
    ],
    mfaRequired: false,
    pendingAuthenticationToken: null,
    challengeId: null,
    factors: null,
  };
}

function workspace(data: DemoWorld, actor: Session) {
  return {
    tenantId,
    name: "Acme Robotics",
    region: "US",
    role: actor.role,
    exampleData: true,
    canWrite: actor.role !== "Auditor",
    displayName: actor.name,
    email: actor.email,
    failingControls: data.controls.filter((item) => item.status === "failing").length,
  };
}

function dashboard(data: DemoWorld, actor: Session) {
  const health = {
    passing: count(data, "passing"),
    failing: count(data, "failing"),
    dueSoon: count(data, "dueSoon"),
    needsEvidence: count(data, "needsEvidence"),
    manual: count(data, "manual"),
    notStarted: count(data, "notStarted"),
  };
  const failingTests = data.controls.flatMap((control) =>
    control.tests
      .filter((test) => test.outcome === "fail")
      .map((test) => ({
        testId: test.id,
        testName: test.name,
        controlCode: control.code,
        detail: test.detail,
        requirementCodes: control.mappings.map(
          (mapping) => `${mapping.framework} ${mapping.requirement}`,
        ),
      })),
  );
  const open = data.controls.filter((item) =>
    ["failing", "dueSoon", "needsEvidence"].includes(item.status),
  ).length;
  return {
    displayName: actor.name,
    tenantName: "Acme Robotics",
    region: "US",
    exampleData: true,
    readinessPercent: percent(health.passing, data.controls.length),
    frameworkCount: data.frameworks.length,
    frameworks: frameworkList(data).map((item) => ({
      key: item.key,
      name: item.name,
      passing: item.passingControlCount,
      total: item.mappedControlCount,
      percent: item.percent,
    })),
    audit: {
      name: "SOC 2 Type II — Fieldwork",
      firm: "Northgate Assurance LLP",
      frameworkKey: "soc2",
      fieldworkOn: "2026-11-30",
      openItems: open,
      samplesDue: data.controls
        .flatMap((item) => item.tests)
        .filter((test) => test.kind === "manualSampling" && test.outcome !== "pass").length,
    },
    failingTests,
    health,
  };
}

function frameworkList(data: DemoWorld) {
  return data.frameworks.map((framework) => {
    const mapped = data.controls.filter((control) =>
      control.mappings.some((mapping) => mapping.framework === framework.key),
    );
    const passing = mapped.filter((control) => control.status === "passing").length;
    return {
      key: framework.key,
      name: framework.name,
      version: framework.version,
      description: framework.description,
      requirementCount: leaves(framework.requirements),
      mappedControlCount: mapped.length,
      passingControlCount: passing,
      percent: percent(passing, mapped.length),
    };
  });
}

function framework(data: DemoWorld, key: string) {
  const match = data.frameworks.find((item) => item.key === key);
  if (!match) throw new DemoError(404, "not_found");
  return json(match);
}

function controlList(data: DemoWorld, query: URLSearchParams) {
  let rows = data.controls;
  const status = query.get("status");
  if (status) rows = rows.filter((item) => item.status.toLowerCase() === status.toLowerCase());
  const owner = query.get("owner");
  if (owner) rows = rows.filter((item) => item.ownerEmail.toLowerCase() === owner.toLowerCase());
  const term = query.get("q");
  if (term) {
    rows = rows.filter(
      (item) =>
        item.code.toLowerCase().includes(term.toLowerCase()) ||
        item.title.toLowerCase().includes(term.toLowerCase()),
    );
  }
  return rows
    .slice()
    .sort((a, b) => a.code.localeCompare(b.code))
    .map((item) => ({
      code: item.code,
      title: item.title,
      status: item.status,
      ownerName: item.ownerName,
      ownerEmail: item.ownerEmail,
      frequency: item.frequency,
      testsPassing: item.tests.filter((test) => test.outcome === "pass").length,
      testsTotal: item.tests.length,
      frameworks: [...new Set(item.mappings.map((mapping) => mapping.framework))],
    }));
}

function controlDetail(data: DemoWorld, code: string) {
  const control = data.controls.find((item) => item.code.toLowerCase() === code.toLowerCase());
  if (!control) throw new DemoError(404, "not_found");
  return {
    code: control.code,
    title: control.title,
    description: control.description,
    status: control.status,
    ownerName: control.ownerName,
    ownerEmail: control.ownerEmail,
    frequency: control.frequency,
    mappings: control.mappings.map((mapping) => ({
      frameworkKey: mapping.framework,
      frameworkName: mapping.frameworkName,
      requirementCode: mapping.requirement,
      requirementTitle: mapping.requirement,
      coverage: mapping.coverage,
    })),
    tests: control.tests,
    evidence: evidenceFor(data, control.code),
  };
}

function assignOwner(data: DemoWorld, actor: Session, code: string, body: Record<string, unknown>) {
  requireWrite(actor);
  const control = data.controls.find(
    (item) => item.code.toLowerCase() === decodeURIComponent(code).toLowerCase(),
  );
  if (!control) throw new DemoError(404, "not_found");
  const name = String(body.ownerName ?? "").trim();
  const email = String(body.ownerEmail ?? "").trim();
  if (email && !email.includes("@")) throw new DemoError(400, "invalid_owner");
  control.ownerName = name;
  control.ownerEmail = email;
  return controlDetail(data, control.code);
}

function evidencePage(data: DemoWorld) {
  return {
    summary: {
      total: data.evidence.length,
      automated: data.evidence.filter((item) => item.source === "Example fixture").length,
      manual: data.evidence.filter((item) => item.source !== "Example fixture").length,
      verified: data.evidence.filter((item) => item.integrity === "verified").length,
      retentionYears: 3,
      chainIntact: data.evidence.every((item) => item.integrity === "verified"),
    },
    items: data.evidence.map(publicEvidence),
  };
}

function publicEvidence(item: DemoEvidence) {
  return {
    id: item.id,
    title: item.title,
    controlCode: item.controlCode,
    source: item.source,
    collectedAt: item.collectedAt,
    integrity: item.integrity,
    sha256: item.sha256,
    lockedUntil: item.lockedUntil,
    objectKey: null,
  };
}

async function verify(data: DemoWorld) {
  let intact = true;
  for (const item of data.evidence) {
    const hash = await sha256(item.body);
    if (hash !== item.sha256) {
      item.integrity = "broken";
      intact = false;
    }
  }
  return {
    intact,
    checked: data.evidence.length,
    objectKey: null,
    reason: intact ? null : "content_hash",
  };
}

async function upload(data: DemoWorld, actor: Session, body: Record<string, unknown>) {
  requireWrite(actor);
  const controlCode = String(body.controlCode ?? "");
  const control = data.controls.find((item) => item.code === controlCode);
  if (!control) throw new DemoError(404, "not_found");
  const title = String(body.title ?? "").trim();
  const content = decodeBase64(String(body.contentBase64 ?? ""));
  if (!title || !content) throw new DemoError(400, "invalid_evidence");
  const text = new TextDecoder().decode(content);
  const item: DemoEvidence = {
    id: freshId(),
    title,
    controlCode,
    source: "Manual upload",
    collectedAt: new Date().toISOString(),
    integrity: "verified",
    sha256: await sha256(text),
    lockedUntil: null,
    body: text,
  };
  data.evidence.unshift(item);
  return publicEvidence(item);
}

function integrations() {
  return ["aws", "google", "microsoft", "github"].map((provider, index) => ({
    id: idOf(20 + index),
    provider,
    displayName:
      provider === "aws"
        ? "Amazon Web Services"
        : provider === "google"
          ? "Google Workspace"
          : provider === "microsoft"
            ? "Microsoft 365"
            : "GitHub",
    status: "connected",
    example: true,
    accountLabel: "Acme Robotics example",
    externalId: null,
    roleArn: null,
    domain: null,
    setupHint: "Example fixture. No customer account is connected.",
    lastCollectedAt: new Date().toISOString(),
  }));
}

function policyList(data: DemoWorld) {
  return data.policies
    .slice()
    .sort((a, b) => a.code.localeCompare(b.code))
    .map((policy) => {
      const latest = [...policy.versions].sort((a, b) => b.number - a.number)[0];
      const approved = [...policy.versions]
        .filter((item) => item.state === "Approved")
        .sort((a, b) => b.number - a.number)[0];
      const rows = policy.acknowledgements.filter((item) => item.version === approved?.number);
      const today = new Date().toISOString().slice(0, 10);
      return {
        code: policy.code,
        title: policy.title,
        ownerName: policy.ownerName,
        version: latest.number,
        state: latest.state,
        controlCodes: policy.controlCodes,
        acknowledged: rows.filter((item) => item.acknowledgedAt).length,
        outstanding: rows.filter((item) => !item.acknowledgedAt && item.dueOn < today).length,
      };
    });
}

function policyDetail(policy: DemoPolicy) {
  const approved = [...policy.versions]
    .filter((item) => item.state === "Approved")
    .sort((a, b) => b.number - a.number)[0];
  const today = new Date().toISOString().slice(0, 10);
  return {
    code: policy.code,
    title: policy.title,
    ownerName: policy.ownerName,
    controlCodes: policy.controlCodes,
    versions: [...policy.versions].sort((a, b) => b.number - a.number),
    acknowledgements: policy.acknowledgements
      .filter((item) => item.version === approved?.number)
      .map((item) => ({
        personName: item.personName,
        personEmail: item.personEmail,
        dueOn: item.dueOn,
        acknowledgedAt: item.acknowledgedAt,
        remindedAt: item.remindedAt,
        reminderDue: needsReminder(item, today),
      })),
  };
}

function needsReminder(
  item: { dueOn: string; acknowledgedAt: string | null; remindedAt: string | null },
  today: string,
) {
  if (item.acknowledgedAt || item.dueOn >= today) return false;
  if (!item.remindedAt) return true;
  return item.remindedAt.slice(0, 10) < today;
}

function draft(policy: DemoPolicy) {
  const latest = [...policy.versions].sort((a, b) => b.number - a.number)[0];
  if (latest.state === "Draft" || latest.state === "InReview")
    throw new DemoError(400, "version_open");
  if (latest.state !== "Approved") throw new DemoError(400, "not_approved");
  policy.versions.push({
    number: latest.number + 1,
    state: "Draft",
    body: latest.body,
    createdAt: new Date().toISOString(),
    approvedAt: null,
    approvedByName: null,
  });
  return policyDetail(policy);
}

function submit(policy: DemoPolicy) {
  const latest = [...policy.versions].sort((a, b) => b.number - a.number)[0];
  if (latest.state !== "Draft") throw new DemoError(400, "not_draft");
  latest.state = "InReview";
  return policyDetail(policy);
}

function approve(policy: DemoPolicy, actor: Session) {
  const latest = [...policy.versions].sort((a, b) => b.number - a.number)[0];
  if (latest.state !== "InReview") throw new DemoError(400, "not_in_review");
  for (const version of policy.versions) {
    if (version.state === "Approved") version.state = "Superseded";
  }
  latest.state = "Approved";
  latest.approvedAt = new Date().toISOString();
  latest.approvedByName = actor.name;
  record(actor, "policy.approved", "ok", policy.code);
  return policyDetail(policy);
}

function assignAck(policy: DemoPolicy, body: Record<string, unknown>) {
  const approved = [...policy.versions]
    .filter((item) => item.state === "Approved")
    .sort((a, b) => b.number - a.number)[0];
  if (!approved) throw new DemoError(400, "not_approved");
  const email = String(body.personEmail ?? "")
    .trim()
    .toLowerCase();
  const name = String(body.personName ?? "").trim();
  if (!name || !email.includes("@")) throw new DemoError(400, "invalid_person");
  if (
    policy.acknowledgements.some(
      (item) => item.version === approved.number && item.personEmail === email,
    )
  ) {
    throw new DemoError(400, "already_assigned");
  }
  policy.acknowledgements.push({
    version: approved.number,
    personName: name,
    personEmail: email,
    dueOn: String(body.dueOn ?? new Date().toISOString().slice(0, 10)),
    acknowledgedAt: null,
    remindedAt: null,
  });
  return policyDetail(policy);
}

function acknowledge(policy: DemoPolicy, actor: Session) {
  const approved = [...policy.versions]
    .filter((item) => item.state === "Approved")
    .sort((a, b) => b.number - a.number)[0];
  const row = policy.acknowledgements.find(
    (item) =>
      item.version === approved?.number &&
      item.personEmail.toLowerCase() === actor.email.toLowerCase() &&
      !item.acknowledgedAt,
  );
  if (!row) throw new DemoError(404, "not_found");
  row.acknowledgedAt = new Date().toISOString();
  return policyDetail(policy);
}

function remind(data: DemoWorld, actor: Session) {
  const today = new Date().toISOString().slice(0, 10);
  let countReminders = 0;
  for (const policy of data.policies) {
    const approved = [...policy.versions]
      .filter((item) => item.state === "Approved")
      .sort((a, b) => b.number - a.number)[0];
    for (const item of policy.acknowledgements) {
      if (item.version === approved?.number && needsReminder(item, today)) {
        item.remindedAt = new Date().toISOString();
        countReminders += 1;
      }
    }
  }
  record(actor, "policy.reminded", "ok", String(countReminders));
  return countReminders;
}

function auditPage(data: DemoWorld) {
  const responded = data.requests.filter((item) => item.response);
  const hours =
    responded.length === 0
      ? null
      : Math.round(
          (responded.reduce((sum, item) => {
            const start = new Date(item.createdAt).getTime();
            const end = new Date(item.response?.respondedAt ?? item.createdAt).getTime();
            return sum + (end - start) / 36e5;
          }, 0) /
            responded.length) *
            10,
        ) / 10;
  return {
    engagement: {
      name: "SOC 2 Type II — Fieldwork",
      firmName: "Northgate Assurance LLP",
      frameworkKey: "soc2",
      scope: "SOC 2 Type II (Security, Availability). Example engagement for Acme Robotics.",
      periodStart: "2026-04-01",
      periodEnd: "2026-09-30",
      fieldworkOn: "2026-11-30",
      controlsPassing: data.controls.filter((item) => item.status === "passing").length,
      controlsTotal: data.controls.length,
      requestsOpen: data.requests.filter((item) => item.status === "Open").length,
      requestsTotal: data.requests.length,
      findingsTotal: data.findings.length,
      exceptions: data.findings.filter((item) => item.severity === "Exception").length,
      averageResponseHours: hours,
    },
    requests: [...data.requests],
    findings: [...data.findings],
    evidence: data.evidence.slice(0, 40).map((item) => ({
      id: item.id,
      title: item.title,
      controlCode: item.controlCode,
    })),
  };
}

function createRequest(data: DemoWorld, actor: Session, body: Record<string, unknown>) {
  requireAudit(actor);
  const title = String(body.title ?? "").trim();
  const population = String(body.population ?? "").trim();
  const source = String(body.source ?? "").trim();
  const dueOn = String(body.dueOn ?? "");
  if (!title || !population || !source || !dueOn) throw new DemoError(400, "invalid_request");
  const numbers = data.requests
    .map((item) => Number(item.code.replace("REQ-", "")))
    .filter((item) => !Number.isNaN(item));
  const code = `REQ-${Math.max(0, ...numbers) + 1}`;
  const row: DemoRequest = {
    code,
    title,
    population,
    sampleSize: body.sampleSize == null || body.sampleSize === "" ? null : Number(body.sampleSize),
    source,
    dueOn,
    status: "Open",
    createdAt: new Date().toISOString(),
    response: null,
  };
  data.requests.unshift(row);
  record(actor, "audit.request.created", "ok", code);
  return row;
}

function respond(
  data: DemoWorld,
  scoped: DemoWorld,
  actor: Session,
  code: string,
  body: Record<string, unknown>,
) {
  requireWrite(actor);
  const row = data.requests.find((item) => item.code === code);
  if (!row || row.status !== "Open") throw new DemoError(400, "not_open");
  const evidenceId = body.evidenceId ? String(body.evidenceId) : null;
  const evidence = evidenceId ? scoped.evidence.find((item) => item.id === evidenceId) : null;
  if (evidenceId && !evidence) throw new DemoError(400, "invalid_evidence");
  row.status = "Submitted";
  row.response = {
    body: String(body.body ?? ""),
    respondedByName: actor.name,
    respondedAt: new Date().toISOString(),
    evidenceTitle: evidence?.title ?? null,
  };
  record(actor, "audit.request.responded", "ok", code);
  return row;
}

function accept(data: DemoWorld, actor: Session, code: string) {
  requireAudit(actor);
  const row = data.requests.find((item) => item.code === code);
  if (!row || row.status !== "Submitted") throw new DemoError(400, "not_submitted");
  row.status = "Accepted";
  record(actor, "audit.request.accepted", "ok", code);
  return row;
}

function createFinding(data: DemoWorld, actor: Session, body: Record<string, unknown>) {
  requireAudit(actor);
  const severity = String(body.severity ?? "");
  if (severity !== "Exception" && severity !== "Observation")
    throw new DemoError(400, "invalid_finding");
  const numbers = data.findings
    .map((item) => Number(item.code.replace("F-", "")))
    .filter((item) => !Number.isNaN(item));
  const row: DemoFinding = {
    code: `F-${Math.max(0, ...numbers) + 1}`,
    title: String(body.title ?? ""),
    severity,
    detail: String(body.detail ?? ""),
    requirementCode: String(body.requirementCode ?? ""),
    managementResponse: null,
    responseByName: null,
  };
  data.findings.unshift(row);
  record(actor, "audit.finding.created", "ok", row.code);
  return row;
}

function management(data: DemoWorld, actor: Session, code: string, body: Record<string, unknown>) {
  requireWrite(actor);
  const row = data.findings.find((item) => item.code === code);
  if (!row) throw new DemoError(404, "not_found");
  row.managementResponse = String(body.response ?? "");
  row.responseByName = actor.name;
  record(actor, "audit.finding.responded", "ok", code);
  return row;
}

async function finishPackage(data: DemoWorld, scoped: DemoWorld, job: Job | undefined) {
  if (!job) return;
  const report = await verify(data);
  const files = scoped.evidence.map((item) => ({
    path: `evidence/${item.controlCode}/${slug(item.title)}.txt`,
    sha256: item.sha256,
    text: item.body,
    controlCode: item.controlCode,
    title: item.title,
  }));
  const manifest = {
    tenantId,
    generatedAt: new Date().toISOString(),
    intact: report.intact,
    checked: report.checked,
    reason: report.reason,
    files: files.map((file) => ({
      path: file.path,
      sha256: file.sha256,
      recordedSha256: file.sha256,
      controlCode: file.controlCode,
      title: file.title,
    })),
  };
  const csv = [
    "code,title,status,requirements,evidenceCount,sha256",
    ...scoped.controls.map((control) => {
      const hashes = scoped.evidence
        .filter((item) => item.controlCode === control.code)
        .map((item) => item.sha256);
      return [
        control.code,
        csvCell(control.title),
        control.status,
        csvCell(control.mappings.map((mapping) => mapping.requirement).join(" ")),
        hashes.length,
        hashes[0] ?? "",
      ].join(",");
    }),
  ].join("\n");
  job.zip = zipStore([
    { name: "manifest.json", text: JSON.stringify(manifest, null, 2) },
    {
      name: "integrity-report.json",
      text: JSON.stringify({
        intact: report.intact,
        checked: report.checked,
        reason: report.reason,
      }),
    },
    { name: "control-matrix.csv", text: csv },
    ...files.map((file) => ({ name: file.path, text: file.text })),
  ]);
}

function packageFile(id: string) {
  const job = jobs.find((item) => item.id === id);
  if (!job || job.kind !== "export" || job.status !== "Succeeded" || !job.zip) {
    throw new DemoError(job ? 409 : 404, job ? "not_ready" : "not_found");
  }
  return new Response(job.zip, { status: 200, headers: { "Content-Type": "application/zip" } });
}

function startJob(
  kind: string,
  steps: string[],
  done: string,
  after?: (job: Job) => void | Promise<void>,
) {
  const job: Job = {
    id: freshId(),
    kind,
    status: "Queued",
    step: 0,
    totalSteps: steps.length,
    currentStep: "Queued",
    percent: 0,
    error: null,
    updatedAt: new Date().toISOString(),
  };
  jobs = [job, ...jobs.filter((item) => item.id !== job.id)].slice(0, 20);
  publish(job);
  void run(job, steps, done, after);
  return job.id;
}

async function run(
  job: Job,
  steps: string[],
  done: string,
  after?: (job: Job) => void | Promise<void>,
) {
  for (let index = 0; index < steps.length; index += 1) {
    await wait(stepDelayMs);
    job.status = "Running";
    job.step = index + 1;
    job.currentStep = steps[index];
    job.percent = Math.round(((index + 1) / steps.length) * 100);
    job.updatedAt = new Date().toISOString();
    if (index === steps.length - 1 && after) await after(job);
    publish(job);
  }
  await wait(stepDelayMs);
  job.status = "Succeeded";
  job.currentStep = done;
  job.percent = 100;
  job.updatedAt = new Date().toISOString();
  publish(job);
}

function publish(job: Job) {
  window.dispatchEvent(new CustomEvent("d2d-job", { detail: publicJob(job) }));
}

function publicJob(job: Job) {
  return {
    id: job.id,
    kind: job.kind,
    status: job.status,
    step: job.step,
    totalSteps: job.totalSteps,
    currentStep: job.currentStep,
    percent: job.percent,
    error: job.error,
    updatedAt: job.updatedAt,
  };
}

function inScope(data: DemoWorld, actor: Session): DemoWorld {
  if (actor.role !== "Auditor") return data;
  const controls = data.controls.filter((control) =>
    control.mappings.some((mapping) => mapping.framework === "soc2"),
  );
  const codes = new Set(controls.map((control) => control.code));
  return {
    ...data,
    controls,
    evidence: data.evidence.filter((item) => codes.has(item.controlCode)),
    checks: data.checks.filter((item) => codes.has(item.controlCode)),
    policies: data.policies
      .filter((policy) => policy.controlCodes.some((code) => codes.has(code)))
      .map((policy) => ({
        ...policy,
        controlCodes: policy.controlCodes.filter((code) => codes.has(code)),
      })),
    frameworks: data.frameworks.filter((framework) => framework.key === "soc2"),
  };
}

function evidenceFor(data: DemoWorld, code: string) {
  return data.evidence
    .filter((item) => item.controlCode === code)
    .map((item) => ({
      id: item.id,
      title: item.title,
      source: item.source,
      collectedAt: item.collectedAt,
      integrity: item.integrity,
      sha256: item.sha256,
      lockedUntil: item.lockedUntil,
    }));
}

function leaves(nodes: { children: unknown[] }[]): number {
  return nodes.reduce(
    (sum, node) =>
      sum + (node.children.length === 0 ? 1 : leaves(node.children as { children: unknown[] }[])),
    0,
  );
}

function count(data: DemoWorld, status: string) {
  return data.controls.filter((item) => item.status === status).length;
}

function percent(part: number, total: number) {
  return total === 0 ? 0 : Math.round((part * 100) / total);
}

function requireUser() {
  if (!session) throw new DemoError(401, "unauthorized");
  return session;
}

function requireWrite(actor: Session) {
  if (actor.role === "Auditor") throw new DemoError(403, "forbidden");
}

function requireAudit(actor: Session) {
  if (actor.role !== "Auditor" && actor.role !== "Admin") throw new DemoError(403, "forbidden");
}

function record(actor: Session, action: string, result: string, detail: string | null) {
  activity.unshift({
    at: new Date().toISOString(),
    action,
    result,
    detail,
    actorEmail: actor.email,
    ipAddress: null,
  });
}

function freshId() {
  idCounter += 1;
  return idOf(1000 + idCounter);
}

function idOf(n: number) {
  return `10000000-0000-4000-8000-${n.toString(16).padStart(12, "0")}`;
}

function slug(value: string) {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "");
}

function csvCell(value: string) {
  return /[",\n]/.test(value) ? `"${value.replaceAll('"', '""')}"` : value;
}

async function sha256(text: string) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, "0")).join("");
}

function decodeBase64(value: string) {
  const binary = atob(value);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
  return bytes;
}

async function readBody(body: BodyInit | null | undefined) {
  if (typeof body !== "string" || body.length === 0) return {};
  return JSON.parse(body) as Record<string, unknown>;
}

function requestPath(input: RequestInfo | URL) {
  if (typeof input === "string")
    return input.startsWith("http") ? new URL(input).pathname : input.split("?")[0];
  if (input instanceof URL) return input.pathname;
  return new URL(input.url).pathname;
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function wait(milliseconds: number) {
  return new Promise((resolve) => {
    window.setTimeout(resolve, milliseconds);
  });
}
