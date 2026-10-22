import exampleControls from "../../../api/content/frameworks/example-controls.json";
import iso27001 from "../../../api/content/frameworks/iso27001.json";
import soc2 from "../../../api/content/frameworks/soc2.json";

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

const frameworks = [soc2, iso27001] as RawFramework[];

const automated = [
  {
    key: "s3-public-access",
    name: "S3 public access blocked",
    controlCode: "CFG-01",
    outcome: "fail",
    detail: "Public access is not blocked: marketing-site.",
    remediation: "Turn on Block Public Access for every bucket.",
    requirements: ["soc2 CC6.1", "iso27001 A.8.3"],
    body: "Example snapshot, Acme Robotics. Bucket marketing-site does not block public access. This is example data.",
  },
  {
    key: "mfa-all-users",
    name: "MFA enforced for all users",
    controlCode: "AC-07",
    outcome: "fail",
    detail: "These active users have no MFA: Jordan P., Casey R.",
    remediation: "Turn on MFA for every active user. Suspend accounts that cannot enroll.",
    requirements: ["soc2 CC6.1", "iso27001 A.5.17", "iso27001 A.8.5"],
    body: "Example snapshot, Acme Robotics. Jordan P. and Casey R. are active without a second factor. This is example data.",
  },
  {
    key: "branch-protection",
    name: "Branch protection on the default branch",
    controlCode: "CM-11",
    outcome: "fail",
    detail: "The default branch is not protected: experiments.",
    remediation: "Require a pull request on the default branch of every repository.",
    requirements: ["soc2 CC8.1", "iso27001 A.8.4", "iso27001 A.8.32"],
    body: "Example snapshot, Acme Robotics. Repository experiments has an unprotected default branch. This is example data.",
  },
  {
    key: "offboarded-removed",
    name: "Offboarded users removed",
    controlCode: "HR-08",
    outcome: "fail",
    detail: "Leavers still have an active account: Sam Leaved.",
    remediation: "Remove or suspend the leaver in every connected system.",
    requirements: ["soc2 CC6.2", "soc2 CC6.3", "iso27001 A.6.5"],
    body: "Example snapshot, Acme Robotics. Sam Leaved is offboarded in the directory and still active on GitHub. This is example data.",
  },
  {
    key: "encryption-at-rest",
    name: "Encryption at rest",
    controlCode: "CR-02",
    outcome: "fail",
    detail: "Encryption at rest is off: marketing-site.",
    remediation: "Turn on default encryption for every bucket.",
    requirements: ["soc2 CC6.7", "iso27001 A.8.24"],
    body: "Example snapshot, Acme Robotics. Bucket marketing-site has no default encryption. This is example data.",
  },
  {
    key: "root-mfa",
    name: "Root account MFA",
    controlCode: "AC-07",
    outcome: "pass",
    detail: "1 root resources match.",
    remediation:
      "Enable MFA on the AWS root account and keep the device with the break-glass owners.",
    requirements: ["soc2 CC6.1", "iso27001 A.5.17"],
    body: "Example snapshot, Acme Robotics. The example root account has MFA. This is example data.",
  },
  {
    key: "cloudtrail-enabled",
    name: "CloudTrail enabled",
    controlCode: "LG-03",
    outcome: "pass",
    detail: "CloudTrail is logging.",
    remediation: "Create a trail that is logging, and keep it from being stopped.",
    requirements: ["soc2 CC7.2", "iso27001 A.8.15"],
    body: "Example snapshot, Acme Robotics. The management-events trail is logging. This is example data.",
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
  const source = exampleControls.controls as {
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
    const control = controls.find((item) => item.code === check.controlCode);
    if (check.outcome === "fail" && control) control.status = "failing";
    const checkId = id(nextId++);
    checks.push({
      id: checkId,
      key: check.key,
      name: check.name,
      controlCode: check.controlCode,
      outcome: check.outcome,
      detail: check.detail,
      remediation: check.remediation,
      requirements: [...check.requirements],
    });
    if (control) {
      control.tests.push({
        id: checkId,
        name: check.name,
        kind: "automated",
        outcome: check.outcome,
        detail: check.detail,
        remediation: check.remediation,
      });
    }
    const hash = await sha256(check.body);
    evidence.push({
      id: id(nextId++),
      title: check.name,
      controlCode: check.controlCode,
      source: "Example fixture",
      collectedAt: iso(addDays(now, -2)),
      integrity: "verified",
      sha256: hash,
      lockedUntil: iso(addDays(now, 365 * 3)),
      body: check.body,
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
    access.acknowledgements.push(
      ack(1, "Lena K.", "lena@acme.example", day(addDays(now, -3)), null, iso(addDays(now, -1))),
      ack(1, "Priya N.", "priya@acme.example", day(addDays(now, 10)), null, null),
    );
  }
  policies
    .find((item) => item.code === "POL-ISP")
    ?.acknowledgements.push(
      ack(1, "Dev T.", "dev@acme.example", day(addDays(now, -5)), iso(addDays(now, -4)), null),
      ack(
        1,
        "People Ops",
        "people@acme.example",
        day(addDays(now, -5)),
        iso(addDays(now, -3)),
        null,
      ),
    );

  const review = evidence.find((item) => item.controlCode === "AC-07");
  const requests: DemoRequest[] = [
    request(
      "REQ-14",
      "New-hire onboarding",
      "23 hires",
      5,
      "HR system",
      "2026-10-24",
      "Open",
      addDays(now, -1),
      null,
    ),
    request(
      "REQ-13",
      "Change tickets",
      "612 PRs",
      25,
      "GitHub",
      "2026-10-23",
      "Submitted",
      addDays(now, -2),
      {
        body: "The pull request list for the sample is attached.",
        respondedByName: "Priya N.",
        respondedAt: iso(addDays(now, -1)),
        evidenceTitle: null,
      },
    ),
    request(
      "REQ-12",
      "Terminations",
      "9 leavers",
      4,
      "Workspace",
      "2026-10-22",
      "Open",
      addDays(now, -3),
      null,
    ),
    request(
      "REQ-11",
      "Q2 access review",
      "Full population",
      null,
      "Manual",
      "2026-10-18",
      "Accepted",
      addDays(now, -8),
      {
        body: "The completed review is in the evidence library.",
        respondedByName: "Priya N.",
        respondedAt: iso(addDays(now, -6)),
        evidenceTitle: review?.title ?? null,
      },
    ),
    request(
      "REQ-10",
      "Backup restores",
      "6 restores",
      2,
      "Azure",
      "2026-10-16",
      "Accepted",
      addDays(now, -10),
      {
        body: "Both restore records are in the evidence library.",
        respondedByName: "Dev T.",
        respondedAt: iso(addDays(now, -7)),
        evidenceTitle: null,
      },
    ),
  ];

  const findings: DemoFinding[] = [
    {
      code: "F-02",
      title: "MFA gap",
      severity: "Exception",
      detail: "3 GitHub accounts lacked MFA for 11 days in July.",
      requirementCode: "CC6.1",
      managementResponse:
        "Those accounts are enrolled now. The access review will check MFA before the account is marked active.",
      responseByName: "Priya N.",
    },
    {
      code: "F-01",
      title: "Late access review",
      severity: "Observation",
      detail: "The Q2 review was completed 9 days after the due date.",
      requirementCode: "CC6.2",
      managementResponse:
        "The review calendar now reminds the owner a week before the due date. Evidence is on REQ-11.",
      responseByName: "Priya N.",
    },
  ];

  const governed = governance(now, controls);
  return {
    controls,
    evidence,
    checks,
    policies,
    requests,
    findings,
    ...governed,
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
    ownerName: "Priya N.",
    controlCodes: [control],
    versions: [
      {
        number: 1,
        state: "Approved",
        body,
        createdAt: iso(addDays(now, -20)),
        approvedAt: iso(addDays(now, -18)),
        approvedByName: "Chandra V.",
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
  const directory = [
    [
      "google",
      "priya",
      "Priya N.",
      "priya@acme.example",
      "Security",
      "Google",
      "Active",
      "Complete",
      -30,
      false,
      true,
    ],
    [
      "google",
      "sam",
      "Sam Leaved",
      "sam.leaved@acme.example",
      "Workforce",
      "Mixed",
      "Offboarded",
      "Overdue",
      -6,
      true,
      false,
    ],
    [
      "microsoft",
      "jordan",
      "Jordan P.",
      "jordan@acme.example",
      "Workforce",
      "Microsoft",
      "Active",
      "Assigned",
      20,
      false,
      true,
    ],
    [
      "microsoft",
      "lena",
      "Lena K.",
      "lena@acme.example",
      "People",
      "Microsoft",
      "Active",
      "Overdue",
      -6,
      false,
      true,
    ],
    [
      "github",
      "casey",
      "Casey R.",
      "casey@acme.example",
      "Engineering",
      "Mixed",
      "Active",
      "Assigned",
      20,
      false,
      true,
    ],
    [
      "github",
      "sam-gh",
      "Sam Leaved",
      "sam.leaved@acme.example",
      "Workforce",
      "Mixed",
      "Offboarded",
      "Overdue",
      -6,
      false,
      true,
    ],
  ] as const;
  const peopleMap = new Map<string, DemoPerson>();
  directory.forEach((row, index) => {
    const [provider, accountName, name, email, department, source, state, training, due, , active] =
      row;
    const person = peopleMap.get(email) ?? {
      id: `person-${index + 1}`,
      name,
      email,
      department,
      source,
      state,
      training,
      trainingDueOn: plus(due),
      tasks: [
        ...["account", "mfa", "policies", "training", "equipment"].map((taskCode) => ({
          kind: "Onboarding",
          taskCode,
          done: state !== "Offboarded" && taskCode !== "equipment",
        })),
        ...["disable", "assets", "access", "interview"].map((taskCode) => ({
          kind: "Offboarding",
          taskCode,
          done: false,
        })),
      ],
      accounts: [],
    };
    person.source = source;
    person.accounts.push({ provider, accountName, active });
    peopleMap.set(email, person);
  });
  const people = [...peopleMap.values()];
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
          ? "The example company does not run its own data center. The office building is covered by the landlord."
          : "This control applies to the way the company handles information.",
        implementation,
      };
    })
    .sort((left, right) => annexRank(left.code) - annexRank(right.code));
  const campaigns: DemoCampaign[] = [
    {
      id: "campaign-q4",
      name: "Q4 access review",
      dueOn: plus(12),
      state: "Open",
      completedAt: null,
      evidenceId: null,
      items: directory.map((row, index) => ({
        id: `review-${index + 1}`,
        personName: row[2],
        personEmail: row[3],
        provider: row[0],
        accountName: row[1],
        decision: "Pending",
      })),
    },
  ];
  return {
    risks: [
      risk(
        "R-01",
        "Public storage",
        "The marketing site bucket can be read by anyone on the internet.",
        4,
        5,
        "Priya N.",
        "priya@acme.example",
        "Mitigate",
        "Turn on Block Public Access and confirm the next collection.",
        plus(7),
        2,
        3,
        ["CFG-01"],
      ),
      risk(
        "R-02",
        "Missing MFA",
        "Two workforce accounts can sign in with a password alone.",
        4,
        4,
        "Priya N.",
        "priya@acme.example",
        "Mitigate",
        "Require a second factor before the account is marked active.",
        plus(14),
        2,
        2,
        ["AC-07"],
      ),
      risk(
        "R-03",
        "Leaver still has access",
        "An offboarded person still has a GitHub account.",
        3,
        5,
        "Lena K.",
        "lena@acme.example",
        "Mitigate",
        "Remove the GitHub account and record it in the access review.",
        plus(-2),
        1,
        2,
        ["HR-08"],
      ),
      risk(
        "R-04",
        "Unprotected branch",
        "The experiments repository can be changed without a second reviewer.",
        3,
        3,
        "Dev T.",
        "dev@acme.example",
        "Mitigate",
        "Protect the default branch.",
        plus(21),
        1,
        2,
        ["CM-11"],
      ),
      risk(
        "R-05",
        "Vendor concentration",
        "Source hosting and identity both sit with one or two suppliers.",
        2,
        4,
        "Chandra V.",
        "chandra@acme.example",
        "Transfer",
        "Keep the suppliers' SOC 2 reports and a named exit path.",
        plus(40),
        2,
        3,
        [],
      ),
    ],
    people,
    soa,
    vendors: [
      vendor(
        "vendor-github",
        "GitHub",
        "Source hosting",
        "Critical",
        "Source code and pull requests",
        "Approved",
        plus(60),
        3,
        "GitHub SOC 2 report",
        plus(-40),
      ),
      vendor(
        "vendor-google",
        "Google Workspace",
        "Email and directory",
        "High",
        "Mail, files, and the employee directory",
        "InReview",
        plus(10),
        3,
        "Workspace security whitepaper",
        plus(-80),
      ),
      vendor(
        "vendor-aws",
        "Amazon Web Services",
        "Production hosting",
        "Critical",
        "Customer application data in example buckets",
        "Approved",
        plus(90),
        4,
        "AWS SOC 2 report",
        plus(-20),
      ),
      vendor(
        "vendor-fonts",
        "Pixel Fonts",
        "Marketing site fonts",
        "Low",
        "No customer data",
        "Expired",
        plus(-12),
        1,
        "Font license",
        plus(-400),
      ),
    ],
    campaigns,
  };
}

function annexRank(code: string) {
  const parts = code.split(".").map((part) => Number(part.replace("A", "0")));
  return (parts[1] ?? 0) * 100 + (parts[2] ?? 0);
}

function vendor(
  id: string,
  name: string,
  service: string,
  criticality: string,
  dataAccess: string,
  reviewState: string,
  reviewDueOn: string,
  riskRating: number,
  documentTitle: string,
  issuedOn: string,
): DemoVendor {
  return {
    id,
    name,
    service,
    criticality,
    dataAccess,
    reviewState,
    reviewDueOn,
    riskRating,
    documents: [{ title: documentTitle, kind: "soc2", issuedOn }],
  };
}
