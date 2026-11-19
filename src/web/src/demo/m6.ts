import type { DemoControl, DemoEvidence, DemoPerson, DemoWorld } from "./catalog";
import { company, seedDevices } from "./client";

export const auditPeriod = { start: "2026-01-01", end: "2026-12-15" };

type Actor = { email: string; name: string; role: string };

export type DemoComputer = {
  id: string;
  serial: string;
  operatingSystem: string;
  ownerName: string;
  ownerEmail: string;
  encrypted: boolean;
  screenLock: boolean;
  antivirus: boolean;
  passwordManager: boolean;
  autoUpdate: boolean;
  lastCheckIn: string;
  source: string;
  screenshotName: string | null;
  evidenceId: string | null;
};

export type DemoVulnerability = {
  id: string;
  cve: string;
  title: string;
  severity: string;
  assetName: string;
  source: string;
  firstSeen: string;
  lastSeen: string;
  slaDue: string;
  fixAvailable: boolean;
  ignored: boolean;
  ignoreReason: string;
};

export type DemoTrustDocument = {
  id: string;
  title: string;
  summary: string;
  access: string;
  fileName: string;
  updatedOn: string;
  hasFile?: boolean;
  sha256?: string;
  contentBase64?: string;
  contentType?: string;
};

export type DemoTrustRequest = {
  id: string;
  documentId: string | null;
  documentTitle: string;
  requesterName: string;
  requesterEmail: string;
  company: string;
  purpose: string;
  status: string;
  requestedAt: string;
  decidedAt: string | null;
  ndaAccepted?: boolean;
  ndaAcceptedAt?: string | null;
  accessToken?: string;
};

export type M6State = {
  computers: DemoComputer[];
  vulnerabilities: DemoVulnerability[];
  sla: { criticalDays: number; highDays: number; mediumDays: number; lowDays: number };
  history: {
    cve: string;
    assetName: string;
    eventKind: string;
    dueOn: string;
    detail: string;
    at: string;
  }[];
  trust: {
    published: boolean;
    headline: string;
    summary: string;
    contactEmail: string;
    documents: DemoTrustDocument[];
    requests: DemoTrustRequest[];
  };
  rules: {
    id: string;
    name: string;
    trigger: string;
    action: string;
    target: string;
    enabled: boolean;
  }[];
  deliveries: {
    id: string;
    trigger: string;
    action: string;
    target: string;
    summary: string;
    externalId: string;
    at: string;
  }[];
};

const triggers = ["TestFailed", "ControlNotReady", "RiskCreated", "ReviewDue"];
const actions = ["Jira", "Slack", "Teams", "Email"];

function day(date: Date) {
  return date.toISOString().slice(0, 10);
}

function shift(now: Date, days: number) {
  const next = new Date(now);
  next.setUTCDate(next.getUTCDate() + days);
  return next;
}

function os(platform: string) {
  if (platform === "Windows") return "Windows 11 24H2";
  if (platform === "Linux") return "Ubuntu 24.04";
  return "macOS 15.1";
}

export function inAuditPeriod(collectedAt: string | null | undefined) {
  if (!collectedAt) return false;
  const period = currentPeriod();
  const value = collectedAt.slice(0, 10);
  return value >= period.start && value <= period.end;
}

export async function buildM6(controls: DemoControl[], evidence: DemoEvidence[], now: Date) {
  refreshBackground(controls, []);
  const oldBody = `${company.name} access sample from the prior year.`;
  const hash = await sha256(oldBody);
  evidence.push({
    id: crypto.randomUUID(),
    title: "Prior-year access sample",
    controlCode: "AC-07",
    source: "Manual",
    collectedAt: "2025-11-02T00:00:00.000Z",
    integrity: "verified",
    sha256: hash,
    lockedUntil: "2028-11-02T00:00:00.000Z",
    body: oldBody,
  });

  const computers: DemoComputer[] = seedDevices.map((device, index) => {
    const miss = index % 7;
    return {
      id: crypto.randomUUID(),
      serial: device.serial,
      operatingSystem: os(device.platform),
      ownerName: device.ownerName,
      ownerEmail: device.ownerEmail,
      encrypted: miss !== 1,
      screenLock: miss !== 2,
      antivirus: miss !== 3,
      passwordManager: miss !== 4,
      autoUpdate: miss !== 5,
      lastCheckIn: shift(now, device.seenOffset).toISOString(),
      source: "Mdm",
      screenshotName: null,
      evidenceId: null,
    };
  });
  computers.push(
    computer(
      "HL-MAN-1044",
      "macOS 15.1",
      "Chandra Vell",
      `chandra.vell@${company.domain}`,
      true,
      shift(now, 0),
    ),
    computer(
      "HL-MAN-2201",
      "Windows 11 24H2",
      "Lena Cho",
      `lena.cho@${company.domain}`,
      false,
      shift(now, -2),
    ),
  );
  computers.push({
    id: crypto.randomUUID(),
    serial: "HL-AGT-1001",
    operatingSystem: "macOS 15.1",
    ownerName: "Dev Patel",
    ownerEmail: `dev.patel@${company.domain}`,
    encrypted: true,
    screenLock: false,
    antivirus: true,
    passwordManager: true,
    autoUpdate: true,
    lastCheckIn: shift(now, 0).toISOString(),
    source: "Agent",
    screenshotName: null,
    evidenceId: null,
  });

  const findings: DemoVulnerability[] = [
    finding(
      "CVE-2024-3094",
      "Build tool package accepts a malicious upstream",
      "Critical",
      "build-runner",
      "GitHub Dependabot",
      -20,
      true,
      false,
      "",
    ),
    finding(
      "CVE-2024-5535",
      "OpenSSL handshake accepts an empty extension",
      "Medium",
      "edge-gateway",
      "Pentest",
      -8,
      false,
      false,
      "",
    ),
    finding(
      "CVE-2023-38545",
      "HTTP client overflow in a warehouse job",
      "High",
      "warehouse-api",
      "AWS Inspector",
      -12,
      true,
      false,
      "",
    ),
    finding(
      "CVE-2024-21626",
      "Container runtime escape on a worker image",
      "Critical",
      "worker-image",
      "AWS Inspector",
      -3,
      true,
      false,
      "",
    ),
    finding(
      "CVE-2024-6387",
      "Secure shell race in a bastion image",
      "High",
      "bastion",
      "AWS Inspector",
      -40,
      true,
      false,
      "",
    ),
    finding(
      "CVE-2023-44487",
      "Request flood in the public gateway",
      "Medium",
      "edge-gateway",
      "AWS Inspector",
      -8,
      false,
      false,
      "",
    ),
    finding(
      "CVE-2024-3094",
      "Build tool package accepts a malicious upstream",
      "Critical",
      "analytics-ci",
      "GitHub Dependabot",
      -6,
      true,
      false,
      "",
    ),
    finding(
      "CVE-2022-41741",
      "Media module flaw in an internal player",
      "Low",
      "support-portal",
      "GitHub Dependabot",
      -15,
      true,
      true,
      "The player is not reachable from the internet and the fix is scheduled.",
    ),
  ].map((item) => ({ ...item, id: crypto.randomUUID() }));

  const report = document(
    "SOC 2 report",
    "Independent examination of the security and availability criteria.",
    "Nda",
    "harborline-soc2.pdf",
    -20,
  );
  const certificate = document(
    "ISO 27001 certificate",
    "Certificate for the information security management system.",
    "Public",
    "harborline-iso27001.pdf",
    -40,
  );
  const whitepaper = document(
    "Security whitepaper",
    "How the platform separates customer warehouses and records access.",
    "Public",
    "harborline-security.pdf",
    -12,
  );
  const pentest = document(
    "Penetration test summary",
    "Summary of the latest independent test and the open findings.",
    "Nda",
    "harborline-pentest.pdf",
    -8,
  );

  const failed = rule("Open a ticket when a test fails", "TestFailed", "Jira", "SEC");
  const ready = rule(
    "Tell the channel when a control is not ready",
    "ControlNotReady",
    "Slack",
    "#security",
  );
  const risk = rule(
    "Mail the risk owner when a risk is created",
    "RiskCreated",
    "Email",
    `security@${company.domain}`,
  );
  const review = rule("Post reviews that are coming due", "ReviewDue", "Teams", "Compliance");

  if (!controls.some((item) => item.code === "HL-01")) {
    controls.push({
      code: "HL-01",
      title: "Customer export is approved",
      description: "A named owner approves each bulk export of customer warehouse data.",
      status: "passing",
      frequency: "Continuous",
      ownerName: "Priya Nair",
      ownerEmail: `priya.nair@${company.domain}`,
      mappings: [
        {
          framework: "iso27001",
          requirement: "A.5.10",
          coverage: "partial",
          frameworkName: "ISO 27001",
        },
      ],
      tests: [],
    });
  }

  const state: M6State = {
    computers,
    vulnerabilities: findings,
    sla: { criticalDays: 7, highDays: 30, mediumDays: 60, lowDays: 90 },
    history: [
      {
        cve: "CVE-2024-6387",
        assetName: "bastion",
        eventKind: "Breached",
        dueOn: day(shift(now, -10)),
        detail: "High findings are due 30 days after first seen.",
        at: shift(now, -9).toISOString(),
      },
      {
        cve: "CVE-2024-3094",
        assetName: "build-runner",
        eventKind: "DueChanged",
        dueOn: day(shift(now, -13)),
        detail: "Critical window moved from 14 days to 7 days.",
        at: shift(now, -18).toISOString(),
      },
      {
        cve: "CVE-2022-41741",
        assetName: "support-portal",
        eventKind: "Ignored",
        dueOn: day(shift(now, 75)),
        detail: "The player is not reachable from the internet and the fix is scheduled.",
        at: shift(now, -2).toISOString(),
      },
    ],
    trust: {
      published: true,
      headline: `${company.name} security and compliance`,
      summary: `${company.name} publishes the frameworks in scope, a control summary, and the documents customers can read. Reports that name customers stay behind an NDA.`,
      contactEmail: `security@${company.domain}`,
      documents: [report, certificate, whitepaper, pentest],
      requests: [
        {
          id: crypto.randomUUID(),
          documentId: report.id,
          documentTitle: report.title,
          requesterName: "Mina Okonkwo",
          requesterEmail: "mina.okonkwo@northbridge.example",
          company: "Northbridge Analytics",
          purpose: "Security review before a warehouse contract.",
          status: "Pending",
          requestedAt: shift(now, -2).toISOString(),
          decidedAt: null,
        },
        {
          id: crypto.randomUUID(),
          documentId: pentest.id,
          documentTitle: pentest.title,
          requesterName: "Owen Blake",
          requesterEmail: "owen.blake@fieldnote.example",
          company: "Fieldnote",
          purpose: "Annual vendor review.",
          status: "Approved",
          requestedAt: shift(now, -18).toISOString(),
          decidedAt: shift(now, -16).toISOString(),
          ndaAccepted: true,
          ndaAcceptedAt: shift(now, -18).toISOString(),
          accessToken: "demo-approved-nda-token-harborline",
        },
      ],
    },
    rules: [failed, ready, risk, review],
    deliveries: [
      {
        id: crypto.randomUUID(),
        trigger: "TestFailed",
        action: "Jira",
        target: "SEC",
        summary: "Personnel completed training within 12 months",
        externalId: "DEMO-SEC-training",
        at: shift(now, -1).toISOString(),
      },
      {
        id: crypto.randomUUID(),
        trigger: "ReviewDue",
        action: "Teams",
        target: "Compliance",
        summary: "Reviews due",
        externalId: "teams:example1",
        at: shift(now, -0.2).toISOString(),
      },
    ],
  };
  return state;

  function finding(
    cve: string,
    title: string,
    severity: string,
    assetName: string,
    source: string,
    age: number,
    fixAvailable: boolean,
    ignored: boolean,
    ignoreReason: string,
  ): Omit<DemoVulnerability, "id"> {
    const first = shift(now, age);
    const days =
      severity === "Critical" ? 7 : severity === "High" ? 30 : severity === "Low" ? 90 : 60;
    return {
      cve,
      title,
      severity,
      assetName,
      source,
      firstSeen: first.toISOString(),
      lastSeen: shift(now, -1).toISOString(),
      slaDue: day(shift(first, days)),
      fixAvailable,
      ignored,
      ignoreReason,
    };
  }
}

function computer(
  serial: string,
  operatingSystem: string,
  ownerName: string,
  ownerEmail: string,
  antivirus: boolean,
  seen: Date,
): DemoComputer {
  return {
    id: crypto.randomUUID(),
    serial,
    operatingSystem,
    ownerName,
    ownerEmail,
    encrypted: true,
    screenLock: true,
    antivirus,
    passwordManager: true,
    autoUpdate: true,
    lastCheckIn: seen.toISOString(),
    source: "Manual",
    screenshotName: null,
    evidenceId: null,
  };
}

function document(
  title: string,
  summary: string,
  access: string,
  fileName: string,
  age: number,
): DemoTrustDocument {
  return {
    id: crypto.randomUUID(),
    title,
    summary,
    access,
    fileName,
    updatedOn: day(shift(new Date(), age)),
  };
}

function rule(name: string, trigger: string, action: string, target: string) {
  return { id: crypto.randomUUID(), name, trigger, action, target, enabled: true };
}

function stateOf(data: DemoWorld) {
  return data.m6;
}

export function attachM6(data: DemoWorld, state: M6State) {
  data.m6 = state;
  refreshBackground(data.controls, data.people);
}

function refreshBackground(controls: DemoControl[], people: DemoPerson[]) {
  const control = controls.find((item) => item.code === "HR-01");
  if (!control || people.length === 0) return;
  const today = new Date();
  const missing = people.filter((person) => {
    if (person.state !== "Active" || !person.hireOn) return false;
    const hired = new Date(`${person.hireOn}T00:00:00Z`);
    const age = (today.getTime() - hired.getTime()) / 86400000;
    if (age < 0 || age > 90) return false;
    const clear = person.background === "Clear";
    const excluded =
      Boolean(person.backgroundExcluded) && Boolean(person.backgroundExclusionReason?.trim());
    return !clear && !excluded;
  });
  const test = control.tests.find((item) => item.name === "New hires have a background check");
  if (!test) return;
  if (missing.length === 0) {
    test.outcome = "pass";
    test.detail = "New hires have a background check or a recorded exclusion.";
    return;
  }
  test.outcome = "fail";
  test.detail = `New hires have no background check: ${missing
    .slice(0, 12)
    .map((person) => person.name)
    .join(", ")}.`;
  control.status = "failing";
}

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

type DemoPentest = {
  id: string;
  title: string;
  vendor: string;
  testedOn: string;
  summary: string;
  findingCount: number;
  uploadedAt: string;
};

const pentestStore = new WeakMap<DemoWorld, DemoPentest[]>();

function reportsOf(data: DemoWorld) {
  const existing = pentestStore.get(data);
  if (existing) return existing;
  const rows = [
    {
      id: crypto.randomUUID(),
      title: "2026 application test",
      vendor: "Northgate Assurance",
      testedOn: day(shift(new Date(), -8)),
      summary: "CVE-2024-5535,Medium,edge-gateway,OpenSSL handshake accepts an empty extension",
      findingCount: 1,
      uploadedAt: shift(new Date(), -8).toISOString(),
    },
  ];
  pentestStore.set(data, rows);
  return rows;
}

function uploadPentest(data: DemoWorld, actor: Actor, body: Record<string, unknown>) {
  if (!canWrite(actor)) return fail(403, "forbidden");
  const title = String(body.title ?? "").trim();
  const vendor = String(body.vendor ?? "").trim();
  const csv = String(body.csv ?? "").trim();
  const lines = csv
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line && !line.toLowerCase().startsWith("cve,"));
  const parsed = lines.map((line) => line.split(",")).filter((cells) => cells.length >= 4);
  if (!title || !vendor || parsed.length === 0) return fail(400, "invalid_pentest");
  const now = new Date().toISOString();
  const state = stateOf(data);
  for (const cells of parsed) {
    const severity = cells[1].trim();
    const days =
      severity === "Critical" ? 7 : severity === "High" ? 30 : severity === "Low" ? 90 : 60;
    const due = new Date();
    due.setUTCDate(due.getUTCDate() + days);
    state.vulnerabilities.unshift({
      id: crypto.randomUUID(),
      cve: cells[0].trim(),
      title: cells.slice(3).join(",").trim(),
      severity,
      assetName: cells[2].trim(),
      source: "Pentest",
      firstSeen: now,
      lastSeen: now,
      slaDue: day(due),
      fixAvailable: false,
      ignored: false,
      ignoreReason: "",
    });
  }
  const report: DemoPentest = {
    id: crypto.randomUUID(),
    title,
    vendor,
    testedOn: String(body.testedOn ?? day(new Date())),
    summary: csv.slice(0, 2000),
    findingCount: parsed.length,
    uploadedAt: now,
  };
  reportsOf(data).unshift(report);
  return json(report);
}

function board(data: DemoWorld) {
  const m6 = stateOf(data);
  const today = day(new Date());
  const items = m6.vulnerabilities.map((item) => ({
    ...item,
    overdue: !item.ignored && item.slaDue < today,
  }));
  const open = items.filter((item) => !item.ignored);
  const assets = [...new Set(open.map((item) => item.assetName))].map((assetName) => {
    const rows = open.filter((item) => item.assetName === assetName);
    return {
      assetName,
      open: rows.length,
      critical: rows.filter((item) => item.severity === "Critical").length,
      high: rows.filter((item) => item.severity === "High").length,
      items: rows,
    };
  });
  return {
    sla: m6.sla,
    findings: open,
    assets,
    ignored: items.filter((item) => item.ignored),
    history: m6.history,
  };
}

function trustAdmin(data: DemoWorld) {
  const trust = stateOf(data).trust;
  return {
    slug: company.slug,
    published: trust.published,
    headline: trust.headline,
    summary: trust.summary,
    contactEmail: trust.contactEmail,
    documents: trust.documents,
    requests: trust.requests,
  };
}

function frameworksOf(data: DemoWorld) {
  return data.frameworks.map((framework) => {
    const controls = data.controls.filter((control) =>
      control.mappings.some((mapping) => mapping.framework === framework.key),
    );
    return {
      key: framework.key,
      name: framework.name,
      controls: controls.length,
      passing: controls.filter((control) => control.status === "passing").length,
    };
  });
}

export function publicTrust(
  method: string,
  path: string,
  body: Record<string, unknown>,
  data: DemoWorld,
) {
  const prefix = "/api/v1/public/trust/";
  const rest = decodeURIComponent(path.slice(prefix.length));
  const [slug, action] = rest.split("/");
  if (slug !== company.slug) return fail(404, "not_found");
  const trust = stateOf(data).trust;
  if (!trust.published) return fail(404, "not_found");
  if (method === "GET" && !action) {
    return json({
      slug: company.slug,
      tenantName: company.name,
      headline: trust.headline,
      summary: trust.summary,
      contactEmail: trust.contactEmail,
      frameworks: frameworksOf(data),
      documents: trust.documents.map((item) => ({
        ...item,
        fileName: item.access === "Public" ? item.fileName : "",
      })),
    });
  }
  if (method === "POST" && action === "requests") {
    const name = String(body.name ?? "").trim();
    const email = String(body.email ?? "").trim();
    const org = String(body.company ?? "").trim();
    const purpose = String(body.purpose ?? "").trim();
    if (!name || !email.includes("@") || !org || !purpose) return fail(400, "invalid_trust");
    const documentId = body.documentId ? String(body.documentId) : null;
    const document = documentId
      ? trust.documents.find((item) => item.id === documentId)
      : undefined;
    if (documentId && !document) return fail(404, "not_found");
    const ndaAccepted = Boolean(body.ndaAccepted);
    const requiresNda = !document || document.access !== "Public";
    if (requiresNda && !ndaAccepted) return fail(403, "nda_required");
    const nowIso = new Date().toISOString();
    const row: DemoTrustRequest = {
      id: crypto.randomUUID(),
      documentId,
      documentTitle: document?.title ?? "",
      requesterName: name,
      requesterEmail: email,
      company: org,
      purpose,
      status: "Pending",
      requestedAt: nowIso,
      decidedAt: null,
      ndaAccepted,
      ndaAcceptedAt: ndaAccepted ? nowIso : null,
      accessToken: "",
    };
    trust.requests.unshift(row);
    return json(row);
  }
  return fail(404, "not_found");
}

export function handleM6(
  method: string,
  path: string,
  body: Record<string, unknown>,
  data: DemoWorld,
  actor: Actor,
) {
  if (path === "/api/v1/computers" && method === "GET") {
    if (actor.role === "Auditor") return fail(403, "forbidden");
    return json(stateOf(data).computers);
  }
  if (path === "/api/v1/computers" && method === "POST") return saveComputer(data, actor, body);
  if (path === "/api/v1/computers/check-in" && method === "POST") {
    return saveComputer(data, actor, { ...body, source: "Agent" });
  }
  if (path === "/api/v1/computers/mdm-sync" && method === "POST") {
    if (!canWrite(actor)) return fail(403, "forbidden");
    return json({ imported: 0 });
  }
  if (path.startsWith("/api/v1/computers/") && path.endsWith("/screenshot") && method === "POST") {
    return screenshot(data, actor, path.split("/")[4], body);
  }
  if (path === "/api/v1/vulnerabilities" && method === "GET") {
    if (actor.role === "Auditor") return fail(403, "forbidden");
    return json(board(data));
  }
  if (path === "/api/v1/vulnerabilities/sync" && method === "POST") {
    if (!canWrite(actor)) return fail(403, "forbidden");
    return json({ imported: 0 });
  }
  if (path === "/api/v1/vulnerabilities/pentests" && method === "GET") {
    if (actor.role === "Auditor") return fail(403, "forbidden");
    return json(reportsOf(data));
  }
  if (path === "/api/v1/vulnerabilities/pentests" && method === "POST") {
    return uploadPentest(data, actor, body);
  }
  if (path === "/api/v1/vulnerabilities/sla" && method === "POST")
    return saveSla(data, actor, body);
  if (
    path.startsWith("/api/v1/vulnerabilities/") &&
    path.endsWith("/ignore") &&
    method === "POST"
  ) {
    return ignoreFinding(data, actor, path.split("/")[4], body);
  }
  if (path === "/api/v1/trust" && method === "GET") {
    if (!canWrite(actor)) return fail(403, "forbidden");
    return json(trustAdmin(data));
  }
  if (path === "/api/v1/trust" && method === "POST") return saveTrust(data, actor, body);
  if (path === "/api/v1/trust/documents" && method === "POST")
    return saveDocument(data, actor, body);
  if (path.startsWith("/api/v1/trust/requests/") && method === "POST") {
    return decide(data, actor, path.split("/")[5], body);
  }
  if (path === "/api/v1/settings/automation" && method === "GET") return rules(data, actor);
  if (path === "/api/v1/settings/automation/deliveries" && method === "GET")
    return deliveries(data, actor);
  if (path === "/api/v1/settings/automation" && method === "POST")
    return saveRule(data, actor, body);
  if (path === "/api/v1/settings/automation/run" && method === "POST") return runDue(data, actor);
  if (path === "/api/v1/controls" && method === "POST") return createControl(data, actor, body);
  if (path === "/api/v1/controls/import" && method === "POST")
    return importControls(data, actor, body);
  if (path.startsWith("/api/v1/people/") && path.endsWith("/background") && method === "POST") {
    return background(
      data,
      actor,
      path.slice("/api/v1/people/".length, -"/background".length),
      body,
    );
  }
  if (path === "/api/v1/audits/period" && method === "POST") return setPeriod(actor, body);
  return null;
}

function saveComputer(data: DemoWorld, actor: Actor, body: Record<string, unknown>) {
  if (!canWrite(actor)) return fail(403, "forbidden");
  const serial = String(body.serial ?? "").trim();
  const operatingSystem = String(body.operatingSystem ?? "").trim();
  const ownerName = String(body.ownerName ?? "").trim();
  const ownerEmail = String(body.ownerEmail ?? "").trim();
  const source = body.source === "Mdm" || body.source === "Agent" ? String(body.source) : "Manual";
  if (!serial || !operatingSystem || !ownerName || !ownerEmail.includes("@"))
    return fail(400, "invalid_computer");
  const computers = stateOf(data).computers;
  const id = body.id ? String(body.id) : "";
  let row = id ? computers.find((item) => item.id === id) : undefined;
  if (id && !row) return fail(404, "not_found");
  if (computers.some((item) => item.serial === serial && item.id !== row?.id))
    return fail(400, "invalid_computer");
  if (!row) {
    row = computer(serial, operatingSystem, ownerName, ownerEmail, true, new Date());
    computers.unshift(row);
  }
  Object.assign(row, {
    serial,
    operatingSystem,
    ownerName,
    ownerEmail,
    encrypted: Boolean(body.encrypted),
    screenLock: Boolean(body.screenLock),
    antivirus: Boolean(body.antivirus),
    passwordManager: Boolean(body.passwordManager),
    autoUpdate: Boolean(body.autoUpdate),
    source,
  });
  return json(row);
}

function screenshot(data: DemoWorld, actor: Actor, id: string, body: Record<string, unknown>) {
  if (!canWrite(actor)) return fail(403, "forbidden");
  const row = stateOf(data).computers.find((item) => item.id === id);
  const name = String(body.fileName ?? "").trim();
  const content = String(body.contentBase64 ?? "");
  if (!row || !name || !content) return fail(400, "invalid_evidence");
  row.screenshotName = name;
  row.evidenceId = crypto.randomUUID();
  row.lastCheckIn = new Date().toISOString();
  return json(row);
}

function ignoreFinding(data: DemoWorld, actor: Actor, id: string, body: Record<string, unknown>) {
  if (!canWrite(actor)) return fail(403, "forbidden");
  const reason = String(body.reason ?? "").trim();
  const row = stateOf(data).vulnerabilities.find((item) => item.id === id);
  if (!row || !reason) return fail(400, "invalid_vulnerability");
  row.ignored = true;
  row.ignoreReason = reason;
  stateOf(data).history.unshift({
    cve: row.cve,
    assetName: row.assetName,
    eventKind: "Ignored",
    dueOn: row.slaDue,
    detail: reason,
    at: new Date().toISOString(),
  });
  return json({ ...row, overdue: false });
}

function saveSla(data: DemoWorld, actor: Actor, body: Record<string, unknown>) {
  if (!canWrite(actor)) return fail(403, "forbidden");
  const sla = {
    criticalDays: Number(body.criticalDays),
    highDays: Number(body.highDays),
    mediumDays: Number(body.mediumDays),
    lowDays: Number(body.lowDays),
  };
  if (Object.values(sla).some((value) => !Number.isInteger(value) || value < 1 || value > 365)) {
    return fail(400, "invalid_sla");
  }
  stateOf(data).sla = sla;
  for (const row of stateOf(data).vulnerabilities) {
    if (row.ignored) continue;
    const days =
      row.severity === "Critical"
        ? sla.criticalDays
        : row.severity === "High"
          ? sla.highDays
          : row.severity === "Low"
            ? sla.lowDays
            : sla.mediumDays;
    const due = day(shift(new Date(row.firstSeen), days));
    if (due !== row.slaDue) {
      row.slaDue = due;
      stateOf(data).history.unshift({
        cve: row.cve,
        assetName: row.assetName,
        eventKind: "DueChanged",
        dueOn: due,
        detail: `${row.severity} window is ${days} days`,
        at: new Date().toISOString(),
      });
    }
  }
  return json(sla);
}

function saveTrust(data: DemoWorld, actor: Actor, body: Record<string, unknown>) {
  if (!canWrite(actor)) return fail(403, "forbidden");
  const trust = stateOf(data).trust;
  const headline = String(body.headline ?? "").trim();
  const summary = String(body.summary ?? "").trim();
  const contactEmail = String(body.contactEmail ?? "").trim();
  if (!headline || !summary || !contactEmail.includes("@")) return fail(400, "invalid_trust");
  trust.published = Boolean(body.published);
  trust.headline = headline;
  trust.summary = summary;
  trust.contactEmail = contactEmail;
  return json(trustAdmin(data));
}

function saveDocument(data: DemoWorld, actor: Actor, body: Record<string, unknown>) {
  if (!canWrite(actor)) return fail(403, "forbidden");
  const title = String(body.title ?? "").trim();
  const summary = String(body.summary ?? "").trim();
  const access = body.access === "Nda" || body.access === "Public" ? String(body.access) : "";
  const fileName = String(body.fileName ?? "").trim();
  if (!title || !summary || !access || !fileName) return fail(400, "invalid_trust");
  const documents = stateOf(data).trust.documents;
  const id = body.id ? String(body.id) : "";
  let row = id ? documents.find((item) => item.id === id) : undefined;
  if (id && !row) return fail(404, "not_found");
  if (!row) {
    row = document(title, summary, access, fileName, 0);
    documents.unshift(row);
  }
  const contentBase64 = typeof body.contentBase64 === "string" ? body.contentBase64 : undefined;
  const contentType = typeof body.contentType === "string" ? body.contentType : "application/pdf";
  Object.assign(row, {
    title,
    summary,
    access,
    fileName,
    updatedOn: day(new Date()),
    ...(contentBase64 ? { hasFile: true, contentBase64, contentType } : {}),
  });
  return json(row);
}

function decide(data: DemoWorld, actor: Actor, id: string, body: Record<string, unknown>) {
  if (!canWrite(actor)) return fail(403, "forbidden");
  const status = body.status === "Approved" || body.status === "Denied" ? String(body.status) : "";
  const row = stateOf(data).trust.requests.find((item) => item.id === id);
  if (!row || !status) return fail(400, "invalid_trust");
  row.status = status;
  row.decidedAt = new Date().toISOString();
  row.accessToken = status === "Approved" ? crypto.randomUUID().replaceAll("-", "") : "";
  return json(row);
}

export function trustStateOf(data: DemoWorld) {
  return stateOf(data).trust;
}

function rules(data: DemoWorld, actor: Actor) {
  if (actor.role !== "Admin") return fail(403, "forbidden");
  return json(stateOf(data).rules);
}

function deliveries(data: DemoWorld, actor: Actor) {
  if (actor.role !== "Admin") return fail(403, "forbidden");
  return json(stateOf(data).deliveries);
}

function saveRule(data: DemoWorld, actor: Actor, body: Record<string, unknown>) {
  if (actor.role !== "Admin") return fail(403, "forbidden");
  const name = String(body.name ?? "").trim();
  const trigger = String(body.trigger ?? "");
  const action = String(body.action ?? "");
  const target = String(body.target ?? "").trim();
  if (!name || !target || !triggers.includes(trigger) || !actions.includes(action))
    return fail(400, "invalid_rule");
  const ruleset = stateOf(data).rules;
  const id = body.id ? String(body.id) : "";
  let row = id ? ruleset.find((item) => item.id === id) : undefined;
  if (id && !row) return fail(404, "not_found");
  if (!row) {
    row = rule(name, trigger, action, target);
    ruleset.unshift(row);
  }
  Object.assign(row, { name, trigger, action, target, enabled: Boolean(body.enabled) });
  return json(row);
}

function runDue(data: DemoWorld, actor: Actor) {
  if (actor.role !== "Admin") return fail(403, "forbidden");
  const names = data.vendors
    .slice(0, 4)
    .map((item) => item.name)
    .concat(data.risks.slice(0, 4).map((item) => item.title));
  const enabled = stateOf(data).rules.filter(
    (item) => item.enabled && item.trigger === "ReviewDue",
  );
  const now = new Date().toISOString();
  for (const item of enabled) {
    stateOf(data).deliveries.unshift({
      id: crypto.randomUUID(),
      trigger: "ReviewDue",
      action: item.action,
      target: item.target,
      summary: "Reviews due",
      externalId:
        item.action === "Jira"
          ? `DEMO-${item.target}-${crypto.randomUUID().slice(0, 8)}`
          : `${item.action.toLowerCase()}:${crypto.randomUUID().slice(0, 8)}`,
      at: now,
    });
  }
  if (names.length === 0 && enabled.length === 0) return json(stateOf(data).deliveries);
  return json(stateOf(data).deliveries);
}

function createControl(data: DemoWorld, actor: Actor, body: Record<string, unknown>) {
  if (!canWrite(actor)) return fail(403, "forbidden");
  const code = String(body.code ?? "").trim();
  const title = String(body.title ?? "").trim();
  const framework = String(body.framework ?? "").trim();
  const requirement = String(body.requirement ?? "").trim();
  if (!code || !title || !framework || !requirement) return fail(400, "invalid_control");
  if (!data.controls.some((item) => item.code === code)) {
    data.controls.push({
      code,
      title,
      description: String(body.description ?? ""),
      status: "manual",
      frequency: String(body.frequency ?? "Annual"),
      ownerName: String(body.ownerName ?? ""),
      ownerEmail: String(body.ownerEmail ?? ""),
      mappings: [
        {
          framework,
          requirement,
          coverage: body.coverage === "partial" ? "partial" : "full",
          frameworkName: framework,
        },
      ],
      tests: [],
    });
  }
  return json({ code, title, custom: true });
}

function importControls(data: DemoWorld, actor: Actor, body: Record<string, unknown>) {
  if (!canWrite(actor)) return fail(403, "forbidden");
  const csv = String(body.csv ?? "");
  const lines = csv.split(/\r?\n/).filter((line) => line.trim());
  if (lines.length < 2) return fail(400, "invalid_import");
  const header = lines[0].split(",").map((cell) => cell.trim());
  let count = 0;
  for (const line of lines.slice(1)) {
    const cells = line.split(",");
    const value = (name: string) => cells[header.indexOf(name)]?.trim() ?? "";
    const created = createControl(data, actor, {
      code: value("code"),
      title: value("title"),
      description: value("description"),
      frequency: value("frequency") || "Annual",
      ownerName: value("ownerName"),
      ownerEmail: value("ownerEmail"),
      framework: value("framework"),
      requirement: value("requirement"),
      coverage: value("coverage") || "full",
    });
    if (created.status !== 200) return created;
    count += 1;
  }
  return json(count);
}

function background(data: DemoWorld, actor: Actor, id: string, body: Record<string, unknown>) {
  if (!canWrite(actor)) return fail(403, "forbidden");
  const person = data.people.find((item) => item.id === id);
  const provider = String(body.provider ?? "").trim();
  const link = String(body.link ?? "").trim();
  const reason = String(body.exclusionReason ?? "").trim();
  const excluded = Boolean(body.excluded);
  if (!person || !provider || (excluded && !reason)) return fail(400, "invalid_background");
  person.backgroundProvider = provider;
  person.backgroundLink = link;
  person.backgroundExcluded = excluded;
  person.backgroundExclusionReason = excluded ? reason : "";
  if (body.completedOn) person.backgroundOn = String(body.completedOn);
  else if (excluded) person.backgroundOn = day(new Date());
  if (body.contentBase64) person.backgroundEvidenceId = crypto.randomUUID();
  if (excluded) person.background = "Excluded";
  else if (!person.backgroundOn && !person.backgroundEvidenceId) person.background = "Pending";
  else {
    person.background = "Clear";
    person.backgroundOn = person.backgroundOn ?? day(new Date());
  }
  refreshBackground(data.controls, data.people);
  return json(personDetailShape(data, person));
}

function personDetailShape(data: DemoWorld, person: DemoPerson) {
  return {
    id: person.id,
    name: person.name,
    email: person.email,
    department: person.department,
    source: person.source,
    state: person.state,
    training: person.training,
    trainingDueOn: person.trainingDueOn,
    role: person.role,
    hireOn: person.hireOn,
    leftOn: person.leftOn,
    mfaEnrolled: person.mfaEnrolled ?? false,
    background: person.background ?? "NotStarted",
    backgroundOn: person.backgroundOn ?? null,
    backgroundProvider: person.backgroundProvider ?? "",
    backgroundLink: person.backgroundLink ?? "",
    backgroundEvidenceId: person.backgroundEvidenceId ?? null,
    backgroundExcluded: person.backgroundExcluded ?? false,
    backgroundExclusionReason: person.backgroundExclusionReason ?? "",
    device: person.device ?? "Missing",
    statusReason: person.statusReason ?? "",
    trainingCompletedOn: person.trainingCompletedOn ?? null,
    leaverStillHasAccess:
      person.state === "Offboarded" && person.accounts.some((account) => account.active),
    accounts: person.accounts,
    tasks: person.tasks,
    acknowledgements: data.policies.flatMap((policy) =>
      policy.acknowledgements
        .filter((item) => item.personEmail.toLowerCase() === person.email.toLowerCase())
        .map((item) => ({
          policyCode: policy.code,
          policyTitle: policy.title,
          dueOn: item.dueOn,
          acknowledgedAt: item.acknowledgedAt,
        })),
    ),
  };
}

let periodOverride: { start: string; end: string } | null = null;

export function currentPeriod() {
  return periodOverride ?? auditPeriod;
}

export function resetPeriod() {
  periodOverride = null;
}

function setPeriod(actor: Actor, body: Record<string, unknown>) {
  if (!canWrite(actor)) {
    return fail(403, "forbidden");
  }
  const periodStart = String(body.periodStart ?? "");
  const periodEnd = String(body.periodEnd ?? "");
  if (!periodStart || !periodEnd || periodEnd < periodStart) return fail(400, "invalid_period");
  periodOverride = { start: periodStart, end: periodEnd };
  return json({ periodStart, periodEnd });
}

async function sha256(text: string) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, "0")).join("");
}
