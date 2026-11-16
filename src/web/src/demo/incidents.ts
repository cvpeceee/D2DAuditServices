import type { DemoWorld } from "./catalog";

export type DemoIncident = {
  id: string;
  code: string;
  title: string;
  severity: string;
  status: string;
  description: string;
  reporterName: string;
  reporterEmail: string;
  reportedAt: string;
  slaDue: string;
  resolvedAt: string | null;
  rootCause: string;
  controlCode: string;
  evidenceId: string | null;
  withinSla: boolean;
  timeline: { kind: string; detail: string; authorName: string; at: string }[];
};

const store = new WeakMap<DemoWorld, DemoIncident[]>();

function day(date: Date) {
  return date.toISOString().slice(0, 10);
}

function shift(now: Date, days: number) {
  const next = new Date(now);
  next.setUTCDate(next.getUTCDate() + days);
  return next;
}

function within(status: string, due: string, resolved: string | null) {
  const today = day(new Date());
  if (status === "Resolved") return (resolved ?? today).slice(0, 10) <= due;
  return today <= due;
}

function row(
  code: string,
  title: string,
  severity: string,
  status: string,
  reported: Date,
  due: Date,
  resolved: Date | null,
  cause: string,
  name: string,
  email: string,
  control: string,
): DemoIncident {
  const timeline = [
    { kind: "Reported", detail: title, authorName: name, at: reported.toISOString() },
  ];
  if (resolved) {
    timeline.push({
      kind: "Resolved",
      detail: cause,
      authorName: name,
      at: resolved.toISOString(),
    });
  }
  const item: DemoIncident = {
    id: crypto.randomUUID(),
    code,
    title,
    severity,
    status,
    description: title,
    reporterName: name,
    reporterEmail: email,
    reportedAt: reported.toISOString(),
    slaDue: day(due),
    resolvedAt: resolved ? resolved.toISOString() : null,
    rootCause: cause,
    controlCode: control,
    evidenceId: null,
    withinSla: false,
    timeline,
  };
  item.withinSla = within(status, item.slaDue, item.resolvedAt);
  return item;
}

export function incidentsOf(data: DemoWorld) {
  const existing = store.get(data);
  if (existing) return existing;
  const now = new Date();
  const rows = [
    row(
      "INC-01",
      "Phishing message reported by success",
      "Low",
      "Resolved",
      shift(now, -12),
      shift(now, 2),
      shift(now, -11),
      "The message was quarantined. No mailbox was signed in.",
      "Jordan Pell",
      "jordan.pell@harborline.example",
      "IR-02",
    ),
    row(
      "INC-02",
      "Warehouse token used from a new network",
      "High",
      "Investigating",
      shift(now, -1),
      shift(now, 2),
      null,
      "",
      "Priya Nair",
      "priya.nair@harborline.example",
      "AC-07",
    ),
    row(
      "INC-03",
      "Backup job failed over a weekend",
      "Medium",
      "Resolved",
      shift(now, -20),
      shift(now, -13),
      shift(now, -8),
      "The restore target filled its disk. The job was rerun after the volume was extended.",
      "Marcus Lee",
      "marcus.lee@harborline.example",
      "BC-01",
    ),
  ];
  store.set(data, rows);
  const control = data.controls.find((item) => item.code === "IR-02");
  const test = control?.tests.find((item) => item.name === "Incidents resolved within SLA");
  if (control && test) {
    const late = rows.filter((item) => !item.withinSla).map((item) => item.code);
    test.outcome = late.length === 0 ? "pass" : "fail";
    test.detail =
      late.length === 0
        ? "Incidents are inside their SLA."
        : `Incidents missed the SLA: ${late.join(", ")}.`;
    if (late.length > 0) control.status = "failing";
  }
  return rows;
}

export function resetIncidents() {
  // A new demo world drops the previous map entries.
}

type Actor = { email: string; name: string; role: string };

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

function canWrite(actor: Actor) {
  return actor.role === "Admin" || actor.role === "ControlOwner" || actor.role === "Editor";
}

export function handleIncidents(
  method: string,
  path: string,
  body: Record<string, unknown>,
  data: DemoWorld,
  actor: Actor,
) {
  const rows = incidentsOf(data);
  if (path === "/api/v1/incidents" && method === "GET") return json(rows);
  if (path === "/api/v1/incidents" && method === "POST") {
    const title = String(body.title ?? "").trim();
    const severity = String(body.severity ?? "");
    const description = String(body.description ?? "").trim();
    if (!title || !description || !["Critical", "High", "Medium", "Low"].includes(severity)) {
      return json({ code: "invalid_incident" }, 400);
    }
    const windows: Record<string, number> = { Critical: 1, High: 3, Medium: 7, Low: 14 };
    const days = windows[severity] ?? 7;
    const now = new Date();
    const due = new Date(now);
    due.setUTCDate(due.getUTCDate() + days);
    const number =
      rows.reduce((max, item) => Math.max(max, Number(item.code.replace("INC-", "")) || 0), 0) + 1;
    const item = row(
      `INC-${String(number).padStart(2, "0")}`,
      title,
      severity,
      "Open",
      now,
      due,
      null,
      "",
      actor.name,
      actor.email,
      "",
    );
    item.description = description;
    item.timeline[0].detail = description;
    rows.unshift(item);
    return json(item);
  }
  if (path.startsWith("/api/v1/incidents/") && method === "POST") {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const id = path.slice("/api/v1/incidents/".length);
    const item = rows.find((rowItem) => rowItem.id === id);
    if (!item) return json({ code: "not_found" }, 404);
    const status = String(body.status ?? "");
    if (status && status !== item.status) {
      item.status = status;
      item.resolvedAt = status === "Resolved" ? new Date().toISOString() : null;
      item.timeline.push({
        kind: "Status",
        detail: status,
        authorName: actor.name,
        at: new Date().toISOString(),
      });
    }
    if (typeof body.rootCause === "string") item.rootCause = body.rootCause;
    if (typeof body.controlCode === "string") item.controlCode = body.controlCode;
    const note = String(body.note ?? "").trim();
    if (note)
      item.timeline.push({
        kind: "Note",
        detail: note,
        authorName: actor.name,
        at: new Date().toISOString(),
      });
    item.withinSla = within(item.status, item.slaDue, item.resolvedAt);
    return json(item);
  }
  return null;
}
