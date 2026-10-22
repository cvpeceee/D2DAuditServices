import { exampleAccounts } from "./accounts";
import { assetSources, company, personMfa, seedDevices } from "./client";
import type { DemoCampaign, DemoPerson, DemoRisk, DemoVendor } from "./catalog";

export const annualCode = "SEC-ANNUAL";
export const lmsCode = "SEC-LMS";

export const slides = [
  {
    title: "Why this matters",
    body: "Customer data stays in the approved warehouse. A personal drive, a chat thread, and a public bucket are not a warehouse.",
  },
  {
    title: "What you do",
    body: "Use the company account, keep a second factor on, and finish this module once a year.",
  },
  {
    title: "How you report",
    body: "Tell Security the same day you see a phishing message, a lost laptop, or a file in the wrong place.",
  },
];

export const questions = [
  {
    prompt: "Who do you tell about a suspected phishing message?",
    choices: ["Security", "A customer", "Nobody"],
    answer: 0,
  },
  {
    prompt: "When do you finish this module?",
    choices: ["Every year", "Never", "Only after an incident"],
    answer: 0,
  },
  {
    prompt: "Where do customer files belong?",
    choices: ["The approved warehouse", "A personal drive", "Email"],
    answer: 0,
  },
];

export const library = [
  {
    code: "R-01",
    title: "Public marketing site",
    statement: "A public bucket can expose files that were not meant to be public.",
    likelihood: 4,
    impact: 5,
    treatment: "Mitigate",
  },
  {
    code: "R-03",
    title: "Leaver still has access",
    statement: "A person who left can still reach a system.",
    likelihood: 4,
    impact: 5,
    treatment: "Mitigate",
  },
  {
    code: "LIB-01",
    title: "Key person dependency",
    statement: "One person holds an operational step that nobody else can run.",
    likelihood: 3,
    impact: 4,
    treatment: "Mitigate",
    plan: "Name a second person and write the step down.",
  },
  {
    code: "LIB-02",
    title: "Restore test missed",
    statement: "A backup exists, and the last restore practice is late.",
    likelihood: 3,
    impact: 4,
    treatment: "Mitigate",
    plan: "Run the restore and keep the result with the control.",
  },
  {
    code: "LIB-03",
    title: "Subprocessor change",
    statement: "A supplier starts processing customer data without a fresh review.",
    likelihood: 3,
    impact: 5,
    treatment: "Mitigate",
    plan: "Review the supplier before the data moves.",
  },
];

export type DemoAsset = {
  id: string;
  name: string;
  type: string;
  source: string;
  ownerName: string;
  ownerEmail: string;
  environment: string;
  classification: string;
  inScope: boolean;
  lastSeen: string;
};

export type DemoWorkTask = {
  id: string;
  title: string;
  linkedKind: string;
  linkedCode: string;
  ownerName: string;
  ownerEmail: string;
  dueOn: string;
  status: string;
  recurrence: string;
};

export type DemoModule = {
  id: string;
  code: string;
  title: string;
  type: string;
  assignedGroup: string;
  dueOn: string;
  externalUrl: string | null;
};

export type DemoCompletion = {
  id: string;
  moduleId: string;
  personEmail: string;
  completedAt: string;
  score: number;
  evidenceId: string | null;
};

export type DemoMember = {
  id: string;
  name: string;
  email: string;
  role: string;
  active: boolean;
};

export type DemoGrant = {
  id: string;
  email: string;
  name: string;
  auditName: string;
  firmName: string;
  periodStart: string;
  periodEnd: string;
  accessExpiresOn: string;
  frameworks: string[];
};

function day(date: Date) {
  return date.toISOString().slice(0, 10);
}

function addDays(date: Date, days: number) {
  const next = new Date(date);
  next.setUTCDate(next.getUTCDate() + days);
  return next;
}

function assetType(kind: string) {
  if (kind === "bucket") return "Bucket";
  if (kind === "repository") return "Repository";
  if (kind === "root" || kind === "subscription" || kind === "project") return "Server";
  if (kind === "database") return "Database";
  return "";
}

function environmentOf(name: string) {
  const value = name.toLowerCase();
  if (value.includes("staging") || value.includes("experiment") || value === "analytics")
    return "staging";
  return "production";
}

function classificationOf(name: string, kind: string, isPublic: boolean) {
  if (isPublic) return "Public";
  const value = name.toLowerCase();
  if (value.includes("customer") || value.includes("warehouse")) return "Restricted";
  return kind === "repository" ? "Internal" : "Confidential";
}

function questionsFor(reviewState: string, criticality: string) {
  const report =
    reviewState === "Approved"
      ? "Yes. The latest report is on file."
      : "No current report is on file.";
  const customer = criticality === "Critical" || criticality === "High" ? "Yes" : "No";
  return [
    { prompt: "Does this supplier process customer data?", answer: customer },
    { prompt: "Is a current assurance report on file?", answer: report },
    {
      prompt: "Is there a contract with a security addendum?",
      answer: reviewState === "NotStarted" ? "Not yet" : "Yes",
    },
    {
      prompt: "What is the exit path?",
      answer: "The data can be exported and the account closed within 30 days.",
    },
  ];
}

export function buildProgram(
  people: DemoPerson[],
  campaigns: DemoCampaign[],
  vendors: DemoVendor[],
  now: Date,
) {
  const devices = new Set(seedDevices.map((item) => item.ownerEmail.toLowerCase()));
  const enriched = people.map((person) => {
    const hired = person.hireOn ? new Date(`${person.hireOn}T00:00:00Z`) : null;
    const tenured = hired ? hired.getTime() + 30 * 86400000 <= now.getTime() : false;
    let background = "NotStarted";
    let backgroundOn: string | null = null;
    if (person.state === "Active" && !tenured) background = "Pending";
    else if (tenured) {
      background = "Clear";
      backgroundOn = person.hireOn
        ? day(addDays(new Date(`${person.hireOn}T00:00:00Z`), 14))
        : null;
    }
    return {
      ...person,
      mfaEnrolled: personMfa(person.email),
      background,
      backgroundOn,
      device: devices.has(person.email.toLowerCase()) ? "Reporting" : "Missing",
      statusReason: person.email.startsWith("sam.leaved@") ? "Leave date confirmed by People." : "",
      trainingCompletedOn: person.training === "Complete" ? day(addDays(now, -40)) : null,
    };
  });

  const seen = addDays(now, -1).toISOString();
  const domain = company.domain;
  const assets: DemoAsset[] = [];
  for (const source of assetSources()) {
    const type = assetType(source.kind);
    if (!type) continue;
    const repo = source.kind === "repository";
    assets.push({
      id: `asset-${assets.length + 1}`,
      name: source.name,
      type,
      source: source.provider,
      ownerName: repo ? "Dev Patel" : "Priya Nair",
      ownerEmail: repo ? `dev.patel@${domain}` : `priya.nair@${domain}`,
      environment: environmentOf(source.name),
      classification: classificationOf(source.name, source.kind, source.isPublic),
      inScope: source.name !== "build-artifacts" && source.name !== "analytics",
      lastSeen: seen,
    });
  }
  for (const cloud of ["aws", "azure", "gcp"]) {
    assets.push({
      id: `asset-${assets.length + 1}`,
      name: `${cloud} warehouse`,
      type: "Database",
      source: cloud,
      ownerName: "Priya Nair",
      ownerEmail: `priya.nair@${domain}`,
      environment: "production",
      classification: "Restricted",
      inScope: true,
      lastSeen: seen,
    });
  }
  const saas = [
    ["Google Workspace", "google", "Lena Cho", `lena.cho@${domain}`],
    ["Microsoft 365", "microsoft", "Lena Cho", `lena.cho@${domain}`],
    ["GitHub", "github", "Dev Patel", `dev.patel@${domain}`],
    [company.name, "manual", "Chandra Vell", `chandra.vell@${domain}`],
  ] as const;
  for (const row of saas) {
    assets.push({
      id: `asset-${assets.length + 1}`,
      name: row[0],
      type: "Saas",
      source: row[1],
      ownerName: row[2],
      ownerEmail: row[3],
      environment: "production",
      classification: row[1] === "manual" ? "Restricted" : "Confidential",
      inScope: true,
      lastSeen: seen,
    });
  }
  seedDevices.forEach((device) => {
    assets.push({
      id: `asset-${assets.length + 1}`,
      name: `${device.ownerName} ${device.platform}`,
      type: "Laptop",
      source: "agent",
      ownerName: device.ownerName,
      ownerEmail: device.ownerEmail,
      environment: "production",
      classification: "Internal",
      inScope: true,
      lastSeen: new Date(`${day(addDays(now, device.seenOffset))}T00:00:00Z`).toISOString(),
    });
  });

  const due = day(addDays(now, 30));
  const modules: DemoModule[] = [
    {
      id: "module-annual",
      code: annualCode,
      title: "Annual security awareness",
      type: "BuiltIn",
      assignedGroup: "Everyone",
      dueOn: due,
      externalUrl: null,
    },
    {
      id: "module-lms",
      code: lmsCode,
      title: "Role-specific course",
      type: "External",
      assignedGroup: "Everyone",
      dueOn: due,
      externalUrl: `https://learn.${domain}/annual`,
    },
  ];
  const completions: DemoCompletion[] = [];
  enriched.forEach((person, index) => {
    if (person.training !== "Complete") return;
    completions.push({
      id: `completion-${completions.length + 1}`,
      moduleId: "module-annual",
      personEmail: person.email,
      completedAt: addDays(now, -40).toISOString(),
      score: questions.length,
      evidenceId: null,
    });
    if (index % 7 !== 0) {
      completions.push({
        id: `completion-${completions.length + 1}`,
        moduleId: "module-lms",
        personEmail: person.email,
        completedAt: addDays(now, -20).toISOString(),
        score: 1,
        evidenceId: null,
      });
    }
  });

  const task = (
    title: string,
    linkedKind: string,
    linkedCode: string,
    ownerName: string,
    owner: string,
    offset: number,
    status: string,
    recurrence: string,
  ): DemoWorkTask => ({
    id: `task-${title.length}-${offset}`,
    title,
    linkedKind,
    linkedCode,
    ownerName,
    ownerEmail: `${owner}@${domain}`,
    dueOn: day(addDays(now, offset)),
    status,
    recurrence,
  });
  const workTasks = [
    task("Close the MFA gap", "Test", "mfa", "Priya Nair", "priya.nair", -3, "Open", "None"),
    task("Review the leaver risk", "Risk", "R-03", "Priya Nair", "priya.nair", -2, "Open", "None"),
    task(
      "Refresh the Pixel Fonts questionnaire",
      "Vendor",
      "Pixel Fonts",
      "Lena Cho",
      "lena.cho",
      -12,
      "Open",
      "Annual",
    ),
    task(
      "Refresh the information security policy",
      "Policy",
      "POL-ISP",
      "Priya Nair",
      "priya.nair",
      14,
      "Open",
      "Annual",
    ),
    task(
      "Confirm the marketing site control",
      "Control",
      "CFG-01",
      "Marcus Lee",
      "marcus.lee",
      5,
      "InProgress",
      "None",
    ),
    task(
      "Prepare the next access review",
      "Control",
      "AC-07",
      "Chandra Vell",
      "chandra.vell",
      20,
      "Open",
      "Quarterly",
    ),
    task(
      "File the last vendor review",
      "Vendor",
      "Mailfold",
      "Lena Cho",
      "lena.cho",
      -40,
      "Done",
      "Annual",
    ),
    task(
      "Protect the experiments branch",
      "Control",
      "CM-11",
      "Dev Patel",
      "dev.patel",
      -2,
      "Open",
      "None",
    ),
  ];

  const reviewed = vendors.map((vendor) => {
    const decision =
      vendor.reviewState === "Approved"
        ? "Approved"
        : vendor.reviewState === "Expired"
          ? "Renew"
          : "Pending";
    const residualLikelihood = Math.min(5, Math.max(1, vendor.riskRating - 1));
    const residualImpact = Math.min(5, Math.max(1, vendor.riskRating));
    const residualScore = residualLikelihood * residualImpact;
    return {
      ...vendor,
      decision,
      residualLikelihood,
      residualImpact,
      residualScore,
      residualBand:
        residualScore >= 15
          ? "critical"
          : residualScore >= 8
            ? "high"
            : residualScore >= 4
              ? "medium"
              : "low",
      questions: questionsFor(vendor.reviewState, vendor.criticality),
    };
  });

  const drafted: DemoCampaign[] = [
    ...campaigns.map((campaign) => ({
      ...campaign,
      providers: [...new Set(campaign.items.map((item) => item.provider))],
      items: campaign.items.map((item) => ({
        ...item,
        needsRemediation: item.decision === "Revoked",
        remediationNote:
          item.decision === "Revoked" ? "Remove the account before the review is submitted." : "",
      })),
    })),
    {
      id: "campaign-draft",
      name: "Q1 contractor review",
      dueOn: day(addDays(now, 40)),
      state: "Draft",
      completedAt: null,
      evidenceId: null,
      providers: ["github"],
      items: [],
    },
  ];

  const members: DemoMember[] = [
    ...exampleAccounts.map((account, index) => ({
      id: `member-${index + 1}`,
      name: account.name,
      email: account.email,
      role: account.role,
      active: true,
    })),
    {
      id: "member-viewer",
      name: "Riley Quinn",
      email: `riley.quinn@${domain}`,
      role: "Viewer",
      active: true,
    },
    {
      id: "member-former",
      name: "Nia Park",
      email: `nia.park@${domain}`,
      role: "Editor",
      active: false,
    },
  ];
  const grants: DemoGrant[] = [
    {
      id: "grant-soc2",
      email: "auditor@northgate.example",
      name: "Rachel Ortiz",
      auditName: "SOC 2 Type II",
      firmName: "Northgate Assurance LLP",
      periodStart: "2026-04-01",
      periodEnd: "2026-09-30",
      accessExpiresOn: "2026-12-15",
      frameworks: ["soc2"],
    },
  ];

  return {
    people: enriched,
    vendors: reviewed,
    campaigns: drafted,
    assets,
    workTasks,
    modules,
    completions,
    members,
    grants,
    sso: [{ domain, connected: false, organizationId: null as string | null }],
  };
}

export function withRiskApproval(risks: DemoRisk[], now: Date) {
  return risks.map((risk) => ({
    ...risk,
    approved: risk.code !== "R-03",
    approvedBy: risk.code === "R-03" ? "" : "Chandra Vell",
    approvedOn: risk.code === "R-03" ? null : day(addDays(now, -12)),
    libraryCode: risk.code,
  }));
}
