import type { DemoWorld } from "./catalog";

type Service = {
  id: string;
  name: string;
  criticality: string;
  rtoHours: number;
  rpoHours: number;
  ownerName: string;
  controlCode: string;
  notes: string;
};
type Plan = {
  id: string;
  name: string;
  kind: string;
  body: string;
  controlCode: string;
  updatedAt: string;
};
type Exercise = {
  id: string;
  name: string;
  planId: string | null;
  result: string;
  notes: string;
  controlCode: string;
  ranAt: string;
};
type Board = { services: Service[]; plans: Plan[]; exercises: Exercise[] };

const store = new WeakMap<DemoWorld, Board>();
const testName = "Recovery exercises are recorded";

function shift(days: number) {
  const next = new Date();
  next.setUTCDate(next.getUTCDate() + days);
  return next.toISOString();
}

function refresh(data: DemoWorld, board: Board) {
  const failed = board.exercises.filter((item) => item.result !== "Pass").map((item) => item.name);
  const control = data.controls.find((item) => item.code === "BC-01");
  if (!control) return;
  let test = control.tests.find((item) => item.name === testName);
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
  if (board.exercises.length === 0) {
    test.outcome = "fail";
    test.detail = "No recovery exercise is on the log.";
  } else if (failed.length > 0) {
    test.outcome = "fail";
    test.detail = `Exercises failed: ${failed.join(", ")}.`;
  } else {
    test.outcome = "pass";
    test.detail = `${board.exercises.length} recovery exercises passed.`;
  }
  if (control.tests.some((item) => item.outcome === "fail")) control.status = "failing";
}

export function continuityOf(data: DemoWorld) {
  const existing = store.get(data);
  if (existing) return existing;
  const continuity = crypto.randomUUID();
  const recovery = crypto.randomUUID();
  const board: Board = {
    services: [
      {
        id: crypto.randomUUID(),
        name: "Customer warehouse",
        criticality: "Critical",
        rtoHours: 4,
        rpoHours: 1,
        ownerName: "Marcus Lee",
        controlCode: "BC-01",
        notes: "Query and load must return inside four hours.",
      },
      {
        id: crypto.randomUUID(),
        name: "Trust center",
        criticality: "Low",
        rtoHours: 24,
        rpoHours: 24,
        ownerName: "Chandra Vell",
        controlCode: "BC-01",
        notes: "The public page can wait a day.",
      },
    ],
    plans: [
      {
        id: continuity,
        name: "Warehouse continuity plan",
        kind: "Continuity",
        body: "Keep the warehouse read-only, restore the latest snapshot, and tell customers when query is back.",
        controlCode: "BC-01",
        updatedAt: shift(-40),
      },
      {
        id: recovery,
        name: "Region failover plan",
        kind: "DisasterRecovery",
        body: "Fail the warehouse to the second region and confirm the restore point is inside one hour.",
        controlCode: "BC-01",
        updatedAt: shift(-20),
      },
    ],
    exercises: [
      {
        id: crypto.randomUUID(),
        name: "Quarterly warehouse restore",
        planId: continuity,
        result: "Pass",
        notes: "The snapshot restored and a sample query matched.",
        controlCode: "BC-01",
        ranAt: shift(-20),
      },
      {
        id: crypto.randomUUID(),
        name: "Region failover drill",
        planId: recovery,
        result: "Fail",
        notes: "The second region came up after the four-hour RTO.",
        controlCode: "BC-01",
        ranAt: shift(-12),
      },
    ],
  };
  store.set(data, board);
  refresh(data, board);
  return board;
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

export function handleContinuity(
  method: string,
  path: string,
  body: Record<string, unknown>,
  data: DemoWorld,
  actor: { role: string },
) {
  const board = continuityOf(data);
  if (path === "/api/v1/continuity" && method === "GET") return json(board);
  if (!path.startsWith("/api/v1/continuity/") || method !== "POST") return null;
  if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
  if (path.endsWith("/services")) {
    const name = String(body.name ?? "").trim();
    if (!name) return json({ code: "invalid_continuity" }, 400);
    const row: Service = {
      id: crypto.randomUUID(),
      name,
      criticality: String(body.criticality ?? "Medium"),
      rtoHours: Number(body.rtoHours ?? 0),
      rpoHours: Number(body.rpoHours ?? 0),
      ownerName: String(body.ownerName ?? ""),
      controlCode: String(body.controlCode ?? "BC-01"),
      notes: String(body.notes ?? ""),
    };
    board.services.push(row);
    return json(row);
  }
  if (path.endsWith("/plans")) {
    const name = String(body.name ?? "").trim();
    const text = String(body.body ?? "").trim();
    if (!name || !text) return json({ code: "invalid_continuity" }, 400);
    const row: Plan = {
      id: crypto.randomUUID(),
      name,
      kind: String(body.kind ?? "Continuity"),
      body: text,
      controlCode: String(body.controlCode ?? "BC-01"),
      updatedAt: new Date().toISOString(),
    };
    board.plans.push(row);
    return json(row);
  }
  if (path.endsWith("/exercises")) {
    const name = String(body.name ?? "").trim();
    const result = String(body.result ?? "");
    if (!name || (result !== "Pass" && result !== "Fail"))
      return json({ code: "invalid_continuity" }, 400);
    const row: Exercise = {
      id: crypto.randomUUID(),
      name,
      planId: body.planId ? String(body.planId) : null,
      result,
      notes: String(body.notes ?? ""),
      controlCode: String(body.controlCode ?? "BC-01"),
      ranAt: new Date().toISOString(),
    };
    board.exercises.unshift(row);
    refresh(data, board);
    return json(row);
  }
  return null;
}
