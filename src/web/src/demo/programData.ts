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
    plan: "Block public access and confirm the next collection.",
    suggestedControls: ["CFG-01", "CR-02"],
    category: "Cloud",
  },
  {
    code: "R-03",
    title: "Leaver still has access",
    statement: "A person who left can still reach a system.",
    likelihood: 4,
    impact: 5,
    treatment: "Mitigate",
    plan: "Remove lingering accounts within one business day.",
    suggestedControls: ["AC-07", "HR-06"],
    category: "Access",
  },
  {
    code: "LIB-01",
    title: "Key person dependency",
    statement: "One person holds an operational step that nobody else can run.",
    likelihood: 3,
    impact: 4,
    treatment: "Mitigate",
    plan: "Name a second person and write the step down.",
    suggestedControls: ["GV-02", "BC-01"],
    category: "People",
  },
  {
    code: "LIB-02",
    title: "Restore test missed",
    statement: "A backup exists, and the last restore practice is late.",
    likelihood: 3,
    impact: 4,
    treatment: "Mitigate",
    plan: "Run the restore and keep the result with the control.",
    suggestedControls: ["BC-04"],
    category: "Resilience",
  },
  {
    code: "LIB-03",
    title: "Subprocessor change",
    statement: "A supplier starts processing customer data without a fresh review.",
    likelihood: 3,
    impact: 5,
    treatment: "Mitigate",
    plan: "Review the supplier before the data moves.",
    suggestedControls: ["GV-04"],
    category: "Suppliers",
  },
  {
    code: "LIB-04",
    title: "Shared admin login",
    statement:
      "Several people sign in with one administrator account, so actions cannot be traced to a person.",
    likelihood: 3,
    impact: 4,
    treatment: "Mitigate",
    plan: "Issue named admin accounts and retire the shared one.",
    suggestedControls: ["AC-04", "LG-03"],
    category: "Access",
  },
  {
    code: "LIB-05",
    title: "Second factor not enforced",
    statement: "A sign-in path accepts a password alone for a system holding customer data.",
    likelihood: 4,
    impact: 5,
    treatment: "Mitigate",
    plan: "Turn on enforced multi-factor sign-in at the identity provider.",
    suggestedControls: ["AC-12"],
    category: "Access",
  },
  {
    code: "LIB-06",
    title: "Access creep after role change",
    statement: "People keep old permissions when they move teams.",
    likelihood: 3,
    impact: 3,
    treatment: "Mitigate",
    plan: "Review access at every role change and in the quarterly review.",
    suggestedControls: ["AC-15", "AC-07"],
    category: "Access",
  },
  {
    code: "LIB-07",
    title: "Production data in test",
    statement: "Copies of live customer records end up in lower environments.",
    likelihood: 3,
    impact: 4,
    treatment: "Mitigate",
    plan: "Mask or synthesize data before it leaves production.",
    suggestedControls: ["CM-11", "CR-05"],
    category: "Data",
  },
  {
    code: "LIB-08",
    title: "Unencrypted storage volume",
    statement: "A disk or database snapshot is stored without encryption at rest.",
    likelihood: 2,
    impact: 4,
    treatment: "Mitigate",
    plan: "Enforce default encryption through cloud policy.",
    suggestedControls: ["CR-02", "CFG-01"],
    category: "Cloud",
  },
  {
    code: "LIB-09",
    title: "Signing key leaked in code",
    statement: "A credential or private key is committed to a source repository.",
    likelihood: 3,
    impact: 5,
    treatment: "Mitigate",
    plan: "Scan commits for secrets and rotate anything found.",
    suggestedControls: ["CR-05", "CM-14"],
    category: "Engineering",
  },
  {
    code: "LIB-10",
    title: "Unreviewed code reaches production",
    statement: "A change ships without a second person looking at it.",
    likelihood: 3,
    impact: 4,
    treatment: "Mitigate",
    plan: "Require pull request approval before merge to the main branch.",
    suggestedControls: ["CM-11", "CM-14"],
    category: "Engineering",
  },
  {
    code: "LIB-11",
    title: "Critical patch delayed",
    statement: "A known exploited flaw stays open past the agreed fix window.",
    likelihood: 3,
    impact: 5,
    treatment: "Mitigate",
    plan: "Track patch age and escalate items past their window.",
    suggestedControls: ["VM-02", "VM-05"],
    category: "Vulnerability",
  },
  {
    code: "LIB-12",
    title: "Laptop without disk encryption",
    statement: "A company laptop is lost while its drive is readable.",
    likelihood: 3,
    impact: 4,
    treatment: "Mitigate",
    plan: "Enforce full-disk encryption through device management.",
    suggestedControls: ["EP-03", "EP-08"],
    category: "Endpoint",
  },
  {
    code: "LIB-13",
    title: "Malware on a workstation",
    statement: "A device runs without active malware protection.",
    likelihood: 3,
    impact: 4,
    treatment: "Mitigate",
    plan: "Deploy endpoint protection and alert on gaps.",
    suggestedControls: ["MAL-01", "EP-08"],
    category: "Endpoint",
  },
  {
    code: "LIB-14",
    title: "Logs missing during an incident",
    statement: "The records needed to investigate an event were never kept or were deleted early.",
    likelihood: 3,
    impact: 4,
    treatment: "Mitigate",
    plan: "Centralize logs and set retention to the policy period.",
    suggestedControls: ["LG-03", "LG-08"],
    category: "Monitoring",
  },
  {
    code: "LIB-15",
    title: "Alert fatigue",
    statement: "Real alerts are lost among noisy ones and nobody responds in time.",
    likelihood: 3,
    impact: 3,
    treatment: "Mitigate",
    plan: "Tune alert thresholds and name an on-call owner.",
    suggestedControls: ["LG-08", "IR-02"],
    category: "Monitoring",
  },
  {
    code: "LIB-16",
    title: "Incident response untested",
    statement: "The response plan has not been exercised this year.",
    likelihood: 3,
    impact: 4,
    treatment: "Mitigate",
    plan: "Run a tabletop exercise and record the lessons.",
    suggestedControls: ["IR-02"],
    category: "Incident",
  },
  {
    code: "LIB-17",
    title: "Breach notice deadline missed",
    statement: "A reportable incident is not disclosed within the regulatory window.",
    likelihood: 2,
    impact: 5,
    treatment: "Mitigate",
    plan: "Add notification timers and legal contacts to the response plan.",
    suggestedControls: ["IR-02", "PP-01"],
    category: "Privacy",
  },
  {
    code: "LIB-18",
    title: "Single region outage",
    statement: "The service runs in one cloud region with no failover path.",
    likelihood: 2,
    impact: 5,
    treatment: "Mitigate",
    plan: "Document recovery to a second region and test it.",
    suggestedControls: ["BC-01", "BC-04"],
    category: "Resilience",
  },
  {
    code: "LIB-19",
    title: "Ransomware reaches backups",
    statement: "An attacker can encrypt or delete backup copies along with production.",
    likelihood: 2,
    impact: 5,
    treatment: "Mitigate",
    plan: "Keep immutable or offline backup copies.",
    suggestedControls: ["BC-04", "MAL-01"],
    category: "Resilience",
  },
  {
    code: "LIB-20",
    title: "Cloud cost runaway",
    statement: "A misconfigured workload consumes budget fast enough to threaten operations.",
    likelihood: 2,
    impact: 3,
    treatment: "Mitigate",
    plan: "Set budget alerts and spending caps per account.",
    suggestedControls: ["CFG-01"],
    category: "Cloud",
  },
  {
    code: "LIB-21",
    title: "Open network port",
    statement: "A management port is reachable from the internet.",
    likelihood: 3,
    impact: 5,
    treatment: "Mitigate",
    plan: "Restrict inbound rules and review security groups monthly.",
    suggestedControls: ["CFG-01", "VM-05"],
    category: "Cloud",
  },
  {
    code: "LIB-22",
    title: "Excessive cloud permissions",
    statement: "Service identities hold wildcard permissions they never use.",
    likelihood: 3,
    impact: 4,
    treatment: "Mitigate",
    plan: "Apply least privilege and remove unused grants.",
    suggestedControls: ["AC-04", "AC-15"],
    category: "Cloud",
  },
  {
    code: "LIB-23",
    title: "Supplier security unknown",
    statement: "A critical supplier has no recent security assurance on file.",
    likelihood: 3,
    impact: 4,
    treatment: "Mitigate",
    plan: "Collect a report or questionnaire before renewal.",
    suggestedControls: ["GV-04"],
    category: "Suppliers",
  },
  {
    code: "LIB-24",
    title: "Supplier outage",
    statement: "A key supplier becomes unavailable and there is no fallback.",
    likelihood: 2,
    impact: 4,
    treatment: "Transfer",
    plan: "Agree service credits and identify a fallback supplier.",
    suggestedControls: ["GV-04", "BC-01"],
    category: "Suppliers",
  },
  {
    code: "LIB-25",
    title: "Phishing compromise",
    statement: "An employee enters credentials on a fake sign-in page.",
    likelihood: 4,
    impact: 4,
    treatment: "Mitigate",
    plan: "Run awareness training and phishing simulations.",
    suggestedControls: ["HR-08", "AC-12"],
    category: "People",
  },
  {
    code: "LIB-26",
    title: "Background checks skipped",
    statement: "Someone with access to sensitive data starts before screening completes.",
    likelihood: 2,
    impact: 4,
    treatment: "Mitigate",
    plan: "Block account creation until screening clears.",
    suggestedControls: ["HR-01"],
    category: "People",
  },
  {
    code: "LIB-27",
    title: "Policies not acknowledged",
    statement: "Staff have not confirmed they read the current security policies.",
    likelihood: 3,
    impact: 2,
    treatment: "Mitigate",
    plan: "Send acknowledgement reminders and track completion.",
    suggestedControls: ["GV-01", "HR-08"],
    category: "Governance",
  },
  {
    code: "LIB-28",
    title: "Policy out of date",
    statement: "A policy has not been reviewed within its review cycle.",
    likelihood: 3,
    impact: 3,
    treatment: "Mitigate",
    plan: "Assign owners and calendar the annual review.",
    suggestedControls: ["GV-01", "GV-03"],
    category: "Governance",
  },
  {
    code: "LIB-29",
    title: "Risk register neglected",
    statement: "Risks are recorded once and never reassessed.",
    likelihood: 3,
    impact: 3,
    treatment: "Mitigate",
    plan: "Set a review cadence for every risk and report overdue items.",
    suggestedControls: ["RM-01", "GV-02"],
    category: "Governance",
  },
  {
    code: "LIB-30",
    title: "Personal data kept too long",
    statement: "Customer records stay beyond the stated retention period.",
    likelihood: 3,
    impact: 4,
    treatment: "Mitigate",
    plan: "Automate deletion against the retention schedule.",
    suggestedControls: ["PP-01", "CR-05"],
    category: "Privacy",
  },
  {
    code: "LIB-31",
    title: "Data subject request missed",
    statement: "A request to access or delete personal data is not answered on time.",
    likelihood: 2,
    impact: 4,
    treatment: "Mitigate",
    plan: "Route requests to a tracked queue with due dates.",
    suggestedControls: ["PP-01"],
    category: "Privacy",
  },
  {
    code: "LIB-32",
    title: "Cross-border transfer without basis",
    statement: "Personal data moves to another jurisdiction without a lawful transfer mechanism.",
    likelihood: 2,
    impact: 5,
    treatment: "Mitigate",
    plan: "Map transfers and sign the needed contractual clauses.",
    suggestedControls: ["PP-01", "GV-04"],
    category: "Privacy",
  },
  {
    code: "LIB-33",
    title: "Payment card data stored",
    statement: "Card numbers are captured in logs or tickets outside the payment flow.",
    likelihood: 2,
    impact: 5,
    treatment: "Avoid",
    plan: "Tokenize at the edge and scrub logs for card patterns.",
    suggestedControls: ["CR-02", "LG-03"],
    category: "Payments",
  },
  {
    code: "LIB-34",
    title: "Office access uncontrolled",
    statement: "Visitors can reach areas with equipment or paper records unescorted.",
    likelihood: 2,
    impact: 3,
    treatment: "Mitigate",
    plan: "Badge access and a visitor log at reception.",
    suggestedControls: ["PH-02"],
    category: "Physical",
  },
  {
    code: "LIB-35",
    title: "Model output harms a customer",
    statement: "An AI feature produces incorrect or biased output that a customer relies on.",
    likelihood: 3,
    impact: 4,
    treatment: "Mitigate",
    plan: "Add human review for high-impact outputs and monitor quality.",
    suggestedControls: ["GV-03", "RM-01"],
    category: "AI",
  },
  {
    code: "LIB-36",
    title: "Training data provenance unclear",
    statement: "A model is trained on data whose source or license is unknown.",
    likelihood: 3,
    impact: 4,
    treatment: "Mitigate",
    plan: "Record dataset lineage and approvals before training.",
    suggestedControls: ["GV-03", "PP-01"],
    category: "AI",
  },
  {
    code: "LIB-37",
    title: "Prompt injection",
    statement: "Untrusted input steers an AI assistant into leaking data or taking actions.",
    likelihood: 3,
    impact: 4,
    treatment: "Mitigate",
    plan: "Isolate tools, filter inputs and test adversarial prompts.",
    suggestedControls: ["VM-05", "CM-14"],
    category: "AI",
  },
  {
    code: "LIB-38",
    title: "Plant network reachable from office",
    statement: "Operational technology shares a flat network with corporate devices.",
    likelihood: 2,
    impact: 5,
    treatment: "Mitigate",
    plan: "Segment the plant network and control the conduits.",
    suggestedControls: ["CFG-01", "AC-04"],
    category: "Operational technology",
  },
  {
    code: "LIB-39",
    title: "Insider data download",
    statement: "A trusted user exports large volumes of customer data before leaving.",
    likelihood: 2,
    impact: 5,
    treatment: "Mitigate",
    plan: "Alert on bulk exports and review leaver activity.",
    suggestedControls: ["LG-08", "HR-06"],
    category: "People",
  },
  {
    code: "LIB-40",
    title: "Cyber insurance gap",
    statement: "A serious incident would cost more than current insurance covers.",
    likelihood: 2,
    impact: 4,
    treatment: "Transfer",
    plan: "Review coverage limits against quantified loss estimates.",
    suggestedControls: ["RM-01", "GV-02"],
    category: "Governance",
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
    const age = hired ? Math.round((now.getTime() - hired.getTime()) / 86400000) : 400;
    let background = "NotStarted";
    let backgroundOn: string | null = null;
    let backgroundProvider = "";
    let backgroundLink = "";
    let backgroundExcluded = false;
    let backgroundExclusionReason = "";
    if (person.email.startsWith("avery.dunn@")) {
      background = "Excluded";
      backgroundExcluded = true;
      backgroundExclusionReason = "The staffing agency confirmed screening in the contract file.";
      backgroundProvider = "Agency";
      backgroundOn = person.hireOn;
    } else if (person.state === "Active" && age <= 10) {
      background = "Pending";
    } else if (person.state === "Active" && hired && person.hireOn) {
      background = "Clear";
      backgroundOn = day(addDays(hired, 5));
      backgroundProvider = age % 2 === 0 ? "Checkr" : "Sterling";
      backgroundLink = `https://checks.example/${person.email}`;
    }
    return {
      ...person,
      mfaEnrolled: personMfa(person.email),
      background,
      backgroundOn,
      backgroundProvider,
      backgroundLink,
      backgroundEvidenceId: null,
      backgroundExcluded,
      backgroundExclusionReason,
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
