import type { DemoControl, DemoWorld } from "./catalog";
import { inventoryResources } from "./client";
import { policyTemplates } from "./policyTemplates";
import {
  ConditionError,
  parseCondition,
  runTest,
  type InventoryResource,
} from "../features/tests/conditions";

type Actor = { email: string; name: string; role: string };
type Log = (action: string, detail: string) => void;

type CustomTest = {
  id: string;
  key: string;
  name: string;
  description: string;
  resourceKind: string;
  provider: string | null;
  filter: unknown;
  assertion: unknown;
  severity: string;
  controlCodes: string[];
  remediation: string;
  enabled: boolean;
  outcome: string;
  detail: string;
  createdByName: string;
  updatedAt: string;
};

type Suppression = {
  state: string;
  reason: string;
  until: string | null;
  byName: string;
  at: string;
};

type EvidenceRequest = {
  id: string;
  assigneeName: string;
  assigneeEmail: string;
  dueOn: string;
  note: string;
  status: string;
  requestedByName: string;
  requestedAt: string;
  fulfilledAt: string | null;
};

type LibraryDocument = {
  id: string;
  code: string;
  title: string;
  description: string;
  ownerName: string;
  ownerEmail: string;
  cadence: string;
  validFrom: string | null;
  validUntil: string | null;
  currentEvidenceId: string | null;
  currentSha256: string | null;
  controlCodes: string[];
  requests: EvidenceRequest[];
};

type Profile = {
  implementationGuidance: string;
  evidenceRequirement: string;
  requiredEvidenceCount: number;
  requiresPassingTest: boolean;
};

type Comment = {
  id: string;
  authorName: string;
  authorEmail: string;
  body: string;
  createdAt: string;
  editedAt: string | null;
  history: { body: string; replacedAt: string }[];
};

type Campaign = {
  id: string;
  policyCode: string;
  versionNumber: number;
  group: string;
  startedOn: string;
  dueOn: string;
  renewOn: string;
  state: string;
  people: { personName: string; personEmail: string; attestedAt: string | null }[];
};

type State = {
  tests: CustomTest[];
  suppressions: Map<string, Suppression>;
  baseline: Map<string, { outcome: string; detail: string }>;
  baseStatus: Map<string, string>;
  documents: LibraryDocument[];
  profiles: Map<string, Profile>;
  comments: Map<string, Comment[]>;
  campaigns: Campaign[];
  counter: number;
};

class Fail extends Error {
  constructor(
    public status: number,
    public code: string,
  ) {
    super(code);
  }
}

const stateByWorld = new WeakMap<DemoWorld, State>();
const severities = ["Low", "Medium", "High", "Critical"];
const cadences = ["OneTime", "Monthly", "Quarterly", "Semiannual", "Annual"];
const kindPattern = /^(\*|[A-Za-z][A-Za-z0-9-]{0,40})$/;
const day = 86_400_000;

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function today() {
  return new Date().toISOString().slice(0, 10);
}

function dateOffset(days: number, from = new Date()) {
  return new Date(from.getTime() + days * day).toISOString().slice(0, 10);
}

function addMonths(date: string, months: number) {
  const value = new Date(`${date}T00:00:00Z`);
  value.setUTCMonth(value.getUTCMonth() + months);
  return value.toISOString().slice(0, 10);
}

function isDate(value: unknown): value is string {
  return (
    typeof value === "string" &&
    /^\d{4}-\d{2}-\d{2}$/.test(value) &&
    !Number.isNaN(Date.parse(value))
  );
}

function text(value: unknown) {
  return typeof value === "string" ? value.trim() : "";
}

function nextId(state: State) {
  state.counter += 1;
  return `20000000-0000-4000-8000-${state.counter.toString(16).padStart(12, "0")}`;
}

function canWrite(actor: Actor) {
  return actor.role !== "Auditor" && actor.role !== "Employee";
}

function requireWrite(actor: Actor) {
  if (!canWrite(actor)) throw new Fail(403, "forbidden");
}

function inScope(control: DemoControl, actor: Actor) {
  return (
    actor.role !== "Auditor" || control.mappings.some((mapping) => mapping.framework === "soc2")
  );
}

function findControl(world: DemoWorld, code: string, actor: Actor) {
  const control = world.controls.find((item) => item.code.toLowerCase() === code.toLowerCase());
  if (!control || !inScope(control, actor)) throw new Fail(404, "not_found");
  return control;
}

export function inventory(world: DemoWorld): InventoryResource[] {
  return [
    ...inventoryResources(),
    ...world.people.map((person) => ({
      provider: "d2d",
      kind: "person",
      externalId: person.email,
      name: person.name,
      attributes: {
        email: person.email,
        department: person.department,
        role: person.role,
        state: person.state,
        training: person.training,
        mfaEnrolled: person.mfaEnrolled ? "true" : "false",
        hireOn: person.hireOn ?? "",
      },
    })),
  ];
}

export function expiryStatus(hasEvidence: boolean, validUntil: string | null, now = today()) {
  if (!hasEvidence) return "missing";
  if (!validUntil) return "current";
  if (validUntil < now) return "expired";
  return validUntil <= dateOffset(30, new Date(`${now}T00:00:00Z`)) ? "expiringSoon" : "current";
}

function nextValidUntil(from: string, cadence: string) {
  const months = { Monthly: 1, Quarterly: 3, Semiannual: 6, Annual: 12 }[cadence];
  return months ? addMonths(from, months) : null;
}

function documentStatus(document: LibraryDocument) {
  return expiryStatus(Boolean(document.currentEvidenceId), document.validUntil);
}

function libraryBlocks(statuses: string[]) {
  return (
    statuses.length > 0 && !statuses.some((item) => item === "current" || item === "expiringSoon")
  );
}

function suppressionActive(item: Suppression | undefined) {
  if (!item) return false;
  return (
    item.state === "Disabled" ||
    (item.state === "Snoozed" && item.until !== null && item.until > today())
  );
}

function describeSuppression(item: Suppression) {
  return item.state === "Snoozed"
    ? `Snoozed until ${item.until}: ${item.reason}`
    : `Disabled: ${item.reason}`;
}

function stateFor(world: DemoWorld): State {
  let state = stateByWorld.get(world);
  if (state) return state;
  state = {
    tests: [],
    suppressions: new Map(),
    baseline: new Map(
      world.checks.map((check) => [check.id, { outcome: check.outcome, detail: check.detail }]),
    ),
    baseStatus: new Map(world.controls.map((control) => [control.code, control.status])),
    documents: [],
    profiles: new Map(),
    comments: new Map(),
    campaigns: [],
    counter: 0,
  };
  stateByWorld.set(world, state);
  seed(world, state);
  apply(world, state);
  return state;
}

function seed(world: DemoWorld, state: State) {
  const now = new Date();
  state.tests.push({
    id: nextId(state),
    key: "custom-production-s3-buckets-must-have-versioning",
    name: "Production S3 buckets must have versioning",
    description:
      "Every production bucket keeps prior object versions so deleted or overwritten data can be restored.",
    resourceKind: "bucket",
    provider: "aws",
    filter: { all: [{ field: "name", op: "contains", value: "-prod" }] },
    assertion: { all: [{ field: "attributes.versioning", op: "equals", value: "true" }] },
    severity: "High",
    controlCodes: ["CR-02", "BC-01"],
    remediation: "Turn on bucket versioning for the failing production buckets.",
    enabled: true,
    outcome: "notRun",
    detail: "",
    createdByName: "Priya Nair",
    updatedAt: new Date(now.getTime() - 3 * day).toISOString(),
  });
  const owner = (code: string) => world.controls.find((item) => item.code === code);
  const doc = (
    code: string,
    title: string,
    description: string,
    cadence: string,
    validFrom: string | null,
    controlCodes: string[],
  ): LibraryDocument => {
    const control = owner(controlCodes[0]);
    return {
      id: nextId(state),
      code,
      title,
      description,
      ownerName: control?.ownerName ?? "Chandra Vell",
      ownerEmail: control?.ownerEmail ?? "chandra.vell@harborline.example",
      cadence,
      validFrom,
      validUntil: validFrom ? nextValidUntil(validFrom, cadence) : null,
      currentEvidenceId: validFrom ? nextId(state) : null,
      currentSha256: validFrom
        ? "9f2c1e7a54b0d3c8e61a2f4b7d90c5e3a8b16f2d4c7e90a1b3c5d7e9f0a2b4c6d"
        : null,
      controlCodes,
      requests: [],
    };
  };
  const evd4 = doc(
    "EVD-004",
    "Hosting provider SOC 2 bridge letter",
    "Bridge letter covering the gap since the hosting provider's last SOC 2 report.",
    "Semiannual",
    null,
    ["GV-04"],
  );
  evd4.requests.push({
    id: nextId(state),
    assigneeName: "Chandra Vell",
    assigneeEmail: "chandra.vell@harborline.example",
    dueOn: dateOffset(9, now),
    note: "Please upload the latest bridge letter from the hosting provider portal.",
    status: "Open",
    requestedByName: "Priya Nair",
    requestedAt: new Date(now.getTime() - 2 * day).toISOString(),
    fulfilledAt: null,
  });
  state.documents.push(
    doc(
      "EVD-001",
      "Annual penetration test report",
      "Third-party penetration test of the production platform, with retest results.",
      "Annual",
      dateOffset(-385, now),
      ["VM-05", "CM-14"],
    ),
    doc(
      "EVD-002",
      "Quarterly access review sign-off",
      "Signed record that production access was reviewed and leavers were removed.",
      "Quarterly",
      addMonths(dateOffset(12, now), -3),
      ["AC-04", "AC-15"],
    ),
    doc(
      "EVD-003",
      "Business continuity test summary",
      "Summary of the latest restore and failover exercise with lessons learned.",
      "Annual",
      dateOffset(-60, now),
      ["BC-04"],
    ),
    evd4,
  );
  state.profiles.set("CM-14", {
    implementationGuidance:
      "Keep the annual penetration test and retest report in the evidence library.",
    evidenceRequirement: "Current penetration test report and remediation tracker.",
    requiredEvidenceCount: 1,
    requiresPassingTest: false,
  });
  state.comments.set("CM-14", [
    {
      id: nextId(state),
      authorName: "Priya Nair",
      authorEmail: "priya.nair@harborline.example",
      body: "The pen test vendor is booked for next month; the old report has lapsed.",
      createdAt: new Date(now.getTime() - 4 * day).toISOString(),
      editedAt: new Date(now.getTime() - 3 * day).toISOString(),
      history: [
        {
          body: "Pen test vendor still to be booked.",
          replacedAt: new Date(now.getTime() - 3 * day).toISOString(),
        },
      ],
    },
  ]);
  const policy = world.policies.find((item) => item.code === "POL-ISP");
  const approved = policy?.versions.filter((item) => item.state === "Approved").at(-1);
  if (policy && approved) {
    const started = dateOffset(-20, now);
    state.campaigns.push({
      id: nextId(state),
      policyCode: policy.code,
      versionNumber: approved.number,
      group: "Everyone",
      startedOn: started,
      dueOn: dateOffset(10, now),
      renewOn: addMonths(started, 12),
      state: "Active",
      people: world.people
        .filter((person) => !person.leftOn)
        .map((person, index) => ({
          personName: person.name,
          personEmail: person.email,
          attestedAt:
            index % 4 === 0 || person.email.startsWith("chandra.")
              ? null
              : new Date(now.getTime() - ((index % 18) + 1) * day).toISOString(),
        })),
    });
  }
}

/** Projects custom tests, suppressions and library readiness onto the shared demo world. */
function apply(world: DemoWorld, state: State) {
  world.checks = world.checks.filter((check) => !check.key.startsWith("custom-"));
  for (const control of world.controls) {
    control.tests = control.tests.filter((test) => !test.id.startsWith("custom:"));
    control.status = state.baseStatus.get(control.code) ?? control.status;
  }
  for (const check of world.checks) {
    const base = state.baseline.get(check.id);
    if (base) {
      check.outcome = base.outcome;
      check.detail = base.detail;
    }
  }
  const resources = inventory(world);
  const now = new Date();
  for (const test of state.tests) {
    const run = runTest(
      test.resourceKind,
      test.provider,
      test.filter,
      test.assertion,
      resources,
      now,
    );
    test.outcome = test.enabled ? run.outcome : "notRun";
    test.detail = test.enabled ? run.detail : "Disabled.";
    for (const code of test.controlCodes) {
      const control = world.controls.find((item) => item.code === code);
      if (!control) continue;
      const id = `custom:${test.id}:${code}`;
      world.checks.push({
        id,
        key: test.key,
        name: test.name,
        controlCode: code,
        outcome: test.outcome,
        detail: test.detail,
        remediation: test.remediation,
        requirements: control.mappings.map((mapping) => mapping.requirement).slice(0, 3),
      });
      control.tests.push({
        id,
        name: test.name,
        kind: "custom",
        outcome: test.outcome,
        detail: test.detail,
        remediation: test.remediation,
      });
    }
  }
  for (const check of world.checks) {
    const suppression = state.suppressions.get(check.key);
    if (suppressionActive(suppression) && suppression) {
      check.outcome = "notRun";
      check.detail = describeSuppression(suppression);
    }
    const control = world.controls.find((item) => item.code === check.controlCode);
    const test = control?.tests.find((item) => item.id === check.id);
    if (test) {
      test.outcome = check.outcome;
      test.detail = check.detail;
    }
  }
  for (const control of world.controls) {
    const customFail = control.tests.some(
      (test) => test.id.startsWith("custom:") && test.outcome === "fail",
    );
    if (customFail && control.status !== "failing") control.status = "failing";
    const statuses = state.documents
      .filter((document) => document.controlCodes.includes(control.code))
      .map(documentStatus);
    if (control.status === "passing" && libraryBlocks(statuses)) control.status = "needsEvidence";
  }
}

function testDto(test: CustomTest) {
  return {
    id: test.id,
    key: test.key,
    name: test.name,
    description: test.description,
    resourceKind: test.resourceKind,
    provider: test.provider,
    filter: test.filter ?? null,
    assertion: test.assertion,
    severity: test.severity,
    controlCodes: test.controlCodes,
    remediation: test.remediation,
    enabled: test.enabled,
    outcome: test.outcome,
    detail: test.detail,
    createdByName: test.createdByName,
    updatedAt: test.updatedAt,
  };
}

function parseConditions(filter: unknown, assertion: unknown) {
  try {
    parseCondition(filter, true);
    parseCondition(assertion, false);
  } catch (error) {
    throw new Fail(400, error instanceof ConditionError ? error.code : "condition_invalid");
  }
}

function keyFor(name: string, existing: string[]) {
  const slug =
    name
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-+|-+$/g, "")
      .slice(0, 48)
      .replace(/-+$/g, "") || "test";
  const base = `custom-${slug}`;
  if (!existing.includes(base)) return base;
  let index = 2;
  while (existing.includes(`${base}-${index}`)) index += 1;
  return `${base}-${index}`;
}

function fillTest(world: DemoWorld, test: CustomTest, body: Record<string, unknown>) {
  const name = text(body.name);
  const resourceKind = text(body.resourceKind);
  const provider = text(body.provider) || null;
  const severity = text(body.severity);
  const controlCodes = Array.isArray(body.controlCodes)
    ? [...new Set(body.controlCodes.map((item) => text(item).toUpperCase()).filter(Boolean))]
    : [];
  const remediation = text(body.remediation);
  const description = text(body.description);
  if (name.length < 3 || name.length > 200) throw new Fail(400, "invalid_name");
  if (!kindPattern.test(resourceKind)) throw new Fail(400, "invalid_resource_kind");
  if (provider && !kindPattern.test(provider)) throw new Fail(400, "invalid_provider");
  if (!severities.includes(severity)) throw new Fail(400, "invalid_severity");
  if (controlCodes.length === 0 || controlCodes.length > 10)
    throw new Fail(400, "invalid_controls");
  if (controlCodes.some((code) => !world.controls.some((control) => control.code === code))) {
    throw new Fail(400, "invalid_controls");
  }
  if (remediation.length > 2000) throw new Fail(400, "invalid_remediation");
  if (description.length > 2000) throw new Fail(400, "invalid_description");
  parseConditions(body.filter, body.assertion);
  Object.assign(test, {
    name,
    description,
    resourceKind,
    provider,
    filter: body.filter ?? null,
    assertion: body.assertion,
    severity,
    controlCodes,
    remediation,
    enabled: body.enabled !== false,
    updatedAt: new Date().toISOString(),
  });
}

async function sha256(value: string) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value));
  return [...new Uint8Array(digest)].map((item) => item.toString(16).padStart(2, "0")).join("");
}

async function writeEvidence(
  world: DemoWorld,
  state: State,
  title: string,
  codes: string[],
  body: string,
) {
  const hash = await sha256(body);
  const ids: string[] = [];
  for (const code of codes) {
    const id = nextId(state);
    ids.push(id);
    world.evidence.unshift({
      id,
      title,
      controlCode: code,
      source: "Demo upload",
      collectedAt: new Date().toISOString(),
      integrity: "verified",
      sha256: hash,
      lockedUntil: null,
      body,
    });
  }
  return { id: ids[0] ?? null, hash };
}

async function handleTests(
  method: string,
  path: string,
  body: Record<string, unknown>,
  world: DemoWorld,
  state: State,
  actor: Actor,
  log: Log,
) {
  if (path === "/api/v1/tests/custom" && method === "GET") {
    const visible = state.tests.filter((test) =>
      test.controlCodes.some((code) => {
        const control = world.controls.find((item) => item.code === code);
        return control && inScope(control, actor);
      }),
    );
    return json(visible.map(testDto));
  }
  if (path === "/api/v1/tests/resource-types" && method === "GET") {
    const groups = new Map<
      string,
      { provider: string; kind: string; count: number; fields: Set<string> }
    >();
    for (const item of inventory(world)) {
      const key = `${item.provider}/${item.kind}`;
      const group = groups.get(key) ?? {
        provider: item.provider,
        kind: item.kind,
        count: 0,
        fields: new Set<string>(),
      };
      group.count += 1;
      Object.keys(item.attributes).forEach((field) => group.fields.add(field));
      groups.set(key, group);
    }
    return json(
      [...groups.values()]
        .sort((a, b) => a.provider.localeCompare(b.provider) || a.kind.localeCompare(b.kind))
        .map((group) => ({
          provider: group.provider,
          kind: group.kind,
          count: group.count,
          fields: [
            "name",
            "provider",
            "kind",
            "externalId",
            ...[...group.fields]
              .sort()
              .slice(0, 40)
              .map((field) => `attributes.${field}`),
          ],
        })),
    );
  }
  if (path === "/api/v1/tests/states" && method === "GET") {
    return json(
      [...state.suppressions.entries()].map(([testKey, item]) => ({
        testKey,
        state: item.state,
        reason: item.reason,
        until: item.until,
        byName: item.byName,
        at: item.at,
        active: suppressionActive(item),
      })),
    );
  }
  if (path === "/api/v1/tests/custom/preview" && method === "POST") {
    requireWrite(actor);
    const kind = text(body.resourceKind);
    if (!kind) throw new Fail(400, "invalid_resource_kind");
    parseConditions(body.filter, body.assertion);
    const run = runTest(
      kind,
      text(body.provider) || null,
      body.filter,
      body.assertion,
      inventory(world),
      new Date(),
    );
    const view = (item: InventoryResource) => ({
      provider: item.provider,
      kind: item.kind,
      name: item.name,
      externalId: item.externalId,
    });
    return json({
      outcome: run.outcome,
      detail: run.detail,
      total: run.passing.length + run.failing.length,
      passing: run.passing.slice(0, 25).map(view),
      failing: run.failing.slice(0, 25).map(view),
    });
  }
  if (path === "/api/v1/tests/custom" && method === "POST") {
    requireWrite(actor);
    const test: CustomTest = {
      id: nextId(state),
      key: "",
      name: "",
      description: "",
      resourceKind: "",
      provider: null,
      filter: null,
      assertion: null,
      severity: "Medium",
      controlCodes: [],
      remediation: "",
      enabled: true,
      outcome: "notRun",
      detail: "",
      createdByName: actor.name,
      updatedAt: new Date().toISOString(),
    };
    fillTest(world, test, body);
    test.key = keyFor(test.name, [
      ...state.tests.map((item) => item.key),
      ...world.checks.map((item) => item.key),
    ]);
    state.tests.push(test);
    apply(world, state);
    await writeEvidence(
      world,
      state,
      `Custom test result: ${test.name}`,
      test.controlCodes,
      test.detail,
    );
    log("test.custom.created", test.key);
    return json(testDto(test));
  }
  const customMatch = /^\/api\/v1\/tests\/custom\/([^/]+)$/.exec(path);
  if (customMatch && (method === "PUT" || method === "DELETE")) {
    requireWrite(actor);
    const test = state.tests.find((item) => item.id === customMatch[1]);
    if (!test) throw new Fail(404, "not_found");
    if (method === "DELETE") {
      state.tests = state.tests.filter((item) => item !== test);
      state.suppressions.delete(test.key);
      apply(world, state);
      log("test.custom.deleted", test.key);
      return new Response(null, { status: 204 });
    }
    fillTest(world, test, body);
    apply(world, state);
    log("test.custom.updated", test.key);
    return json(testDto(test));
  }
  const stateMatch = /^\/api\/v1\/tests\/([^/]+)\/state$/.exec(path);
  if (stateMatch && method === "PUT") {
    requireWrite(actor);
    const key = decodeURIComponent(stateMatch[1]);
    if (!world.checks.some((check) => check.key === key)) throw new Fail(404, "not_found");
    const next = text(body.state);
    if (next === "Enabled") {
      state.suppressions.delete(key);
      apply(world, state);
      log("test.enabled", key);
      return json(null);
    }
    const reason = text(body.reason);
    const until =
      body.until === null || body.until === undefined || body.until === "" ? null : body.until;
    if (next !== "Disabled" && next !== "Snoozed") throw new Fail(400, "invalid_state");
    if (reason.length < 3 || reason.length > 500) throw new Fail(400, "reason_required");
    if (next === "Snoozed") {
      if (!isDate(until) || until <= today()) throw new Fail(400, "invalid_until");
      if (until > dateOffset(365)) throw new Fail(400, "snooze_too_long");
    }
    const item: Suppression = {
      state: next,
      reason,
      until: next === "Snoozed" ? (until as string) : null,
      byName: actor.name,
      at: new Date().toISOString(),
    };
    state.suppressions.set(key, item);
    apply(world, state);
    log(next === "Snoozed" ? "test.snoozed" : "test.disabled", `${key}: ${reason}`);
    return json({ testKey: key, ...item, active: suppressionActive(item) });
  }
  return null;
}

function documentDto(document: LibraryDocument) {
  return {
    ...document,
    status: documentStatus(document),
    requests: document.requests.map((request) => requestDto(document, request)),
  };
}

function requestDto(document: LibraryDocument, request: EvidenceRequest) {
  return {
    ...request,
    documentCode: document.code,
    documentTitle: document.title,
    overdue: request.status === "Open" && request.dueOn < today(),
  };
}

function visibleDocuments(world: DemoWorld, state: State, actor: Actor) {
  if (actor.role !== "Auditor") return state.documents;
  return state.documents.filter((document) =>
    document.controlCodes.some((code) => {
      const control = world.controls.find((item) => item.code === code);
      return control && inScope(control, actor);
    }),
  );
}

async function handleLibrary(
  method: string,
  path: string,
  body: Record<string, unknown>,
  world: DemoWorld,
  state: State,
  actor: Actor,
  log: Log,
) {
  if (path === "/api/v1/evidence/library" && method === "GET") {
    const documents = visibleDocuments(world, state, actor).map(documentDto);
    const count = (status: string) => documents.filter((item) => item.status === status).length;
    return json({
      summary: {
        total: documents.length,
        current: count("current"),
        expiringSoon: count("expiringSoon"),
        expired: count("expired"),
        missing: count("missing"),
        openRequests: documents
          .flatMap((item) => item.requests)
          .filter((item) => item.status === "Open").length,
      },
      documents,
      myRequests: state.documents.flatMap((document) =>
        document.requests
          .filter(
            (request) =>
              request.status === "Open" &&
              request.assigneeEmail.toLowerCase() === actor.email.toLowerCase(),
          )
          .map((request) => requestDto(document, request)),
      ),
    });
  }
  if (path === "/api/v1/evidence/library" && method === "POST") {
    requireWrite(actor);
    const title = text(body.title);
    const ownerEmail = text(body.ownerEmail);
    const cadence = text(body.cadence);
    const codes = Array.isArray(body.controlCodes)
      ? [...new Set(body.controlCodes.map((item) => text(item).toUpperCase()).filter(Boolean))]
      : [];
    if (title.length < 3 || title.length > 200) throw new Fail(400, "invalid_document");
    if (!cadences.includes(cadence)) throw new Fail(400, "invalid_cadence");
    if (!ownerEmail.includes("@")) throw new Fail(400, "invalid_owner");
    if (codes.some((code) => !world.controls.some((control) => control.code === code))) {
      throw new Fail(400, "invalid_controls");
    }
    const document: LibraryDocument = {
      id: nextId(state),
      code: `EVD-${String(state.documents.length + 1).padStart(3, "0")}`,
      title,
      description: text(body.description),
      ownerName: text(body.ownerName) || ownerEmail,
      ownerEmail,
      cadence,
      validFrom: null,
      validUntil: null,
      currentEvidenceId: null,
      currentSha256: null,
      controlCodes: codes,
      requests: [],
    };
    state.documents.push(document);
    apply(world, state);
    log("evidence.library.created", document.code);
    return json(documentDto(document));
  }
  if (path === "/api/v1/evidence/library/links" && method === "POST") {
    requireWrite(actor);
    const documentCodes = Array.isArray(body.documentCodes) ? body.documentCodes.map(text) : [];
    const controlCodes = Array.isArray(body.controlCodes)
      ? body.controlCodes.map((item) => text(item).toUpperCase())
      : [];
    if (documentCodes.length === 0 || controlCodes.length === 0)
      throw new Fail(400, "invalid_link");
    if (controlCodes.some((code) => !world.controls.some((control) => control.code === code))) {
      throw new Fail(400, "invalid_controls");
    }
    const documents = documentCodes.map((code) =>
      state.documents.find((item) => item.code === code),
    );
    if (documents.some((item) => !item)) throw new Fail(404, "not_found");
    let added = 0;
    for (const document of documents as LibraryDocument[]) {
      for (const code of controlCodes) {
        if (!document.controlCodes.includes(code)) {
          document.controlCodes.push(code);
          added += 1;
        }
      }
    }
    apply(world, state);
    log("evidence.library.linked", `${documentCodes.join(",")} -> ${controlCodes.join(",")}`);
    return json(added);
  }
  if (path === "/api/v1/evidence/library/renewals" && method === "POST") {
    requireWrite(actor);
    let created = 0;
    for (const document of state.documents) {
      const status = documentStatus(document);
      const open = world.workTasks.some(
        (task) =>
          task.linkedKind === "Evidence" &&
          task.linkedCode === document.code &&
          task.status === "Open",
      );
      if (status === "current" || open) continue;
      world.workTasks.push({
        id: nextId(state),
        title: `Renew evidence: ${document.title}`,
        linkedKind: "Evidence",
        linkedCode: document.code,
        ownerName: document.ownerName,
        ownerEmail: document.ownerEmail,
        dueOn:
          document.validUntil && document.validUntil > today()
            ? document.validUntil
            : dateOffset(14),
        status: "Open",
        recurrence: "None",
      });
      created += 1;
    }
    log("evidence.library.renewals", String(created));
    return json(created);
  }
  const requestMatch = /^\/api\/v1\/evidence\/library\/([^/]+)\/requests$/.exec(path);
  if (requestMatch && method === "POST") {
    requireWrite(actor);
    const document = state.documents.find(
      (item) => item.code === decodeURIComponent(requestMatch[1]),
    );
    if (!document) throw new Fail(404, "not_found");
    const email = text(body.assigneeEmail);
    const note = text(body.note);
    if (!email.includes("@") || !text(body.assigneeName)) throw new Fail(400, "invalid_assignee");
    if (!isDate(body.dueOn) || body.dueOn < today()) throw new Fail(400, "invalid_due");
    if (note.length > 1000) throw new Fail(400, "invalid_note");
    document.requests.push({
      id: nextId(state),
      assigneeName: text(body.assigneeName),
      assigneeEmail: email,
      dueOn: body.dueOn,
      note,
      status: "Open",
      requestedByName: actor.name,
      requestedAt: new Date().toISOString(),
      fulfilledAt: null,
    });
    log("evidence.library.requested", `${document.code} -> ${email}`);
    return json(documentDto(document));
  }
  const uploadMatch = /^\/api\/v1\/evidence\/library\/([^/]+)\/upload$/.exec(path);
  if (uploadMatch && method === "POST") {
    const document = state.documents.find(
      (item) => item.code === decodeURIComponent(uploadMatch[1]),
    );
    if (!document) throw new Fail(404, "not_found");
    const assigned = document.requests.some(
      (request) =>
        request.status === "Open" &&
        request.assigneeEmail.toLowerCase() === actor.email.toLowerCase(),
    );
    if (!canWrite(actor) && !assigned) throw new Fail(403, "forbidden");
    const fileName = text(body.fileName);
    const content = text(body.contentBase64);
    if (!fileName || fileName.length > 200 || !content) throw new Fail(400, "invalid_evidence");
    let decoded = "";
    try {
      decoded = atob(content);
    } catch {
      throw new Fail(400, "invalid_evidence");
    }
    if (decoded.length === 0 || decoded.length > 5_000_000) throw new Fail(400, "invalid_evidence");
    const validFrom =
      body.validFrom === undefined || body.validFrom === null || body.validFrom === ""
        ? today()
        : body.validFrom;
    if (!isDate(validFrom) || validFrom > today()) throw new Fail(400, "invalid_valid_from");
    if (document.controlCodes.length === 0) throw new Fail(400, "document_unlinked");
    const written = await writeEvidence(
      world,
      state,
      `${document.title} (${fileName})`,
      document.controlCodes,
      decoded,
    );
    document.validFrom = validFrom;
    document.validUntil = nextValidUntil(validFrom, document.cadence);
    document.currentEvidenceId = written.id;
    document.currentSha256 = written.hash;
    for (const request of document.requests) {
      if (request.status === "Open") {
        request.status = "Fulfilled";
        request.fulfilledAt = new Date().toISOString();
      }
    }
    apply(world, state);
    log("evidence.library.uploaded", document.code);
    return json(documentDto(document));
  }
  return null;
}

function readiness(world: DemoWorld, state: State, control: DemoControl) {
  const profile = state.profiles.get(control.code) ?? {
    implementationGuidance: "",
    evidenceRequirement: "",
    requiredEvidenceCount: 0,
    requiresPassingTest: false,
  };
  const library = state.documents
    .filter((document) => document.controlCodes.includes(control.code))
    .map(documentDto);
  const checks: { key: string; met: boolean; detail: string }[] = [
    {
      key: "owner",
      met: Boolean(control.ownerEmail),
      detail: control.ownerEmail ? "Owner assigned." : "Assign an owner.",
    },
  ];
  const ran = control.tests.filter((test) => test.outcome !== "notRun");
  const failing = ran.filter((test) => test.outcome === "fail").length;
  const passing = ran.filter((test) => test.outcome === "pass").length;
  if (failing > 0) checks.push({ key: "tests", met: false, detail: `${failing} test(s) failing.` });
  else if (profile.requiresPassingTest && passing === 0)
    checks.push({ key: "tests", met: false, detail: "A passing test is required." });
  else
    checks.push({
      key: "tests",
      met: true,
      detail: ran.length === 0 ? "No tests have run." : `${passing} test(s) passing.`,
    });
  const evidenceCount = world.evidence.filter((item) => item.controlCode === control.code).length;
  const required = Math.max(0, profile.requiredEvidenceCount);
  checks.push(
    evidenceCount >= required
      ? { key: "evidence", met: true, detail: `${evidenceCount} evidence item(s) on file.` }
      : {
          key: "evidence",
          met: false,
          detail: `${required - evidenceCount} more evidence item(s) needed.`,
        },
  );
  if (library.length > 0) {
    const statuses = library.map((item) => item.status);
    const blocked = libraryBlocks(statuses);
    const expired = statuses.filter((item) => item === "expired" || item === "missing").length;
    checks.push({
      key: "library",
      met: !blocked,
      detail: blocked
        ? "All linked library evidence is expired or missing."
        : expired > 0
          ? `${expired} linked item(s) expired; at least one is current.`
          : "Linked library evidence is current.",
    });
  }
  const met = checks.filter((item) => item.met).length;
  return {
    controlCode: control.code,
    ready: met === checks.length,
    percent: Math.floor((met * 100) / checks.length),
    checks,
    profile,
    libraryEvidence: library,
  };
}

function handleControls(
  method: string,
  path: string,
  body: Record<string, unknown>,
  world: DemoWorld,
  state: State,
  actor: Actor,
  log: Log,
) {
  const match = /^\/api\/v1\/controls\/([^/]+)\/(readiness|profile|comments)(?:\/([^/]+))?$/.exec(
    path,
  );
  if (!match) return null;
  const control = findControl(world, decodeURIComponent(match[1]), actor);
  const [, , action, commentId] = match;
  if (action === "readiness" && method === "GET") return json(readiness(world, state, control));
  if (action === "profile" && method === "PUT") {
    requireWrite(actor);
    const guidance = text(body.implementationGuidance);
    const requirement = text(body.evidenceRequirement);
    const count = Number(body.requiredEvidenceCount ?? 0);
    if (
      guidance.length > 4000 ||
      requirement.length > 2000 ||
      !Number.isInteger(count) ||
      count < 0 ||
      count > 50
    ) {
      throw new Fail(400, "invalid_profile");
    }
    state.profiles.set(control.code, {
      implementationGuidance: guidance,
      evidenceRequirement: requirement,
      requiredEvidenceCount: count,
      requiresPassingTest: body.requiresPassingTest === true,
    });
    const ownerEmail = text(body.ownerEmail);
    if (ownerEmail) {
      if (!ownerEmail.includes("@")) throw new Fail(400, "invalid_owner");
      control.ownerEmail = ownerEmail;
      control.ownerName = text(body.ownerName) || ownerEmail;
    }
    log("control.profile.updated", control.code);
    return json(readiness(world, state, control));
  }
  const comments = state.comments.get(control.code) ?? [];
  if (action === "comments" && method === "GET" && !commentId) {
    return json([...comments].sort((a, b) => b.createdAt.localeCompare(a.createdAt)));
  }
  if (action === "comments" && (method === "POST" || method === "PUT")) {
    requireWrite(actor);
    const value = text(body.body);
    if (!value || value.length > 2000) throw new Fail(400, "invalid_comment");
    if (method === "POST" && !commentId) {
      const comment: Comment = {
        id: nextId(state),
        authorName: actor.name,
        authorEmail: actor.email,
        body: value,
        createdAt: new Date().toISOString(),
        editedAt: null,
        history: [],
      };
      state.comments.set(control.code, [...comments, comment]);
      log("control.comment.added", control.code);
      return json(comment);
    }
    const comment = comments.find((item) => item.id === commentId);
    if (!comment) throw new Fail(404, "not_found");
    if (comment.authorEmail.toLowerCase() !== actor.email.toLowerCase() && actor.role !== "Admin") {
      throw new Fail(403, "forbidden");
    }
    const at = new Date().toISOString();
    comment.history.unshift({ body: comment.body, replacedAt: at });
    comment.body = value;
    comment.editedAt = at;
    log("control.comment.edited", control.code);
    return json(comment);
  }
  return null;
}

export function lineDiff(before: string, after: string) {
  const left = before.split(/\r?\n/).slice(0, 1000);
  const right = after.split(/\r?\n/).slice(0, 1000);
  const table = Array.from({ length: left.length + 1 }, () =>
    new Array<number>(right.length + 1).fill(0),
  );
  for (let i = left.length - 1; i >= 0; i -= 1) {
    for (let j = right.length - 1; j >= 0; j -= 1) {
      table[i][j] =
        left[i] === right[j] ? table[i + 1][j + 1] + 1 : Math.max(table[i + 1][j], table[i][j + 1]);
    }
  }
  const lines: {
    kind: string;
    text: string;
    oldNumber: number | null;
    newNumber: number | null;
  }[] = [];
  let i = 0;
  let j = 0;
  while (i < left.length || j < right.length) {
    if (i < left.length && j < right.length && left[i] === right[j]) {
      lines.push({ kind: "same", text: left[i], oldNumber: i + 1, newNumber: j + 1 });
      i += 1;
      j += 1;
    } else if (j < right.length && (i >= left.length || table[i][j + 1] >= table[i + 1][j])) {
      lines.push({ kind: "added", text: right[j], oldNumber: null, newNumber: j + 1 });
      j += 1;
    } else {
      lines.push({ kind: "removed", text: left[i], oldNumber: i + 1, newNumber: null });
      i += 1;
    }
  }
  return lines;
}

function campaignDto(campaign: Campaign) {
  const now = today();
  let status = "current";
  if (campaign.state === "Renewed") status = "renewed";
  else if (campaign.renewOn < now) status = "overdue";
  else if (campaign.renewOn <= dateOffset(30)) status = "renewalDue";
  return {
    id: campaign.id,
    policyCode: campaign.policyCode,
    versionNumber: campaign.versionNumber,
    group: campaign.group,
    startedOn: campaign.startedOn,
    dueOn: campaign.dueOn,
    renewOn: campaign.renewOn,
    status,
    attested: campaign.people.filter((person) => person.attestedAt).length,
    total: campaign.people.length,
    people: campaign.people,
  };
}

function handlePolicies(
  method: string,
  path: string,
  query: URLSearchParams,
  body: Record<string, unknown>,
  world: DemoWorld,
  state: State,
  actor: Actor,
  log: Log,
) {
  if (path === "/api/v1/policies/library" && method === "POST") {
    requireWrite(actor);
    let added = 0;
    for (const template of policyTemplates) {
      if (world.policies.some((policy) => policy.code === template.code)) continue;
      world.policies.push({
        code: template.code,
        title: template.title,
        ownerName: actor.name,
        controlCodes: template.controlCodes.filter((code) =>
          world.controls.some((control) => control.code === code),
        ),
        versions: [
          {
            number: 1,
            state: "Draft",
            body: template.body,
            createdAt: new Date().toISOString(),
            approvedAt: null,
            approvedByName: null,
          },
        ],
        acknowledgements: [],
      });
      added += 1;
    }
    log("policy.library.installed", String(added));
    return json(added);
  }
  const match =
    /^\/api\/v1\/policies\/([^/]+)\/(diff|attestations|attest)(?:\/([^/]+)\/(renew))?$/.exec(path);
  if (!match) return null;
  const [, rawCode, action, campaignId, renew] = match;
  const code = decodeURIComponent(rawCode);
  const policy = world.policies.find((item) => item.code === code);
  const visible =
    policy &&
    (actor.role !== "Auditor" ||
      policy.controlCodes.some((item) => {
        const control = world.controls.find((entry) => entry.code === item);
        return control && inScope(control, actor);
      }));
  if (!policy || !visible) throw new Fail(404, "not_found");
  if (action === "diff" && method === "GET") {
    const from = policy.versions.find((item) => item.number === Number(query.get("from")));
    const to = policy.versions.find((item) => item.number === Number(query.get("to")));
    if (!from || !to) throw new Fail(404, "not_found");
    const lines = lineDiff(from.body, to.body);
    return json({
      code: policy.code,
      from: from.number,
      to: to.number,
      added: lines.filter((item) => item.kind === "added").length,
      removed: lines.filter((item) => item.kind === "removed").length,
      lines,
    });
  }
  const campaigns = state.campaigns.filter((item) => item.policyCode === policy.code);
  if (action === "attestations" && method === "GET" && !campaignId) {
    return json(campaigns.map(campaignDto).sort((a, b) => b.startedOn.localeCompare(a.startedOn)));
  }
  if (action === "attestations" && method === "POST" && !campaignId) {
    requireWrite(actor);
    const group = text(body.group);
    if (!group || group.length > 100) throw new Fail(400, "invalid_group");
    if (!isDate(body.dueOn) || body.dueOn < today()) throw new Fail(400, "invalid_due");
    const approved = policy.versions.filter((item) => item.state === "Approved").at(-1);
    if (!approved) throw new Fail(400, "no_approved_version");
    const people = world.people.filter(
      (person) =>
        !person.leftOn &&
        (group === "Everyone" || person.department.toLowerCase() === group.toLowerCase()),
    );
    if (people.length === 0) throw new Fail(400, "empty_group");
    const campaign: Campaign = {
      id: nextId(state),
      policyCode: policy.code,
      versionNumber: approved.number,
      group,
      startedOn: today(),
      dueOn: body.dueOn,
      renewOn: addMonths(today(), 12),
      state: "Active",
      people: people.map((person) => ({
        personName: person.name,
        personEmail: person.email,
        attestedAt: null,
      })),
    };
    state.campaigns.push(campaign);
    log("policy.attestation.started", `${policy.code} v${approved.number} · ${group}`);
    return json(campaignDto(campaign));
  }
  if (action === "attestations" && renew && method === "POST") {
    requireWrite(actor);
    const campaign = campaigns.find((item) => item.id === campaignId);
    if (!campaign) throw new Fail(404, "not_found");
    if (campaign.state === "Renewed") throw new Fail(400, "already_renewed");
    const approved = policy.versions.filter((item) => item.state === "Approved").at(-1);
    if (!approved) throw new Fail(400, "no_approved_version");
    campaign.state = "Renewed";
    const next: Campaign = {
      ...campaign,
      id: nextId(state),
      versionNumber: approved.number,
      startedOn: today(),
      dueOn: dateOffset(30),
      renewOn: addMonths(today(), 12),
      state: "Active",
      people: campaign.people.map((person) => ({ ...person, attestedAt: null })),
    };
    state.campaigns.push(next);
    log("policy.attestation.renewed", `${policy.code} · ${campaign.group}`);
    return json(campaignDto(next));
  }
  if (action === "attest" && method === "POST") {
    const campaign = campaigns.find(
      (item) =>
        item.state === "Active" &&
        item.people.some(
          (person) => person.personEmail.toLowerCase() === actor.email.toLowerCase(),
        ),
    );
    if (!campaign) throw new Fail(404, "not_found");
    const person = campaign.people.find(
      (item) => item.personEmail.toLowerCase() === actor.email.toLowerCase(),
    );
    if (person && !person.attestedAt) person.attestedAt = new Date().toISOString();
    log("policy.attested", `${policy.code} v${campaign.versionNumber}`);
    return json(campaignDto(campaign));
  }
  return null;
}

export async function handleAssurance(
  method: string,
  path: string,
  query: URLSearchParams,
  body: Record<string, unknown>,
  world: DemoWorld,
  actor: Actor,
  log: Log,
): Promise<Response | null> {
  const state = stateFor(world);
  try {
    if (path.startsWith("/api/v1/tests/"))
      return await handleTests(method, path, body, world, state, actor, log);
    if (path.startsWith("/api/v1/evidence/library")) {
      return await handleLibrary(method, path, body, world, state, actor, log);
    }
    if (path.startsWith("/api/v1/controls/"))
      return handleControls(method, path, body, world, state, actor, log);
    if (path.startsWith("/api/v1/policies/")) {
      return handlePolicies(method, path, query, body, world, state, actor, log);
    }
    return null;
  } catch (error) {
    if (error instanceof Fail) return json({ code: error.code }, error.status);
    if (error instanceof ConditionError) return json({ code: error.code }, 400);
    throw error;
  }
}
