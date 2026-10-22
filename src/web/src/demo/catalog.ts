import exampleControls from "../../../api/content/frameworks/example-controls.json";
import iso27001 from "../../../api/content/frameworks/iso27001.json";
import iso27017 from "../../../api/content/frameworks/iso27017.json";
import iso27018 from "../../../api/content/frameworks/iso27018.json";
import soc2 from "../../../api/content/frameworks/soc2.json";
import {
  company,
  evaluateCheck,
  resourceRows,
  reviewAccounts,
  seedAccess,
  seedActivity,
  seedControls,
  seedDevices,
  seedDirectory,
  seedEvidence,
  seedFindings,
  seedHistory,
  seedNotifications,
  seedPeople,
  seedRequests,
  seedRisks,
  seedVendors,
} from "./client";

export { company };

function withBrand(value: unknown): unknown {
  if (typeof value === "string") {
    return value.replaceAll("{{company}}", company.name).replaceAll("{{domain}}", company.domain);
  }
  if (Array.isArray(value)) return value.map(withBrand);
  if (value && typeof value === "object") {
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, withBrand(item)]));
  }
  return value;
}

const brandedControls = withBrand(exampleControls) as typeof exampleControls;

export type DemoControl = {
  code: string;
  title: string;
  description: string;
  status: string;
  frequency: string;
  ownerName: string;
  ownerEmail: string;
  mappings: { framework: string; requirement: string; coverage: string; frameworkName: string }[];
  tests: {
    id: string;
    name: string;
    kind: string;
    outcome: string;
    detail: string;
    remediation: string | null;
  }[];
};

export type DemoEvidence = {
  id: string;
  title: string;
  controlCode: string;
  source: string;
  collectedAt: string;
  integrity: string;
  sha256: string;
  lockedUntil: string | null;
  body: string;
};

export type DemoCheck = {
  id: string;
  key: string;
  name: string;
  controlCode: string;
  outcome: string;
  detail: string;
  remediation: string;
  requirements: string[];
};

export type DemoPolicy = {
  code: string;
  title: string;
  ownerName: string;
  controlCodes: string[];
  versions: {
    number: number;
    state: string;
    body: string;
    createdAt: string;
    approvedAt: string | null;
    approvedByName: string | null;
  }[];
  acknowledgements: {
    version: number;
    personName: string;
    personEmail: string;
    dueOn: string;
    acknowledgedAt: string | null;
    remindedAt: string | null;
  }[];
};

export type DemoRequest = {
  code: string;
  title: string;
  population: string;
  sampleSize: number | null;
  source: string;
  dueOn: string;
  status: string;
  createdAt: string;
  response: {
    body: string;
    respondedByName: string;
    respondedAt: string;
    evidenceTitle: string | null;
  } | null;
};

export type DemoFinding = {
  code: string;
  title: string;
  severity: string;
  detail: string;
  requirementCode: string;
  managementResponse: string | null;
  responseByName: string | null;
  comments: { authorName: string; body: string; at: string }[];
};

export type DemoRisk = {
  code: string;
  title: string;
  statement: string;
  likelihood: number;
  impact: number;
  score: number;
  band: string;
  ownerName: string;
  ownerEmail: string;
  treatment: string;
  treatmentPlan: string;
  reviewOn: string;
  residualLikelihood: number;
  residualImpact: number;
  residualScore: number;
  residualBand: string;
  controlCodes: string[];
};

export type DemoPerson = {
  id: string;
  name: string;
  email: string;
  department: string;
  role: string;
  hireOn: string | null;
  leftOn: string | null;
  source: string;
  state: string;
  training: string;
  trainingDueOn: string | null;
  tasks: { kind: string; taskCode: string; done: boolean }[];
  accounts: { provider: string; accountName: string; active: boolean }[];
};

export type DemoSoa = {
  code: string;
  title: string;
  applicable: boolean;
  justification: string;
  implementation: string;
};

export type DemoVendor = {
  id: string;
  name: string;
  service: string;
  criticality: string;
  dataAccess: string;
  reviewState: string;
  reviewDueOn: string;
  riskRating: number;
  documents: { title: string; kind: string; issuedOn: string | null }[];
};

export type DemoCampaign = {
  id: string;
  name: string;
  dueOn: string;
  state: string;
  completedAt: string | null;
  evidenceId: string | null;
  items: {
    id: string;
    personName: string;
    personEmail: string;
    provider: string;
    accountName: string;
    decision: string;
  }[];
};

export type DemoWorld = {
  controls: DemoControl[];
  evidence: DemoEvidence[];
  checks: DemoCheck[];
  policies: DemoPolicy[];
  requests: DemoRequest[];
  findings: DemoFinding[];
  risks: DemoRisk[];
  people: DemoPerson[];
  soa: DemoSoa[];
  vendors: DemoVendor[];
  campaigns: DemoCampaign[];
  history: { weekOf: string; passing: number; failing: number }[];
  notifications: { title: string; body: string; href: string; at: string }[];
  devices: { ownerName: string; platform: string; serial: string; lastSeenOn: string }[];
  resources: { provider: string; kind: string; name: string; detail: string }[];
  activity: { at: string; action: string; result: string; detail: string; actor: string }[];
  frameworks: {
    key: string;
    name: string;
    version: string;
    description: string;
    requirements: DemoNode[];
  }[];
};

export type DemoNode = {
  code: string;
  title: string;
  summary: string;
  controls: { code: string; title: string; coverage: string; status: string }[];
  children: DemoNode[];
};

type RawNode = { code: string; title: string; summary?: string; children?: RawNode[] };
type RawFramework = {
  key: string;
  name: string;
  version: string;
  description: string;
  requirements: RawNode[];
};

const frameworks = [soc2, iso27001, iso27017, iso27018] as RawFramework[];

const automated = [
  {
    key: "s3-public-access",
    name: "S3 public access blocked",
    controlCode: "CFG-01",
    remediation: "Turn on Block Public Access for every bucket.",
    requirements: ["soc2 CC6.1", "iso27001 A.8.3"],
  },
  {
    key: "mfa-all-users",
    name: "MFA enforced for all users",
    controlCode: "AC-07",
    remediation: "Turn on MFA for every active user. Suspend accounts that cannot enroll.",
    requirements: ["soc2 CC6.1", "iso27001 A.5.17", "iso27001 A.8.5"],
  },
  {
    key: "branch-protection",
    name: "Branch protection on the default branch",
    controlCode: "CM-11",
    remediation: "Require a pull request on the default branch of every repository.",
    requirements: ["soc2 CC8.1", "iso27001 A.8.4", "iso27001 A.8.32"],
  },
  {
    key: "offboarded-removed",
    name: "Offboarded users removed",
    controlCode: "HR-08",
    remediation: "Remove or suspend the leaver in every connected system.",
    requirements: ["soc2 CC6.2", "soc2 CC6.3", "iso27001 A.6.5"],
  },
  {
    key: "encryption-at-rest",
    name: "Encryption at rest",
    controlCode: "CR-02",
    remediation: "Turn on default encryption for every bucket.",
    requirements: ["soc2 CC6.7", "iso27001 A.8.24"],
  },
  {
    key: "root-mfa",
    name: "Root account MFA",
    controlCode: "AC-07",
    remediation:
      "Enable MFA on the AWS root account and keep the device with the break-glass owners.",
    requirements: ["soc2 CC6.1", "iso27001 A.5.17"],
  },
  {
    key: "cloudtrail-enabled",
    name: "CloudTrail enabled",
    controlCode: "LG-03",
    remediation: "Create a trail that is logging, and keep it from being stopped.",
    requirements: ["soc2 CC7.2", "iso27001 A.8.15"],
  },
] as const;

function camel(value: string) {
  return value.length === 0 ? value : value[0].toLowerCase() + value.slice(1);
}

function iso(date: Date) {
  return date.toISOString();
}

function day(date: Date) {
  return date.toISOString().slice(0, 10);
}

function addDays(date: Date, days: number) {
  const next = new Date(date);
  next.setUTCDate(next.getUTCDate() + days);
  return next;
}

async function sha256(text: string) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, "0")).join("");
}

function id(n: number) {
  return `00000000-0000-4000-8000-${n.toString(16).padStart(12, "0")}`;
}

function frameworkName(key: string) {
  return frameworks.find((item) => item.key === key)?.name ?? key;
}

function decorate(nodes: RawNode[], framework: string, controls: DemoControl[]): DemoNode[] {
  return nodes.map((node) => ({
    code: node.code,
    title: node.title,
    summary: node.summary ?? "",
    controls: controls.flatMap((control) => {
      const mapping = control.mappings.find(
        (item) => item.framework === framework && item.requirement === node.code,
      );
      return mapping
        ? [
            {
              code: control.code,
              title: control.title,
              coverage: mapping.coverage,
              status: control.status,
            },
          ]
        : [];
    }),
    children: decorate(node.children ?? [], framework, controls),
  }));
}

export async function createWorld(now = new Date()): Promise<DemoWorld> {
  let nextId = 1;
  const source = [...brandedControls.controls, ...seedControls] as {
    code: string;
    title: string;
    description: string;
    status: string;
    frequency: string;
    ownerName: string;
    ownerEmail: string;
    mappings: { framework: string; requirement: string; coverage: string }[];
    tests: { name: string; kind: string; outcome: string; detail?: string | null }[];
  }[];

  const controls: DemoControl[] = source.map((control) => ({
    code: control.code,
    title: control.title,
    description: control.description,
    status: camel(control.status),
    frequency: control.frequency,
    ownerName: control.ownerName,
    ownerEmail: control.ownerEmail,
    mappings: control.mappings.map((mapping) => ({
      ...mapping,
      frameworkName: frameworkName(mapping.framework),
    })),
    tests: control.tests.map((test) => ({
      id: id(nextId++),
      name: test.name,
      kind: test.kind,
      outcome: test.outcome,
      detail: test.detail ?? "",
      remediation: null,
    })),
  }));

  const checks: DemoCheck[] = [];
  const evidence: DemoEvidence[] = [];
  for (const check of automated) {
    const result = evaluateCheck(check.key);
    const outcome = result.outcome === "pass" ? "pass" : "fail";
    const body = `${company.name} example snapshot. ${result.detail} Example record.`;
    const control = controls.find((item) => item.code === check.controlCode);
    if (outcome === "fail" && control) control.status = "failing";
    const checkId = id(nextId++);
    checks.push({
      id: checkId,
      key: check.key,
      name: check.name,
      controlCode: check.controlCode,
      outcome,
      detail: result.detail,
      remediation: check.remediation,
      requirements: [...check.requirements],
    });
    if (control) {
      control.tests.push({
        id: checkId,
        name: check.name,
        kind: "automated",
        outcome,
        detail: result.detail,
        remediation: check.remediation,
      });
    }
    const hash = await sha256(body);
    evidence.push({
      id: id(nextId++),
      title: check.name,
      controlCode: check.controlCode,
      source: "Example fixture",
      collectedAt: iso(addDays(now, -2)),
      integrity: "verified",
      sha256: hash,
      lockedUntil: iso(addDays(now, 365 * 3)),
      body,
    });
  }
  for (const item of seedEvidence) {
    const hash = await sha256(item.body);
    evidence.push({
      id: id(nextId++),
      title: item.title,
      controlCode: item.control,
      source: "Manual",
      collectedAt: iso(addDays(now, -8)),
      integrity: "verified",
      sha256: hash,
      lockedUntil: iso(addDays(now, 365 * 3)),
      body: item.body,
    });
  }

  const policies: DemoPolicy[] = [
    policy(
      "POL-ISP",
      "Information security policy",
      "GV-01",
      "The company names an owner for information security and reviews this policy once a year. Every worker follows the controls mapped to it. An exception is written down and approved before the work proceeds.",
      now,
    ),
    policy(
      "POL-ACC",
      "Access control policy",
      "AC-07",
      "An account is issued to one named person. A second factor is required on every active account. Access is removed when the person leaves, and a shared password is not used.",
      now,
    ),
    policy(
      "POL-CHG",
      "Change management policy",
      "CM-11",
      "A change to production is recorded before it ships. The default branch of a production repository stays protected so a second person reviews the change before it merges.",
      now,
    ),
    policy(
      "POL-INC",
      "Incident response policy",
      "IR-02",
      "A person who sees a security incident reports it the same day. The response owner writes down what happened, who was told, and what was restored, and keeps that note with the control evidence.",
      now,
    ),
    policy(
      "POL-BCP",
      "Business continuity policy",
      "BC-01",
      "The company keeps a short list of services it must restore and a named owner for each one. A restore is practiced often enough that the owner can show the last result.",
      now,
    ),
    policy(
      "POL-PRI",
      "Personal data policy",
      "PP-01",
      "Personal data is collected for a stated purpose and kept only while that purpose still needs it. This example policy is mapped to the privacy control.",
      now,
    ),
  ];
  const access = policies.find((item) => item.code === "POL-ACC");
  if (access) {
    access.versions.push({
      number: 2,
      state: "InReview",
      body: `${access.versions[0].body} Contractors use the same second factor before their account is marked active.`,
      createdAt: iso(addDays(now, -2)),
      approvedAt: null,
      approvedByName: null,
    });
    const lena = seedDirectory.find((person) => person.name === "Lena Cho");
    const priya = seedDirectory.find((person) => person.name === "Priya Nair");
    if (lena) {
      access.acknowledgements.push(
        ack(1, lena.name, lena.email, day(addDays(now, -3)), null, iso(addDays(now, -1))),
      );
    }
    if (priya) {
      access.acknowledgements.push(
        ack(1, priya.name, priya.email, day(addDays(now, 10)), null, null),
      );
    }
  }
  const isp = policies.find((item) => item.code === "POL-ISP");
  for (const person of seedDirectory) {
    if (person.leaveOffset !== null && person.leaveOffset <= -30) continue;
    const dueOffset = person.training === "Overdue" ? person.trainingDueOffset : 20;
    const acknowledged = person.training === "Complete" ? iso(addDays(now, -2)) : null;
    isp?.acknowledgements.push(
      ack(1, person.name, person.email, day(addDays(now, dueOffset)), acknowledged, null),
    );
  }

  const requests: DemoRequest[] = seedRequests.map((item) =>
    request(
      item.code,
      item.title,
      item.population,
      item.sampleSize,
      item.source,
      day(addDays(now, item.dueOffset)),
      item.status,
      addDays(now, item.createdOffset),
      item.response
        ? {
            body: item.response,
            respondedByName: "Priya Nair",
            respondedAt: iso(addDays(now, item.createdOffset + 1)),
            evidenceTitle: null,
          }
        : null,
    ),
  );

  const findings: DemoFinding[] = seedFindings.map((item) => ({
    code: item.code,
    title: item.title,
    severity: item.severity,
    detail: item.detail,
    requirementCode: item.requirementCode,
    managementResponse: item.managementResponse,
    responseByName: item.responseByName,
    comments: item.comments.map((comment) => ({
      authorName: comment.author,
      body: comment.body,
      at: iso(addDays(now, comment.dayOffset)),
    })),
  }));

  const governed = governance(now, controls);
  return {
    controls,
    evidence,
    checks,
    policies,
    requests,
    findings,
    ...governed,
    history: seedHistory.map((item) => ({
      weekOf: day(addDays(now, item.week * 7)),
      passing: item.passing,
      failing: item.failing,
    })),
    notifications: seedNotifications.map((item) => ({
      title: item.title,
      body: item.body,
      href: item.href,
      at: iso(addDays(now, item.dayOffset)),
    })),
    devices: seedDevices.map((item) => ({
      ownerName: item.ownerName,
      platform: item.platform,
      serial: item.serial,
      lastSeenOn: day(addDays(now, item.seenOffset)),
    })),
    resources: resourceRows(),
    activity: seedActivity.map((item) => ({
      at: iso(addDays(now, item.dayOffset)),
      action: item.action,
      result: item.result,
      detail: item.detail,
      actor: item.actor,
    })),
    frameworks: frameworks.map((framework) => ({
      key: framework.key,
      name: framework.name,
      version: framework.version,
      description: framework.description,
      requirements: decorate(framework.requirements, framework.key, controls),
    })),
  };
}

function policy(code: string, title: string, control: string, body: string, now: Date): DemoPolicy {
  return {
    code,
    title,
    ownerName: "Priya Nair",
    controlCodes: [control],
    versions: [
      {
        number: 1,
        state: "Approved",
        body,
        createdAt: iso(addDays(now, -20)),
        approvedAt: iso(addDays(now, -18)),
        approvedByName: "Chandra Vell",
      },
    ],
    acknowledgements: [],
  };
}

function ack(
  version: number,
  personName: string,
  personEmail: string,
  dueOn: string,
  acknowledgedAt: string | null,
  remindedAt: string | null,
) {
  return { version, personName, personEmail, dueOn, acknowledgedAt, remindedAt };
}

function request(
  code: string,
  title: string,
  population: string,
  sampleSize: number | null,
  source: string,
  dueOn: string,
  status: string,
  createdAt: Date,
  response: DemoRequest["response"],
): DemoRequest {
  return {
    code,
    title,
    population,
    sampleSize,
    source,
    dueOn,
    status,
    createdAt: iso(createdAt),
    response,
  };
}

function band(score: number) {
  if (score >= 15) return "critical";
  if (score >= 8) return "high";
  if (score >= 4) return "medium";
  return "low";
}

function risk(
  code: string,
  title: string,
  statement: string,
  likelihood: number,
  impact: number,
  ownerName: string,
  ownerEmail: string,
  treatment: string,
  treatmentPlan: string,
  reviewOn: string,
  residualLikelihood: number,
  residualImpact: number,
  controlCodes: string[],
): DemoRisk {
  const score = likelihood * impact;
  const residualScore = residualLikelihood * residualImpact;
  return {
    code,
    title,
    statement,
    likelihood,
    impact,
    score,
    band: band(score),
    ownerName,
    ownerEmail,
    treatment,
    treatmentPlan,
    reviewOn,
    residualLikelihood,
    residualImpact,
    residualScore,
    residualBand: band(residualScore),
    controlCodes,
  };
}

function annexLeaves(nodes: RawNode[]): { code: string; title: string }[] {
  return nodes.flatMap((node) =>
    node.children && node.children.length > 0
      ? annexLeaves(node.children)
      : /^A\.\d+\.\d+$/.test(node.code)
        ? [{ code: node.code, title: node.title }]
        : [],
  );
}

function governance(now: Date, controls: DemoControl[]) {
  const plus = (days: number) => day(addDays(now, days));
  const people: DemoPerson[] = seedPeople(plus).map((person, index) => ({
    id: `person-${index + 1}`,
    name: person.name,
    email: person.email,
    department: person.department,
    role: person.role,
    hireOn: person.hireOn,
    leftOn: person.leftOn,
    source: person.source,
    state: person.state,
    training: person.training,
    trainingDueOn: person.trainingDueOn,
    tasks: [
      ...["account", "mfa", "policies", "training", "equipment"].map((taskCode) => ({
        kind: "Onboarding",
        taskCode,
        done: person.state !== "Offboarded" && taskCode !== "equipment",
      })),
      ...["disable", "assets", "access", "interview"].map((taskCode) => ({
        kind: "Offboarding",
        taskCode,
        done: false,
      })),
    ],
    accounts: person.accounts,
  }));
  const soa = annexLeaves((iso27001 as RawFramework).requirements)
    .map((leaf) => {
      const related = controls.filter((control) =>
        control.mappings.some(
          (mapping) => mapping.framework === "iso27001" && mapping.requirement === leaf.code,
        ),
      );
      const excluded = leaf.code === "A.7.5";
      const implementation = excluded
        ? "NotApplicable"
        : related.some((control) => control.status === "failing")
          ? "InProgress"
          : related.length > 0 && related.every((control) => control.status === "passing")
            ? "Implemented"
            : "NotStarted";
      return {
        code: leaf.code,
        title: leaf.title,
        applicable: !excluded,
        justification: excluded
          ? `${company.name} does not run its own data center. Customer warehouses run on the connected clouds, and the office is covered by the landlord.`
          : `This control applies to the way ${company.name} handles information.`,
        implementation,
      };
    })
    .sort((left, right) => annexRank(left.code) - annexRank(right.code));
  const accounts = reviewAccounts();
  const campaigns: DemoCampaign[] = [
    {
      id: "campaign-q4",
      name: seedAccess.name,
      dueOn: plus(seedAccess.dueOffset),
      state: "Open",
      completedAt: null,
      evidenceId: null,
      items: accounts.map((row, index) => ({
        id: `review-${index + 1}`,
        personName: row.personName,
        personEmail: row.personEmail,
        provider: row.provider,
        accountName: row.accountName,
        decision: row.decision,
      })),
    },
  ];
  return {
    risks: seedRisks.map((item) =>
      risk(
        item.code,
        item.title,
        item.statement,
        item.likelihood,
        item.impact,
        item.ownerName,
        item.ownerEmail,
        item.treatment,
        item.treatmentPlan,
        plus(item.reviewOffset),
        item.residualLikelihood,
        item.residualImpact,
        item.controlCodes,
      ),
    ),
    people,
    soa,
    vendors: seedVendors.map((item, index) => ({
      id: `vendor-${index + 1}`,
      name: item.name,
      service: item.service,
      criticality: item.criticality,
      dataAccess: item.dataAccess,
      reviewState: item.reviewState,
      reviewDueOn: plus(item.reviewOffset),
      riskRating: item.riskRating,
      documents: item.documents.map((file) => ({
        title: file.title,
        kind: file.kind,
        issuedOn: plus(file.issuedOffset),
      })),
    })),
    campaigns,
  };
}

function annexRank(code: string) {
  const parts = code.split(".").map((part) => Number(part.replace("A", "0")));
  return (parts[1] ?? 0) * 100 + (parts[2] ?? 0);
}
