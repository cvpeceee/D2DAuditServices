import type { DemoWorld } from "./catalog";
import { plainPdf } from "./pdf";

type Actor = { email: string; name: string; role: string };

type Point = {
  day: string;
  controlsPassing: number;
  controlsTotal: number;
  percent: number;
  failingTests: number;
  vulnCritical: number;
  vulnHigh: number;
  vulnMedium: number;
  vulnLow: number;
  openTasks: number;
  overdueItems: number;
  frameworks: { key: string; name: string; passing: number; total: number; percent: number }[];
};

type Note = {
  id: string;
  kind: string;
  title: string;
  body: string;
  href: string;
  at: string;
  target: string | null;
};

type Pref = { kind: string; inApp: boolean; email: boolean; slack: boolean };

type Schedule = {
  reportKey: string;
  frequency: string;
  format: string;
  recipients: string;
  enabled: boolean;
  nextRunOn: string | null;
  lastSentAt: string | null;
  lastDeliveryId: string | null;
};

type State = {
  read: Set<string>;
  prefs: Map<string, Pref[]>;
  schedules: Map<string, Schedule>;
  deliveries: number;
};

export const insightKinds = [
  "test_failing",
  "task_assigned",
  "policy_ack",
  "review_due",
  "auditor_request",
  "vendor_overdue",
] as const;

const reportKeys = ["board", "framework", "risk", "vendor", "personnel"] as const;
const scheduledKeys = ["board", "risk", "vendor", "personnel"];
const store = new WeakMap<DemoWorld, State>();
const day = 86_400_000;

class InsightError extends Error {
  constructor(
    public status: number,
    public code: string,
  ) {
    super(code);
  }
}

function today() {
  return new Date().toISOString().slice(0, 10);
}

function shift(iso: string, days: number) {
  return new Date(Date.parse(`${iso}T00:00:00Z`) + days * day).toISOString().slice(0, 10);
}

function pct(part: number, total: number) {
  return total === 0 ? 0 : Math.round((part * 100) / total);
}

/** Deterministic wobble in [-1, 1] so demo history is identical across reloads. */
function wobble(seed: number) {
  const value = Math.sin(seed * 12.9898) * 43758.5453;
  return (value - Math.floor(value)) * 2 - 1;
}

export function livePoint(data: DemoWorld, on = today()): Point {
  const passing = data.controls.filter((item) => item.status === "passing").length;
  const failingTests = data.controls
    .flatMap((item) => item.tests)
    .filter((test) => test.outcome === "fail").length;
  const openTasks = data.workTasks.filter((task) => !/done|complete/i.test(task.status)).length;
  const overdueItems =
    data.workTasks.filter((task) => !/done|complete/i.test(task.status) && task.dueOn < on).length +
    data.vendors.filter((vendor) => vendor.reviewDueOn < on).length +
    data.risks.filter((risk) => risk.reviewOn < on).length;
  return {
    day: on,
    controlsPassing: passing,
    controlsTotal: data.controls.length,
    percent: pct(passing, data.controls.length),
    failingTests,
    vulnCritical: 1,
    vulnHigh: 4,
    vulnMedium: 11,
    vulnLow: 19,
    openTasks,
    overdueItems,
    frameworks: data.frameworks.map((framework) => {
      const mapped = data.controls.filter((control) =>
        control.mappings.some((mapping) => mapping.framework === framework.key),
      );
      const ok = mapped.filter((control) => control.status === "passing").length;
      return {
        key: framework.key,
        name: framework.name,
        passing: ok,
        total: mapped.length,
        percent: pct(ok, mapped.length),
      };
    }),
  };
}

/** Ninety days of Harborline posture that climbs steadily toward today's live numbers. */
export function demoHistory(data: DemoWorld, days = 90, end = today()): Point[] {
  const live = livePoint(data, end);
  const points: Point[] = [];
  for (let back = days - 1; back >= 1; back -= 1) {
    const lag = back / days;
    const scale = (value: number, drop: number) =>
      Math.max(0, Math.round(value * (1 - drop * lag) + wobble(back) * value * 0.02));
    const grow = (value: number, rise: number) =>
      Math.max(0, Math.round(value + rise * lag + wobble(back + 7) * 1.2));
    const passing = Math.min(live.controlsTotal, scale(live.controlsPassing, 0.35));
    points.push({
      day: shift(end, -back),
      controlsPassing: passing,
      controlsTotal: live.controlsTotal,
      percent: pct(passing, live.controlsTotal),
      failingTests: grow(live.failingTests, 9),
      vulnCritical: grow(live.vulnCritical, 4),
      vulnHigh: grow(live.vulnHigh, 9),
      vulnMedium: grow(live.vulnMedium, 14),
      vulnLow: grow(live.vulnLow, 6),
      openTasks: grow(live.openTasks, 12),
      overdueItems: grow(live.overdueItems, 7),
      frameworks: live.frameworks.map((item, index) => {
        const ok = Math.min(
          item.total,
          Math.max(
            0,
            Math.round(
              item.passing * (1 - (0.3 + index * 0.08) * lag) +
                wobble(back * (index + 3)) * item.total * 0.015,
            ),
          ),
        );
        return { ...item, passing: ok, percent: pct(ok, item.total) };
      }),
    });
  }
  points.push(live);
  return points;
}

function state(data: DemoWorld): State {
  let current = store.get(data);
  if (!current) {
    current = {
      read: new Set(),
      prefs: new Map(),
      schedules: new Map(),
      deliveries: 0,
    };
    store.set(data, current);
  }
  return current;
}

function soc2Codes(data: DemoWorld) {
  return new Set(
    data.controls
      .filter((control) => control.mappings.some((mapping) => mapping.framework === "soc2"))
      .map((control) => control.code),
  );
}

export function demoNotifications(data: DemoWorld): Note[] {
  const now = Date.now();
  const at = (hours: number) => new Date(now - hours * 3_600_000).toISOString();
  const on = today();
  const notes: Note[] = [];
  data.controls.forEach((control) =>
    control.tests
      .filter((test) => test.outcome === "fail")
      .forEach((test, index) =>
        notes.push({
          id: `test-${test.id}`,
          kind: "test_failing",
          title: `Test started failing: ${test.name}`,
          body: `${control.code} ${control.title}`,
          href: `/controls/${encodeURIComponent(control.code)}`,
          at: at(2 + index * 5),
          target: null,
        }),
      ),
  );
  data.workTasks
    .filter((task) => !/done|complete/i.test(task.status))
    .forEach((task, index) =>
      notes.push({
        id: `task-${task.id}`,
        kind: "task_assigned",
        title: `Task assigned to you: ${task.title}`,
        body: `Due ${task.dueOn}`,
        href: "/tasks",
        at: at(6 + index * 9),
        target: task.ownerEmail.toLowerCase(),
      }),
    );
  data.policies.forEach((policy) =>
    policy.acknowledgements
      .filter((item) => !item.acknowledgedAt)
      .forEach((item) =>
        notes.push({
          id: `ack-${policy.code}-${item.personEmail}`,
          kind: "policy_ack",
          title: `Policy needs acknowledgment: ${policy.title}`,
          body: `Acknowledge by ${item.dueOn}`,
          href: "/me",
          at: at(20),
          target: item.personEmail.toLowerCase(),
        }),
      ),
  );
  data.risks
    .filter((risk) => risk.reviewOn <= shift(on, 14))
    .forEach((risk) =>
      notes.push({
        id: `review-${risk.code}`,
        kind: "review_due",
        title: `Risk review due: ${risk.title}`,
        body: `${risk.code} review on ${risk.reviewOn}`,
        href: "/risks",
        at: at(30),
        target: risk.ownerEmail.toLowerCase(),
      }),
    );
  data.requests
    .filter((request) => request.status === "Open")
    .forEach((request, index) =>
      notes.push({
        id: `request-${request.code}`,
        kind: "auditor_request",
        title: `Auditor request: ${request.title}`,
        body: `${request.code} due ${request.dueOn}`,
        href: "/auditor",
        at: at(12 + index * 4),
        target: null,
      }),
    );
  data.vendors
    .filter((vendor) => vendor.reviewDueOn < on)
    .forEach((vendor) =>
      notes.push({
        id: `vendor-${vendor.id}`,
        kind: "vendor_overdue",
        title: `Vendor review overdue: ${vendor.name}`,
        body: `Review was due ${vendor.reviewDueOn}`,
        href: "/vendors",
        at: at(40),
        target: null,
      }),
    );
  return notes.sort((a, b) => b.at.localeCompare(a.at));
}

function prefsFor(current: State, email: string): Pref[] {
  const saved = current.prefs.get(email);
  return insightKinds.map(
    (kind) =>
      saved?.find((item) => item.kind === kind) ?? {
        kind,
        inApp: true,
        email: kind !== "test_failing",
        slack: false,
      },
  );
}

function feedFor(data: DemoWorld, actor: Actor) {
  const current = state(data);
  const email = actor.email.toLowerCase();
  const prefs = prefsFor(current, email);
  const items = demoNotifications(data)
    .filter((note) => (note.target === null ? actor.role !== "Auditor" : note.target === email))
    .filter((note) => prefs.find((pref) => pref.kind === note.kind)?.inApp !== false)
    .map((note) => ({
      id: note.id,
      kind: note.kind,
      title: note.title,
      body: note.body,
      href: note.href,
      at: note.at,
      read: current.read.has(`${email}|${note.id}`),
    }));
  return { unread: items.filter((item) => !item.read).length, items };
}

type Entry = { kind: string; key: string; title: string; subtitle: string; href: string };

function searchEntries(data: DemoWorld, actor: Actor): Entry[] {
  const auditor = actor.role === "Auditor";
  const employee = actor.role === "Employee";
  const codes = auditor ? soc2Codes(data) : null;
  const allowControl = (code: string) => !codes || codes.has(code);
  const entries: Entry[] = [];
  data.frameworks
    .filter((framework) => !auditor || framework.key === "soc2")
    .forEach((framework) =>
      entries.push({
        kind: "framework",
        key: framework.key,
        title: framework.name,
        subtitle: framework.version,
        href: `/frameworks/${framework.key}`,
      }),
    );
  data.policies
    .filter((policy) => !auditor || policy.controlCodes.some(allowControl))
    .forEach((policy) =>
      entries.push({
        kind: "policy",
        key: policy.code,
        title: policy.title,
        subtitle: policy.code,
        href: "/policies",
      }),
    );
  if (employee) return entries;
  data.controls
    .filter((control) => allowControl(control.code))
    .forEach((control) => {
      entries.push({
        kind: "control",
        key: control.code,
        title: control.title,
        subtitle: control.code,
        href: `/controls/${encodeURIComponent(control.code)}`,
      });
      control.tests.forEach((test) =>
        entries.push({
          kind: "test",
          key: test.id,
          title: test.name,
          subtitle: control.code,
          href: "/tests",
        }),
      );
    });
  data.evidence
    .filter((item) => allowControl(item.controlCode))
    .slice(0, 2000)
    .forEach((item) =>
      entries.push({
        kind: "evidence",
        key: item.id,
        title: item.title,
        subtitle: item.controlCode,
        href: "/evidence",
      }),
    );
  if (auditor) return entries;
  data.risks.forEach((risk) =>
    entries.push({
      kind: "risk",
      key: risk.code,
      title: risk.title,
      subtitle: risk.code,
      href: "/risks",
    }),
  );
  data.vendors.forEach((vendor) =>
    entries.push({
      kind: "vendor",
      key: vendor.id,
      title: vendor.name,
      subtitle: vendor.service,
      href: "/vendors",
    }),
  );
  data.people.forEach((person) =>
    entries.push({
      kind: "person",
      key: person.id,
      title: person.name,
      subtitle: person.email,
      href: `/people?person=${encodeURIComponent(person.id)}`,
    }),
  );
  return entries;
}

export function demoSearch(data: DemoWorld, actor: Actor, query: string) {
  const q = query.trim().toLowerCase();
  if (q.length < 2) return [];
  return searchEntries(data, actor)
    .map((entry) => {
      const key = entry.key.toLowerCase();
      const title = entry.title.toLowerCase();
      const score =
        key === q
          ? 100
          : title.startsWith(q) || key.startsWith(q)
            ? 50
            : title.includes(q)
              ? 25
              : entry.subtitle.toLowerCase().includes(q)
                ? 10
                : 0;
      return { entry, score };
    })
    .filter((item) => item.score > 0)
    .sort((a, b) => b.score - a.score || a.entry.title.localeCompare(b.entry.title))
    .slice(0, 40)
    .map((item) => item.entry);
}

function cell(value: string | number) {
  const text = String(value);
  const safe = /^[=+\-@\t\r]/.test(text) ? `'${text}` : text;
  return /[",\n\r]/.test(safe) ? `"${safe.replaceAll('"', '""')}"` : safe;
}

function reportRows(data: DemoWorld, key: string, framework: string | null) {
  const live = livePoint(data);
  switch (key) {
    case "board":
    case "framework": {
      const frameworks = live.frameworks.filter(
        (item) => key === "board" || !framework || item.key === framework,
      );
      const rows: (string | number)[][] = [
        ["Framework", "Controls passing", "Controls total", "Readiness %"],
        ...frameworks.map((item) => [item.name, item.passing, item.total, item.percent]),
      ];
      if (key === "board") {
        rows.push(
          [],
          ["Metric", "Value"],
          ["Overall readiness %", live.percent],
          ["Failing tests", live.failingTests],
          ["Open critical/high vulnerabilities", live.vulnCritical + live.vulnHigh],
          ["Open tasks", live.openTasks],
          ["Overdue items", live.overdueItems],
        );
      } else {
        rows.push([], ["Control", "Title", "Status", "Owner"]);
        data.controls
          .filter((control) =>
            control.mappings.some((mapping) => !framework || mapping.framework === framework),
          )
          .forEach((control) =>
            rows.push([control.code, control.title, control.status, control.ownerName]),
          );
      }
      return {
        title: key === "board" ? "Board and executive report" : "Framework readiness report",
        rows,
      };
    }
    case "risk":
      return {
        title: "Risk report",
        rows: [
          ["Code", "Title", "Inherent", "Residual", "Treatment", "Owner", "Review on"],
          ...data.risks.map((risk) => [
            risk.code,
            risk.title,
            risk.score,
            risk.residualScore,
            risk.treatment,
            risk.ownerName,
            risk.reviewOn,
          ]),
        ],
      };
    case "vendor":
      return {
        title: "Vendor report",
        rows: [
          ["Vendor", "Service", "Criticality", "Data access", "Review state", "Review due"],
          ...data.vendors.map((vendor) => [
            vendor.name,
            vendor.service,
            vendor.criticality,
            vendor.dataAccess,
            vendor.reviewState,
            vendor.reviewDueOn,
          ]),
        ],
      };
    default:
      return {
        title: "Personnel compliance report",
        rows: [
          ["Name", "Email", "Department", "State", "Training", "Training due"],
          ...data.people.map((person) => [
            person.name,
            person.email,
            person.department,
            person.state,
            person.training,
            person.trainingDueOn ?? "",
          ]),
        ],
      };
  }
}

function nextRun(frequency: string, from = today()) {
  if (frequency === "Daily") return shift(from, 1);
  if (frequency === "Weekly") return shift(from, 7);
  const date = new Date(`${from}T00:00:00Z`);
  date.setUTCMonth(date.getUTCMonth() + 1, 1);
  return date.toISOString().slice(0, 10);
}

function schedulesOf(current: State): Schedule[] {
  return scheduledKeys.map(
    (key) =>
      current.schedules.get(key) ?? {
        reportKey: key,
        frequency: "Monthly",
        format: "pdf",
        recipients: "",
        enabled: false,
        nextRunOn: null,
        lastSentAt: null,
        lastDeliveryId: null,
      },
  );
}

function recipients(value: string) {
  const list = value
    .split(/[,;\s]+/)
    .map((item) => item.trim())
    .filter(Boolean);
  if (list.length > 20 || list.some((item) => !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(item)))
    return null;
  return list;
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function admin(actor: Actor) {
  if (actor.role !== "Admin") throw new InsightError(403, "forbidden");
}

/** Public: confirms a password reset link. Demo accounts keep the example password. */
export function handleDemoReset(method: string, path: string, body: Record<string, unknown>) {
  if (path !== "/api/v1/auth/reset" || method !== "POST") return null;
  const token = typeof body.token === "string" ? body.token : "";
  const password = typeof body.password === "string" ? body.password : "";
  if (!token || token.length > 2048 || token === "expired")
    return json({ code: "invalid_token" }, 400);
  if (password.length < 12 || password.length > 200) return json({ code: "weak_password" }, 400);
  return json({ result: "reset" });
}

export function handleInsights(
  method: string,
  path: string,
  query: URLSearchParams,
  body: Record<string, unknown>,
  data: DemoWorld,
  actor: Actor,
): Response | null {
  try {
    return route(method, path, query, body, data, actor);
  } catch (error) {
    if (error instanceof InsightError) return json({ code: error.code }, error.status);
    throw error;
  }
}

function route(
  method: string,
  path: string,
  query: URLSearchParams,
  body: Record<string, unknown>,
  data: DemoWorld,
  actor: Actor,
): Response | null {
  const current = state(data);
  if (path === "/api/v1/insights/trends" && method === "GET") {
    const days = Math.min(365, Math.max(7, Number(query.get("days")) || 90));
    return json({ days, points: demoHistory(data, days) });
  }
  if (path === "/api/v1/insights/snapshot" && method === "POST") {
    if (actor.role === "Auditor") throw new InsightError(403, "forbidden");
    return json(livePoint(data));
  }
  if (path === "/api/v1/notifications/feed" && method === "GET") {
    const take = Math.min(100, Math.max(1, Number(query.get("take")) || 30));
    const feed = feedFor(data, actor);
    return json({ unread: feed.unread, items: feed.items.slice(0, take) });
  }
  if (path === "/api/v1/notifications/unread-count" && method === "GET") {
    return json({ unread: feedFor(data, actor).unread });
  }
  if (path === "/api/v1/notifications/read-all" && method === "POST") {
    const email = actor.email.toLowerCase();
    feedFor(data, actor).items.forEach((item) => current.read.add(`${email}|${item.id}`));
    window.dispatchEvent(new Event("d2d-notification"));
    return new Response(null, { status: 204 });
  }
  const readMatch = /^\/api\/v1\/notifications\/([^/]+)\/read$/.exec(path);
  if (readMatch && method === "POST") {
    const id = decodeURIComponent(readMatch[1]);
    if (!feedFor(data, actor).items.some((item) => item.id === id))
      throw new InsightError(404, "not_found");
    current.read.add(`${actor.email.toLowerCase()}|${id}`);
    return new Response(null, { status: 204 });
  }
  if (path === "/api/v1/notifications/preferences") {
    const email = actor.email.toLowerCase();
    if (method === "GET") return json(prefsFor(current, email));
    if (method === "PUT") {
      const incoming = Array.isArray(body.preferences) ? (body.preferences as Pref[]) : [];
      const merged = prefsFor(current, email).map((pref) => {
        const next = incoming.find((item) => item?.kind === pref.kind);
        return next
          ? {
              kind: pref.kind,
              inApp: Boolean(next.inApp),
              email: Boolean(next.email),
              slack: Boolean(next.slack),
            }
          : pref;
      });
      current.prefs.set(email, merged);
      return json(merged);
    }
  }
  if (path === "/api/v1/notifications/digest" && method === "POST") {
    admin(actor);
    current.deliveries += 1;
    const notes = demoNotifications(data);
    return json({
      recipients: 5,
      notifications: notes.length,
      deliveryIds: [`email:demo-digest-${current.deliveries}`],
    });
  }
  if (path === "/api/v1/search" && method === "GET") {
    const q = (query.get("q") ?? "").slice(0, 100);
    return json({ query: q, results: demoSearch(data, actor, q) });
  }
  if (path === "/api/v1/reports/schedules" && method === "GET") {
    admin(actor);
    return json(schedulesOf(current));
  }
  if (path === "/api/v1/reports/schedules" && method === "PUT") {
    admin(actor);
    const key = String(body.reportKey ?? "");
    const frequency = String(body.frequency ?? "");
    const format = String(body.format ?? "");
    const list = recipients(String(body.recipients ?? ""));
    if (
      !scheduledKeys.includes(key) ||
      !["Daily", "Weekly", "Monthly"].includes(frequency) ||
      !["csv", "pdf"].includes(format) ||
      !list
    )
      throw new InsightError(400, "invalid_schedule");
    const enabled = Boolean(body.enabled);
    const previous = schedulesOf(current).find((item) => item.reportKey === key);
    const saved: Schedule = {
      reportKey: key,
      frequency,
      format,
      recipients: list.join(", "),
      enabled,
      nextRunOn: enabled ? nextRun(frequency) : null,
      lastSentAt: previous?.lastSentAt ?? null,
      lastDeliveryId: previous?.lastDeliveryId ?? null,
    };
    current.schedules.set(key, saved);
    return json(saved);
  }
  const sendMatch = /^\/api\/v1\/reports\/schedules\/([a-z]+)\/send$/.exec(path);
  if (sendMatch && method === "POST") {
    admin(actor);
    const schedule = schedulesOf(current).find((item) => item.reportKey === sendMatch[1]);
    if (!schedule || !recipients(schedule.recipients)?.length)
      throw new InsightError(400, "invalid_schedule");
    current.deliveries += 1;
    const sentAt = new Date().toISOString();
    const deliveryId = `email:demo-report-${current.deliveries}`;
    current.schedules.set(schedule.reportKey, {
      ...schedule,
      lastSentAt: sentAt,
      lastDeliveryId: deliveryId,
    });
    return json({ reportKey: schedule.reportKey, deliveryId, sentAt });
  }
  const reportMatch = /^\/api\/v1\/reports\/([a-z]+)$/.exec(path);
  if (reportMatch && method === "GET" && reportKeys.includes(reportMatch[1] as never)) {
    if (actor.role === "Auditor" || actor.role === "Employee")
      throw new InsightError(403, "forbidden");
    const format = query.get("format") ?? "pdf";
    if (format !== "csv" && format !== "pdf") throw new InsightError(400, "invalid_format");
    const key = reportMatch[1];
    const report = reportRows(data, key, query.get("framework"));
    const name = `${key}-report-${today()}`;
    if (format === "csv") {
      const csv = report.rows.map((row) => row.map(cell).join(",")).join("\r\n") + "\r\n";
      return new Response(csv, {
        status: 200,
        headers: {
          "Content-Type": "text/csv",
          "Content-Disposition": `attachment; filename="${name}.csv"`,
        },
      });
    }
    const lines = [
      `D2D Audit Services - ${report.title}`,
      `Generated ${today()} (example data)`,
      "",
      ...report.rows.map((row) => row.join("  ")),
    ];
    const bytes = plainPdf(lines);
    const copy = new ArrayBuffer(bytes.byteLength);
    new Uint8Array(copy).set(bytes);
    return new Response(copy, {
      status: 200,
      headers: {
        "Content-Type": "application/pdf",
        "Content-Disposition": `attachment; filename="${name}.pdf"`,
      },
    });
  }
  return null;
}
