import type { DemoWorld } from "./catalog";

export type DemoChange = {
  id: string;
  code: string;
  title: string;
  source: string;
  externalKey: string;
  repository: string;
  authorName: string;
  status: string;
  approverName: string;
  approvedAt: string | null;
  deployRef: string;
  deployedAt: string | null;
  openedAt: string;
  reviewed: boolean;
};

const store = new WeakMap<DemoWorld, DemoChange[]>();
const testName = "production changes are reviewed";

function shift(now: Date, days: number) {
  const next = new Date(now);
  next.setUTCDate(next.getUTCDate() + days);
  return next.toISOString();
}

function item(
  code: string,
  title: string,
  source: string,
  externalKey: string,
  repository: string,
  authorName: string,
  approverName: string,
  openedAt: string,
  approvedAt: string | null,
  deployedAt: string | null,
  deployRef: string,
): DemoChange {
  return {
    id: crypto.randomUUID(),
    code,
    title,
    source,
    externalKey,
    repository,
    authorName,
    status: deployedAt ? "Deployed" : "Open",
    approverName,
    approvedAt,
    deployRef,
    deployedAt,
    openedAt,
    reviewed: Boolean(approvedAt),
  };
}

export function changesOf(data: DemoWorld) {
  const existing = store.get(data);
  if (existing) return existing;
  const now = new Date();
  const rows = [
    item(
      "CHG-01",
      "Rotate the warehouse credential helper",
      "github",
      "harborline/warehouse#482",
      "harborline/warehouse",
      "Dev Patel",
      "Priya Nair",
      shift(now, -6),
      shift(now, -5),
      shift(now, -5),
      "a1b2c3d",
    ),
    item(
      "CHG-02",
      "Open a temporary rule on the staging network",
      "github",
      "harborline/warehouse#501",
      "harborline/warehouse",
      "Dev Patel",
      "",
      shift(now, -2),
      null,
      shift(now, -2),
      "e4f5a6b",
    ),
    item(
      "CHG-03",
      "Expand the backup window",
      "jira",
      "HL-214",
      "HL",
      "Marcus Lee",
      "",
      shift(now, -1),
      null,
      null,
      "",
    ),
  ];
  store.set(data, rows);
  refresh(data, rows);
  return rows;
}

function refresh(data: DemoWorld, rows: DemoChange[]) {
  const late = rows.filter((row) => row.deployedAt && !row.approvedAt).map((row) => row.code);
  const control = data.controls.find((entry) => entry.code === "CM-11");
  if (!control) return;
  let test = control.tests.find((entry) => entry.name === testName);
  if (!test) {
    test = {
      id: crypto.randomUUID(),
      name: testName,
      kind: "document",
      outcome: "pass",
      detail: "",
      remediation: null,
    };
    control.tests.push(test);
  }
  if (late.length > 0) {
    test.outcome = "fail";
    test.detail = `Deployed without approval: ${late.join(", ")}.`;
  } else {
    test.outcome = "pass";
    const deployed = rows.filter((row) => row.deployedAt).length;
    test.detail =
      deployed === 0
        ? "No production changes are on the log."
        : `${deployed} deployed changes have an approval.`;
  }
  const failing = control.tests.some((entry) => entry.outcome === "fail");
  control.status = failing ? "failing" : "passing";
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

function canWrite(actor: { role: string }) {
  return actor.role === "Admin" || actor.role === "ControlOwner" || actor.role === "Editor";
}

function statusOf(row: DemoChange) {
  if (row.status === "Rejected") return "Rejected";
  if (row.deployedAt) return "Deployed";
  if (row.approvedAt) return "Approved";
  return "Open";
}

export function handleChanges(
  method: string,
  path: string,
  body: Record<string, unknown>,
  data: DemoWorld,
  actor: { name: string; role: string },
) {
  const rows = changesOf(data);
  if (path === "/api/v1/changes" && method === "GET") return json(rows);
  if (path === "/api/v1/changes/sync" && method === "POST") {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    return json({ imported: 0 });
  }
  if (path === "/api/v1/changes" && method === "POST") {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const title = String(body.title ?? "").trim();
    const source = String(body.source ?? "").trim();
    const externalKey = String(body.externalKey ?? "").trim();
    const repository = String(body.repository ?? "").trim();
    if (!title || !["github", "jira", "manual"].includes(source)) {
      return json({ code: "invalid_change" }, 400);
    }
    const number =
      rows.reduce((max, row) => Math.max(max, Number(row.code.replace("CHG-", "")) || 0), 0) + 1;
    const created = item(
      `CHG-${String(number).padStart(2, "0")}`,
      title,
      source,
      externalKey,
      repository,
      actor.name,
      "",
      new Date().toISOString(),
      null,
      null,
      "",
    );
    rows.unshift(created);
    refresh(data, rows);
    return json(created);
  }
  if (path.startsWith("/api/v1/changes/") && method === "POST") {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const id = path.slice("/api/v1/changes/".length);
    const row = rows.find((entry) => entry.id === id);
    if (!row) return json({ code: "not_found" }, 404);
    const action = String(body.action ?? "");
    const deployRef = String(body.deployRef ?? "").trim();
    if (action === "approve" && !row.approvedAt) {
      row.approvedAt = new Date().toISOString();
      row.approverName = actor.name;
      row.reviewed = true;
    } else if (action === "deploy") {
      if (!deployRef) return json({ code: "invalid_change" }, 400);
      row.deployRef = deployRef;
      row.deployedAt = row.deployedAt ?? new Date().toISOString();
    } else if (action === "reject") {
      row.status = "Rejected";
    } else if (action !== "approve") {
      return json({ code: "invalid_change" }, 400);
    }
    if (action !== "reject") row.status = statusOf(row);
    refresh(data, rows);
    return json(row);
  }
  return null;
}
