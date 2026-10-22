import type { DemoWorld } from "./catalog";
import { annualCode, library, questions, slides } from "./programData";

type Actor = { email: string; name: string; role: string };

const kinds = ["Policy", "Training", "Mfa", "Device", "BackgroundCheck"];
const assetTypes = ["Server", "Database", "Bucket", "Repository", "Laptop", "Saas"];
const classes = ["Public", "Internal", "Confidential", "Restricted"];
const taskKinds = ["Control", "Test", "Risk", "Vendor", "Policy"];
const taskStatuses = ["Open", "InProgress", "Done"];
const recurrences = ["None", "Weekly", "Monthly", "Quarterly", "Annual"];

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

function fail(status: number, code: string) {
  return json({ code }, status);
}

function canWrite(actor: Actor) {
  return actor.role === "Admin" || actor.role === "ControlOwner" || actor.role === "Editor";
}

function today() {
  return new Date().toISOString().slice(0, 10);
}

function personOf(data: DemoWorld, email: string) {
  return data.people.find((item) => item.email.toLowerCase() === email.toLowerCase());
}

async function sha256(text: string) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, "0")).join("");
}

async function store(data: DemoWorld, title: string, controlCode: string, body: string) {
  const hash = await sha256(body);
  const id = crypto.randomUUID();
  const now = new Date();
  const locked = new Date(now);
  locked.setUTCFullYear(locked.getUTCFullYear() + 3);
  data.evidence.unshift({
    id,
    title,
    controlCode,
    source: "Employee portal",
    collectedAt: now.toISOString(),
    integrity: "verified",
    sha256: hash,
    lockedUntil: locked.toISOString(),
    body,
  });
  return { id, hash };
}

function withinYear(completed: string | null | undefined) {
  if (!completed) return false;
  const when = new Date(`${completed}T00:00:00Z`);
  when.setUTCMonth(when.getUTCMonth() + 12);
  return when >= new Date();
}

function refreshTraining(data: DemoWorld) {
  const outside = data.people.filter(
    (person) => person.state === "Active" && !withinYear(person.trainingCompletedOn),
  );
  const outcome = outside.length === 0 ? "pass" : "fail";
  const detail =
    outcome === "pass"
      ? "Personnel completed training within 12 months."
      : `Some personnel are outside the 12-month training window.`;
  const control = data.controls.find((item) => item.code === "HR-06");
  if (!control) return;
  control.status = outcome === "pass" ? "passing" : "failing";
  const test = control.tests.find(
    (item) => item.name === "Personnel completed training within 12 months",
  );
  if (test) {
    test.outcome = outcome;
    test.detail = detail;
  }
}

function cards(data: DemoWorld, actor: Actor) {
  const person = personOf(data, actor.email);
  if (!person) {
    return kinds.map((kind) => ({
      kind,
      title: cardTitle(kind),
      status: "Unassigned",
      dueOn: null,
      action: "none",
      evidenceId: null,
      evidenceSha256: null,
    }));
  }
  const acks = data.policies.flatMap((policy) =>
    policy.acknowledgements.filter(
      (item) =>
        item.personEmail.toLowerCase() === person.email.toLowerCase() && !item.acknowledgedAt,
    ),
  );
  const annual = data.completions.find(
    (item) =>
      item.moduleId === "module-annual" &&
      item.personEmail.toLowerCase() === person.email.toLowerCase(),
  );
  const evidence = annual?.evidenceId
    ? data.evidence.find((item) => item.id === annual.evidenceId)
    : undefined;
  const ackStatus =
    acks.length === 0 ? "Complete" : acks.some((item) => item.dueOn < today()) ? "Overdue" : "Due";
  const deviceReady = person.device === "Reporting" || person.device === "Screenshot";
  let background = "NotStarted";
  if (person.background === "Clear") background = "Complete";
  else if (person.background === "Pending") background = "Due";
  let training = "NotStarted";
  if (person.training === "Complete") training = "Complete";
  else if (person.training === "Overdue") training = "Overdue";
  else if (person.training === "Assigned") training = "Due";
  return [
    card(
      "Policy",
      ackStatus,
      acks[0]?.dueOn ?? null,
      acks.length === 0 ? "done" : "acknowledge",
      null,
      null,
    ),
    card(
      "Training",
      training,
      person.trainingDueOn,
      person.training === "Complete" ? "done" : "open",
      annual?.evidenceId ?? null,
      evidence?.sha256 ?? null,
    ),
    card(
      "Mfa",
      person.mfaEnrolled ? "Complete" : "Due",
      person.hireOn,
      person.mfaEnrolled ? "done" : "attest",
      null,
      null,
    ),
    card(
      "Device",
      deviceReady ? "Complete" : "Due",
      today(),
      deviceReady ? "done" : "upload",
      null,
      null,
    ),
    card(
      "BackgroundCheck",
      background,
      person.backgroundOn ?? null,
      person.background === "Clear" ? "done" : "record",
      null,
      null,
    ),
  ];
}

function card(
  kind: string,
  status: string,
  dueOn: string | null,
  action: string,
  evidenceId: string | null,
  evidenceSha256: string | null,
) {
  return { kind, title: cardTitle(kind), status, dueOn, action, evidenceId, evidenceSha256 };
}

function cardTitle(kind: string) {
  if (kind === "Policy") return "Policies to acknowledge";
  if (kind === "Training") return "Security training";
  if (kind === "Mfa") return "Multi-factor authentication";
  if (kind === "Device") return "Device";
  if (kind === "BackgroundCheck") return "Background check";
  return kind;
}

function moduleItem(data: DemoWorld, module: DemoWorld["modules"][number]) {
  const assigned = data.people.filter((person) => person.state === "Active").length;
  const completed = data.completions.filter((item) => item.moduleId === module.id).length;
  return {
    id: module.id,
    code: module.code,
    title: module.title,
    type: module.type,
    assignedGroup: module.assignedGroup,
    dueOn: module.dueOn,
    assigned,
    completed,
    externalUrl: module.externalUrl,
  };
}

function moduleDetail(data: DemoWorld, module: DemoWorld["modules"][number], actor: Actor) {
  const completion = data.completions.find(
    (item) =>
      item.moduleId === module.id && item.personEmail.toLowerCase() === actor.email.toLowerCase(),
  );
  const evidence = completion?.evidenceId
    ? data.evidence.find((item) => item.id === completion.evidenceId)
    : undefined;
  return {
    ...moduleItem(data, module),
    slides: module.type === "BuiltIn" ? slides : [],
    questions:
      module.type === "BuiltIn"
        ? questions.map(({ prompt, choices }) => ({ prompt, choices }))
        : [],
    completed: Boolean(completion),
    score: completion?.score ?? null,
    evidenceId: completion?.evidenceId ?? null,
    evidenceSha256: evidence?.sha256 ?? null,
  };
}

function taskItem(task: DemoWorld["workTasks"][number]) {
  return {
    ...task,
    overdue: task.status !== "Done" && task.dueOn < today(),
  };
}

function assetItem(asset: DemoWorld["assets"][number]) {
  return asset;
}

export async function handleProgram(
  method: string,
  path: string,
  query: URLSearchParams,
  body: Record<string, unknown>,
  data: DemoWorld,
  actor: Actor,
  onRecord: (action: string, detail: string) => void,
) {
  if (path === "/api/v1/me/cards" && method === "GET") return json(cards(data, actor));
  if (path === "/api/v1/me/cards" && method === "POST")
    return completeCard(data, actor, body, onRecord);
  if (path === "/api/v1/training" && method === "GET")
    return json(data.modules.map((item) => moduleItem(data, item)));
  if (path.startsWith("/api/v1/training/") && path.endsWith("/people") && method === "GET") {
    return trainingPeople(data, path.slice("/api/v1/training/".length, -"/people".length), query);
  }
  if (path.startsWith("/api/v1/training/") && path.endsWith("/quiz") && method === "POST") {
    return quiz(
      data,
      actor,
      path.slice("/api/v1/training/".length, -"/quiz".length),
      body,
      onRecord,
    );
  }
  if (path.startsWith("/api/v1/training/") && path.endsWith("/certificate") && method === "POST") {
    return certificate(
      data,
      actor,
      path.slice("/api/v1/training/".length, -"/certificate".length),
      body,
      onRecord,
    );
  }
  if (path.startsWith("/api/v1/training/") && method === "GET") {
    const module = data.modules.find(
      (item) => item.id === decodeURIComponent(path.slice("/api/v1/training/".length)),
    );
    return module ? json(moduleDetail(data, module, actor)) : fail(404, "not_found");
  }
  if (path === "/api/v1/assets" && method === "GET") return assetsPage(data, query);
  if (path === "/api/v1/assets" && method === "POST") return saveAsset(data, actor, body, onRecord);
  if (path === "/api/v1/assets/import" && method === "POST")
    return importAssets(data, actor, body, onRecord);
  if (path === "/api/v1/assets/export" && method === "GET") return exportAssets(data);
  if (path === "/api/v1/tasks" && method === "GET") {
    const mine = query.get("mine") === "true";
    const rows = data.workTasks.filter(
      (item) => !mine || item.ownerEmail.toLowerCase() === actor.email.toLowerCase(),
    );
    return json(rows.map(taskItem));
  }
  if (path === "/api/v1/tasks" && method === "POST") return saveTask(data, actor, body, onRecord);
  if (path.startsWith("/api/v1/tasks/") && path.endsWith("/done") && method === "POST") {
    return finishTask(data, actor, path.slice("/api/v1/tasks/".length, -"/done".length), onRecord);
  }
  if (path === "/api/v1/settings/members" && method === "GET")
    return admin(actor) ? json(data.members) : fail(403, "forbidden");
  if (path === "/api/v1/settings/members" && method === "POST")
    return invite(data, actor, body, onRecord);
  if (path.startsWith("/api/v1/settings/members/") && path.endsWith("/role") && method === "POST") {
    return setRole(
      data,
      actor,
      path.slice("/api/v1/settings/members/".length, -"/role".length),
      body,
      onRecord,
    );
  }
  if (
    path.startsWith("/api/v1/settings/members/") &&
    path.endsWith("/deactivate") &&
    method === "POST"
  ) {
    return deactivate(
      data,
      actor,
      path.slice("/api/v1/settings/members/".length, -"/deactivate".length),
      onRecord,
    );
  }
  if (path === "/api/v1/settings/sso" && method === "GET") {
    return admin(actor)
      ? json(data.sso[0] ?? { domain: "", connected: false, organizationId: null })
      : fail(403, "forbidden");
  }
  if (path === "/api/v1/settings/grants" && method === "GET")
    return admin(actor) ? json(data.grants) : fail(403, "forbidden");
  if (path === "/api/v1/settings/grants" && method === "POST")
    return saveGrant(data, actor, body, onRecord);
  if (path === "/api/v1/risks/library" && method === "GET") {
    return json(
      library.map((item) => ({
        ...item,
        added: data.risks.some((risk) => risk.libraryCode === item.code || risk.code === item.code),
      })),
    );
  }
  if (path.startsWith("/api/v1/risks/library/") && method === "POST") {
    return adopt(
      data,
      actor,
      decodeURIComponent(path.slice("/api/v1/risks/library/".length)),
      onRecord,
    );
  }
  if (path.startsWith("/api/v1/people/") && path.endsWith("/status") && method === "POST") {
    return setStatus(
      data,
      actor,
      path.slice("/api/v1/people/".length, -"/status".length),
      body,
      onRecord,
    );
  }
  if (path === "/api/v1/access-reviews/draft" && method === "POST")
    return draft(data, actor, body, onRecord);
  if (path.startsWith("/api/v1/access-reviews/") && path.endsWith("/apps") && method === "POST") {
    return pickApps(
      data,
      actor,
      path.slice("/api/v1/access-reviews/".length, -"/apps".length),
      body,
      onRecord,
    );
  }
  if (path.startsWith("/api/v1/access-reviews/") && path.endsWith("/start") && method === "POST") {
    return begin(
      data,
      actor,
      path.slice("/api/v1/access-reviews/".length, -"/start".length),
      onRecord,
    );
  }
  if (
    path.includes("/remediate") &&
    path.startsWith("/api/v1/access-reviews/") &&
    method === "POST"
  ) {
    return remediate(data, actor, path, body, onRecord);
  }
  return null;
}

async function completeCard(
  data: DemoWorld,
  actor: Actor,
  body: Record<string, unknown>,
  onRecord: (action: string, detail: string) => void,
) {
  const kind = String(body.kind ?? "");
  if (!kinds.includes(kind)) return fail(400, "invalid_card");
  const person = personOf(data, actor.email);
  if (!person) return fail(404, "not_found");
  if (kind === "Training") return fail(400, "use_module");
  let evidenceId: string | null = null;
  if (kind === "Policy") {
    for (const policy of data.policies) {
      for (const ack of policy.acknowledgements) {
        if (ack.personEmail.toLowerCase() === person.email.toLowerCase() && !ack.acknowledgedAt) {
          ack.acknowledgedAt = new Date().toISOString();
        }
      }
    }
    const stored = await store(
      data,
      `Policy acknowledgement ${person.name}`,
      "HR-01",
      `${person.name} acknowledged the assigned policies.`,
    );
    evidenceId = stored.id;
  } else if (kind === "Mfa") {
    person.mfaEnrolled = true;
    const stored = await store(
      data,
      `MFA attestation ${person.name}`,
      "HR-06",
      `${person.name} confirmed a second factor is enrolled.`,
    );
    evidenceId = stored.id;
  } else if (kind === "Device") {
    const note = String(body.body ?? body.note ?? "").trim();
    if (!note) return fail(400, "invalid_card");
    person.device = "Screenshot";
    const stored = await store(data, `Device screenshot ${person.name}`, "HR-01", note);
    evidenceId = stored.id;
  } else {
    person.background = "Clear";
    person.backgroundOn = today();
    const note = String(body.note ?? "Background check recorded.").trim();
    const stored = await store(data, `Background check ${person.name}`, "HR-01", note);
    evidenceId = stored.id;
  }
  onRecord("portal.completed", kind);
  const next = cards(data, actor).find((item) => item.kind === kind);
  return json({ ...next, evidenceId: evidenceId ?? next?.evidenceId ?? null });
}

async function quiz(
  data: DemoWorld,
  actor: Actor,
  id: string,
  body: Record<string, unknown>,
  onRecord: (action: string, detail: string) => void,
) {
  const module = data.modules.find((item) => item.id === id);
  if (!module || module.type !== "BuiltIn") return fail(404, "not_found");
  const existing = data.completions.find(
    (item) => item.moduleId === id && item.personEmail.toLowerCase() === actor.email.toLowerCase(),
  );
  if (existing) {
    const evidence = data.evidence.find((item) => item.id === existing.evidenceId);
    return json({
      passed: true,
      score: existing.score,
      total: questions.length,
      evidenceId: existing.evidenceId,
      evidenceSha256: evidence?.sha256 ?? null,
    });
  }
  const answers = Array.isArray(body.answers) ? body.answers.map((item) => Number(item)) : [];
  const score = questions.reduce(
    (sum, question, index) => sum + (answers[index] === question.answer ? 1 : 0),
    0,
  );
  if (score !== questions.length)
    return json({
      passed: false,
      score,
      total: questions.length,
      evidenceId: null,
      evidenceSha256: null,
    });
  return finishModule(
    data,
    actor,
    module,
    score,
    `${module.title} certificate. ${actor.name} completed the module. Score ${score} of ${questions.length}.`,
    onRecord,
  );
}

async function certificate(
  data: DemoWorld,
  actor: Actor,
  id: string,
  body: Record<string, unknown>,
  onRecord: (action: string, detail: string) => void,
) {
  const text = String(body.body ?? "").trim();
  if (!text) return fail(400, "invalid_certificate");
  const module = data.modules.find((item) => item.id === id);
  if (!module || module.type !== "External") return fail(404, "not_found");
  const existing = data.completions.find(
    (item) => item.moduleId === id && item.personEmail.toLowerCase() === actor.email.toLowerCase(),
  );
  if (existing)
    return json({
      passed: true,
      score: existing.score,
      total: 1,
      evidenceId: existing.evidenceId,
      evidenceSha256: null,
    });
  return finishModule(data, actor, module, 1, text, onRecord);
}

async function finishModule(
  data: DemoWorld,
  actor: Actor,
  module: DemoWorld["modules"][number],
  score: number,
  body: string,
  onRecord: (action: string, detail: string) => void,
) {
  const stored = await store(data, `${module.title} ${actor.name}`, "HR-06", body);
  data.completions.push({
    id: crypto.randomUUID(),
    moduleId: module.id,
    personEmail: actor.email,
    completedAt: new Date().toISOString(),
    score,
    evidenceId: stored.id,
  });
  const person = personOf(data, actor.email);
  if (person && module.code === annualCode) {
    person.training = "Complete";
    person.trainingCompletedOn = today();
  }
  refreshTraining(data);
  onRecord("training.completed", module.code);
  return json({
    passed: true,
    score,
    total: module.type === "BuiltIn" ? questions.length : 1,
    evidenceId: stored.id,
    evidenceSha256: stored.hash,
  });
}

function trainingPeople(data: DemoWorld, id: string, query: URLSearchParams) {
  const pageSize = 40;
  const page = Math.max(1, Number(query.get("page")) || 1);
  const start = (page - 1) * pageSize;
  const items = data.people.slice(start, start + pageSize).map((person) => {
    const completion = data.completions.find(
      (item) => item.moduleId === id && item.personEmail === person.email,
    );
    return {
      name: person.name,
      email: person.email,
      department: person.department,
      status: completion ? "Complete" : person.training,
      completedOn: completion ? completion.completedAt.slice(0, 10) : null,
      dueOn: person.trainingDueOn,
    };
  });
  return json({ items, total: data.people.length, page, pageSize });
}

function assetsPage(data: DemoWorld, query: URLSearchParams) {
  const term = (query.get("q") ?? "").trim().toLowerCase();
  const pageSize = Math.min(100, Math.max(1, Number(query.get("pageSize")) || 40));
  const page = Math.max(1, Number(query.get("page")) || 1);
  const filtered = data.assets.filter((item) => {
    if (!term) return true;
    return `${item.name} ${item.type} ${item.source} ${item.ownerName}`
      .toLowerCase()
      .includes(term);
  });
  const start = (page - 1) * pageSize;
  return json({
    items: filtered.slice(start, start + pageSize).map(assetItem),
    total: filtered.length,
    page,
    pageSize,
  });
}

function saveAsset(
  data: DemoWorld,
  actor: Actor,
  body: Record<string, unknown>,
  onRecord: (action: string, detail: string) => void,
) {
  if (!canWrite(actor)) return fail(403, "forbidden");
  const name = String(body.name ?? "").trim();
  const type = String(body.type ?? "");
  const classification = String(body.classification ?? "");
  if (!name || !assetTypes.includes(type) || !classes.includes(classification))
    return fail(400, "invalid_asset");
  const id = body.id ? String(body.id) : "";
  let row = id ? data.assets.find((item) => item.id === id) : undefined;
  if (id && !row) return fail(404, "not_found");
  if (!row) {
    row = {
      id: crypto.randomUUID(),
      name,
      type,
      source: "manual",
      ownerName: "",
      ownerEmail: "",
      environment: "production",
      classification,
      inScope: true,
      lastSeen: new Date().toISOString(),
    };
    data.assets.unshift(row);
  }
  row.name = name;
  row.type = type;
  row.source = String(body.source ?? "manual");
  row.ownerName = String(body.ownerName ?? "");
  row.ownerEmail = String(body.ownerEmail ?? "");
  row.environment = String(body.environment ?? "production");
  row.classification = classification;
  row.inScope = Boolean(body.inScope);
  row.lastSeen = new Date().toISOString();
  onRecord("asset.saved", type);
  return json(row);
}

function importAssets(
  data: DemoWorld,
  actor: Actor,
  body: Record<string, unknown>,
  onRecord: (action: string, detail: string) => void,
) {
  if (!canWrite(actor)) return fail(403, "forbidden");
  const lines = String(body.csv ?? "")
    .split("\n")
    .map((line) => line.trim())
    .filter(Boolean);
  if (lines.length < 2) return fail(400, "invalid_asset");
  let added = 0;
  for (const line of lines.slice(1)) {
    const cells = line.split(",");
    if (cells.length < 6 || !assetTypes.includes(cells[1].trim())) continue;
    data.assets.unshift({
      id: crypto.randomUUID(),
      name: cells[0].trim(),
      type: cells[1].trim(),
      source: cells[2].trim(),
      ownerName: cells[3].trim(),
      ownerEmail: cells[4].trim(),
      environment: cells[5].trim() || "production",
      classification: classes.includes(cells[6]?.trim()) ? cells[6].trim() : "Internal",
      inScope: cells[7]?.trim().toLowerCase() !== "false",
      lastSeen: new Date().toISOString(),
    });
    added += 1;
  }
  onRecord("asset.imported", String(added));
  return json(added);
}

function exportAssets(data: DemoWorld) {
  const lines = ["name,type,source,owner,ownerEmail,environment,classification,inScope,lastSeen"];
  for (const row of data.assets) {
    lines.push(
      [
        row.name,
        row.type,
        row.source,
        row.ownerName,
        row.ownerEmail,
        row.environment,
        row.classification,
        row.inScope,
        row.lastSeen.slice(0, 10),
      ].join(","),
    );
  }
  return new Response(lines.join("\n"), {
    status: 200,
    headers: {
      "Content-Type": "text/csv",
      "Content-Disposition": 'attachment; filename="assets.csv"',
    },
  });
}

function saveTask(
  data: DemoWorld,
  actor: Actor,
  body: Record<string, unknown>,
  onRecord: (action: string, detail: string) => void,
) {
  if (!canWrite(actor)) return fail(403, "forbidden");
  const title = String(body.title ?? "").trim();
  const linkedKind = String(body.linkedKind ?? "");
  const status = String(body.status ?? "Open");
  const recurrence = String(body.recurrence ?? "None");
  if (
    !title ||
    !taskKinds.includes(linkedKind) ||
    !taskStatuses.includes(status) ||
    !recurrences.includes(recurrence)
  ) {
    return fail(400, "invalid_task");
  }
  const row = {
    id: body.id ? String(body.id) : crypto.randomUUID(),
    title,
    linkedKind,
    linkedCode: String(body.linkedCode ?? ""),
    ownerName: String(body.ownerName ?? actor.name),
    ownerEmail: String(body.ownerEmail ?? actor.email),
    dueOn: String(body.dueOn ?? today()),
    status,
    recurrence,
  };
  const index = data.workTasks.findIndex((item) => item.id === row.id);
  if (index >= 0) data.workTasks[index] = row;
  else data.workTasks.unshift(row);
  onRecord("task.saved", linkedKind);
  return json(taskItem(row));
}

function finishTask(
  data: DemoWorld,
  actor: Actor,
  id: string,
  onRecord: (action: string, detail: string) => void,
) {
  const row = data.workTasks.find((item) => item.id === id);
  if (!row) return fail(404, "not_found");
  if (!canWrite(actor) && row.ownerEmail.toLowerCase() !== actor.email.toLowerCase())
    return fail(403, "forbidden");
  row.status = "Done";
  onRecord("task.done", row.linkedKind);
  return json(taskItem(row));
}

function admin(actor: Actor) {
  return actor.role === "Admin";
}

function invite(
  data: DemoWorld,
  actor: Actor,
  body: Record<string, unknown>,
  onRecord: (action: string, detail: string) => void,
) {
  if (!admin(actor)) return fail(403, "forbidden");
  const email = String(body.email ?? "").trim();
  const name = String(body.name ?? "").trim();
  const role = String(body.role ?? "");
  const roles = ["Admin", "Editor", "Viewer", "Employee", "Auditor", "ControlOwner"];
  if (!name || !email.includes("@") || !roles.includes(role)) return fail(400, "invalid_member");
  let row = data.members.find((item) => item.email.toLowerCase() === email.toLowerCase());
  if (!row) {
    row = { id: crypto.randomUUID(), name, email, role, active: true };
    data.members.push(row);
  }
  row.name = name;
  row.role = role;
  row.active = true;
  onRecord("member.invited", role);
  return json(row);
}

function setRole(
  data: DemoWorld,
  actor: Actor,
  id: string,
  body: Record<string, unknown>,
  onRecord: (action: string, detail: string) => void,
) {
  if (!admin(actor)) return fail(403, "forbidden");
  const row = data.members.find((item) => item.id === id);
  const role = String(body.role ?? "");
  if (!row) return fail(404, "not_found");
  row.role = role;
  onRecord("member.role", role);
  return json(row);
}

function deactivate(
  data: DemoWorld,
  actor: Actor,
  id: string,
  onRecord: (action: string, detail: string) => void,
) {
  if (!admin(actor)) return fail(403, "forbidden");
  const row = data.members.find((item) => item.id === id);
  if (!row) return fail(404, "not_found");
  if (row.email.toLowerCase() === actor.email.toLowerCase()) return fail(400, "invalid_member");
  row.active = false;
  onRecord("member.deactivated", row.role);
  return json(row);
}

function saveGrant(
  data: DemoWorld,
  actor: Actor,
  body: Record<string, unknown>,
  onRecord: (action: string, detail: string) => void,
) {
  if (!admin(actor)) return fail(403, "forbidden");
  const email = String(body.email ?? "").trim();
  const member = data.members.find((item) => item.email.toLowerCase() === email.toLowerCase());
  const frameworks = Array.isArray(body.frameworks) ? body.frameworks.map(String) : [];
  if (!member || frameworks.length === 0) return fail(400, "invalid_grant");
  const row = {
    id: crypto.randomUUID(),
    email,
    name: member.name,
    auditName: String(body.auditName ?? ""),
    firmName: String(body.firmName ?? ""),
    periodStart: String(body.periodStart ?? ""),
    periodEnd: String(body.periodEnd ?? ""),
    accessExpiresOn: String(body.accessExpiresOn ?? ""),
    frameworks,
  };
  data.grants.push(row);
  onRecord("grant.saved", row.auditName);
  return json(row);
}

function adopt(
  data: DemoWorld,
  actor: Actor,
  code: string,
  onRecord: (action: string, detail: string) => void,
) {
  if (!canWrite(actor)) return fail(403, "forbidden");
  const entry = library.find((item) => item.code === code);
  if (!entry) return fail(404, "not_found");
  const existing = data.risks.find((item) => item.libraryCode === code || item.code === code);
  if (existing) return json(existing);
  const numbers = data.risks
    .map((item) => Number(item.code.replace("R-", "")))
    .filter((item) => !Number.isNaN(item));
  const next = `R-${String(Math.max(0, ...numbers) + 1).padStart(2, "0")}`;
  const due = new Date();
  due.setUTCDate(due.getUTCDate() + 30);
  const row = {
    code: next,
    title: entry.title,
    statement: entry.statement,
    likelihood: entry.likelihood,
    impact: entry.impact,
    score: entry.likelihood * entry.impact,
    band: "high",
    ownerName: actor.name,
    ownerEmail: actor.email,
    treatment: entry.treatment,
    treatmentPlan: "plan" in entry ? (entry.plan ?? "") : "",
    reviewOn: due.toISOString().slice(0, 10),
    residualLikelihood: Math.max(1, entry.likelihood - 1),
    residualImpact: Math.max(1, entry.impact - 1),
    residualScore: Math.max(1, entry.likelihood - 1) * Math.max(1, entry.impact - 1),
    residualBand: "medium",
    controlCodes: [],
    approved: false,
    approvedBy: "",
    approvedOn: null,
    libraryCode: code,
  };
  data.risks.push(row);
  onRecord("risk.saved", next);
  return json(row);
}

function setStatus(
  data: DemoWorld,
  actor: Actor,
  id: string,
  body: Record<string, unknown>,
  onRecord: (action: string, detail: string) => void,
) {
  if (!canWrite(actor)) return fail(403, "forbidden");
  const person = data.people.find((item) => item.id === id);
  const reason = String(body.reason ?? "").trim();
  const state = String(body.state ?? "");
  if (!person || !reason || !["Active", "Offboarding", "Offboarded"].includes(state))
    return fail(400, "invalid_status");
  person.state = state;
  person.statusReason = reason;
  onRecord("people.status", state);
  return json({ ...person, id: person.id });
}

function draft(
  data: DemoWorld,
  actor: Actor,
  body: Record<string, unknown>,
  onRecord: (action: string, detail: string) => void,
) {
  if (!canWrite(actor)) return fail(403, "forbidden");
  const name = String(body.name ?? "").trim();
  const dueOn = String(body.dueOn ?? "");
  if (!name || !dueOn) return fail(400, "invalid_campaign");
  const row = {
    id: crypto.randomUUID(),
    name,
    dueOn,
    state: "Draft",
    completedAt: null,
    evidenceId: null,
    providers: [] as string[],
    items: [],
  };
  data.campaigns.unshift(row);
  onRecord("access.drafted", name);
  return json({ ...row, evidenceSha256: null });
}

function pickApps(
  data: DemoWorld,
  actor: Actor,
  id: string,
  body: Record<string, unknown>,
  onRecord: (action: string, detail: string) => void,
) {
  if (!canWrite(actor)) return fail(403, "forbidden");
  const row = data.campaigns.find((item) => item.id === id);
  const providers = Array.isArray(body.providers) ? body.providers.map(String).filter(Boolean) : [];
  if (!row) return fail(404, "not_found");
  if (row.state !== "Draft") return fail(400, "campaign_closed");
  if (providers.length === 0) return fail(400, "apps_required");
  row.providers = providers;
  onRecord("access.apps", row.name);
  return json({ ...row, evidenceSha256: null });
}

function begin(
  data: DemoWorld,
  actor: Actor,
  id: string,
  onRecord: (action: string, detail: string) => void,
) {
  if (!canWrite(actor)) return fail(403, "forbidden");
  const row = data.campaigns.find((item) => item.id === id);
  if (!row) return fail(404, "not_found");
  if (row.state !== "Draft") return fail(400, "campaign_closed");
  const providers = row.providers ?? [];
  if (providers.length === 0) return fail(400, "apps_required");
  const items = data.people.flatMap((person) =>
    person.accounts
      .filter((account) => providers.includes(account.provider))
      .map((account) => ({
        id: crypto.randomUUID(),
        personName: person.name,
        personEmail: person.email,
        provider: account.provider,
        accountName: account.accountName,
        decision: "Pending",
        needsRemediation: false,
        remediationNote: "",
      })),
  );
  if (items.length === 0) return fail(400, "no_accounts");
  row.items = items;
  row.state = "Open";
  onRecord("access.started", row.name);
  return json({ ...row, evidenceSha256: null });
}

function remediate(
  data: DemoWorld,
  actor: Actor,
  path: string,
  body: Record<string, unknown>,
  onRecord: (action: string, detail: string) => void,
) {
  if (!canWrite(actor)) return fail(403, "forbidden");
  const rest = path.slice("/api/v1/access-reviews/".length);
  const [campaignId, , itemId] = rest.split("/");
  const note = String(body.note ?? "").trim();
  const row = data.campaigns.find((item) => item.id === campaignId);
  const item = row?.items.find((entry) => entry.id === itemId?.replace("/remediate", ""));
  if (!row || !item || !note)
    return fail(row ? 400 : 404, row ? "invalid_remediation" : "not_found");
  item.needsRemediation = true;
  item.remediationNote = note;
  if (
    row.items.every((entry) => entry.decision === "Approved" || entry.decision === "Revoked") &&
    row.items.some((entry) => entry.needsRemediation)
  ) {
    row.state = "Remediation";
  }
  onRecord("access.remediated", row.name);
  return json({ ...row, evidenceSha256: null });
}

export function assetCsv(data: DemoWorld) {
  const lines = ["name,type,source,owner,environment,classification,inScope,lastSeen"];
  for (const row of data.assets) {
    lines.push(
      [
        row.name,
        row.type,
        row.source,
        row.ownerName,
        row.environment,
        row.classification,
        String(row.inScope),
        row.lastSeen.slice(0, 10),
      ].join(","),
    );
  }
  return lines.join("\n");
}
