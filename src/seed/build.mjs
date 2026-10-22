import { writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const domain = "harborline.example";
const firstNames = [
  "Avery", "Blake", "Cameron", "Drew", "Ellis", "Finley", "Gray", "Harper", "Indigo", "Jules",
  "Kai", "Logan", "Morgan", "Noel", "Oakley", "Parker", "Quinn", "Reese", "Sawyer", "Taylor",
  "Uma", "Val", "Winter", "Yael", "Zion", "Ada", "Ben", "Cora", "Diego", "Eden",
  "Fran", "Gio", "Hana", "Ivan", "Jo", "Kit", "Leif", "Mina", "Nico", "Oma",
];
const lastNames = [
  "Adler", "Berg", "Cho", "Diaz", "Ellis", "Frost", "Garcia", "Hoff", "Ito", "Jones",
  "Khan", "Lopez", "Meyer", "Novak", "Owens", "Patel", "Quill", "Rossi", "Shah", "Tran",
  "Ueda", "Voss", "Walsh", "Xu", "Young", "Zimmer", "Abe", "Bose", "Cruz", "Dunn",
];
const departments = [
  ["Engineering", "Engineer", 46],
  ["Product", "Product manager", 10],
  ["Design", "Designer", 6],
  ["Security", "Security engineer", 4],
  ["People", "People partner", 6],
  ["Finance", "Accountant", 8],
  ["Sales", "Account executive", 16],
  ["Success", "Success manager", 10],
  ["Marketing", "Marketer", 6],
];

function mulberry32(seed) {
  let state = seed >>> 0;
  return () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let next = state;
    next = Math.imul(next ^ (next >>> 15), next | 1);
    next ^= next + Math.imul(next ^ (next >>> 7), next | 61);
    return ((next ^ (next >>> 14)) >>> 0) / 4294967296;
  };
}

const random = mulberry32(12046);
const pick = (items) => items[Math.floor(random() * items.length)];

const named = [
  person("Chandra", "Vell", "Security", "Chief security officer", -980, null, ["google", "github"], true, "Complete", -40),
  person("Priya", "Nair", "Security", "Security lead", -860, null, ["google", "github"], true, "Complete", -20),
  person("Lena", "Cho", "People", "People lead", -740, null, ["google", "microsoft"], true, "Overdue", -6),
  person("Marcus", "Lee", "Engineering", "Engineering manager", -1100, null, ["google", "github"], true, "Complete", -15),
  person("Dev", "Patel", "Engineering", "Staff engineer", -640, null, ["google", "github"], true, "Assigned", 18),
  person("Jordan", "Pell", "Success", "Success manager", -220, null, ["microsoft"], false, "Assigned", 20),
  person("Casey", "Ruiz", "Engineering", "Engineer", -180, null, ["github"], false, "Assigned", 12),
  person("Sam", "Leaved", "Engineering", "Engineer", -500, -20, ["google", "github"], true, "Overdue", -6, { github: true }),
];

function slug(value) {
  return value.toLowerCase().replace(/[^a-z]+/g, "");
}

function person(first, last, department, role, hireOffset, leaveOffset, providers, mfa, training, trainingDueOffset, lingering = {}) {
  const email = `${slug(first)}.${slug(last)}@${domain}`;
  return {
    first,
    last,
    name: `${first} ${last}`,
    email,
    department,
    role,
    hireOffset,
    leaveOffset,
    providers,
    mfa,
    training,
    trainingDueOffset,
    lingering,
  };
}

const used = new Set(named.map((item) => item.email));
const generated = [];
let quota = departments.flatMap(([department, role, count]) => Array.from({ length: count }, () => [department, role]));
quota = quota.slice(0, 112);
quota.forEach(([department, role], index) => {
  let first = pick(firstNames);
  let last = pick(lastNames);
  let email = `${slug(first)}.${slug(last)}@${domain}`;
  let guard = 0;
  while (used.has(email) && guard < 20) {
    first = pick(firstNames);
    last = pick(lastNames);
    email = `${slug(first)}.${slug(last)}@${domain}`;
    guard += 1;
  }
  if (used.has(email)) email = `${slug(first)}.${slug(last)}${index}@${domain}`;
  used.add(email);
  const leaver = index < 8;
  const lingeringGithub = leaver && index < 2;
  const providers = department === "Engineering" || department === "Security" || department === "Product"
    ? ["google", "github"]
    : department === "Sales" || department === "Success" || department === "Marketing"
      ? ["microsoft"]
      : ["google"];
  generated.push({
    name: `${first} ${last}`,
    email,
    department,
    role,
    hireOffset: -40 - Math.floor(random() * 1600),
    leaveOffset: leaver ? -3 - Math.floor(random() * 80) : null,
    providers,
    mfa: true,
    training: leaver ? "Overdue" : random() > 0.25 ? "Complete" : "Assigned",
    trainingDueOffset: leaver ? -8 : random() > 0.25 ? -30 : 16,
    lingering: lingeringGithub ? { github: true } : {},
  });
});

const people = [...named, ...generated];
if (people.length !== 120) throw new Error(`expected 120 people, got ${people.length}`);

const resources = [];
resources.push(resource("aws", "root", "210987654321", "root:210987654321", { mfa: "true" }));
for (const bucket of [
  ["logs-prod", false, true],
  ["customer-files", false, true],
  ["backups", false, true],
  ["marketing-site", true, false],
  ["build-artifacts", false, true],
]) {
  resources.push(resource("aws", "bucket", bucket[0], bucket[0], { public: String(bucket[1]), encrypted: String(bucket[2]) }));
}
resources.push(resource("aws", "trail", "management-events", "management-events", { enabled: "true" }));
for (const iam of ["harborline-deploy", "harborline-read", "break-glass", "billing-audit"]) {
  resources.push(resource("aws", "iam-user", iam, iam, { mfa: iam === "break-glass" ? "true" : "false" }));
}
for (const repo of [
  ["harborline-api", true],
  ["harborline-web", true],
  ["harborline-worker", true],
  ["harborline-infra", true],
  ["harborline-docs", true],
  ["experiments", false],
]) {
  resources.push(resource("github", "repository", repo[0], repo[0], { protected: String(repo[1]) }));
}

people.forEach((item) => {
  item.providers.forEach((provider) => {
    const lingering = Boolean(item.lingering[provider]);
    const offboarded = item.leaveOffset !== null && !lingering;
    const active = item.leaveOffset === null || lingering;
    const account = provider === "github" ? slug(item.name).slice(0, 18) : slug(item.email.split("@")[0]);
    resources.push(resource(provider, "user", `${provider}:${item.email}`, item.name, {
      email: item.email,
      mfa: item.mfa ? "true" : "false",
      offboarded: offboarded ? "true" : "false",
      status: active ? "active" : "suspended",
      department: item.department,
      role: item.role,
    }));
  });
});

function resource(provider, kind, externalId, name, attributes) {
  return { provider, kind, externalId, name, attributes };
}

const risks = [
  risk("R-01", "Public marketing site", "The marketing site bucket can be read by anyone on the internet.", 4, 5, "Priya Nair", "priya.nair@harborline.example", "Mitigate", "Turn on Block Public Access and confirm the next collection.", 7, 2, 3, ["CFG-01"]),
  risk("R-02", "Missing MFA", "Two workforce accounts can sign in with a password alone.", 4, 4, "Priya Nair", "priya.nair@harborline.example", "Mitigate", "Require a second factor before the account is marked active.", 14, 2, 2, ["AC-07"]),
  risk("R-03", "Leaver still has access", "An offboarded person still has a GitHub account.", 3, 5, "Lena Cho", "lena.cho@harborline.example", "Mitigate", "Remove the GitHub account and record it in the access review.", -2, 1, 2, ["HR-08"]),
  risk("R-04", "Unprotected branch", "The experiments repository can be changed without a second reviewer.", 3, 3, "Dev Patel", "dev.patel@harborline.example", "Mitigate", "Protect the default branch.", 21, 1, 2, ["CM-11"]),
  risk("R-05", "Vendor concentration", "Source hosting and identity both sit with one or two suppliers.", 2, 4, "Chandra Vell", "chandra.vell@harborline.example", "Transfer", "Keep the suppliers' reports and a named exit path.", 40, 2, 3, []),
  risk("R-06", "Backup restore untested", "The last restore drill is older than the policy allows.", 2, 4, "Marcus Lee", "marcus.lee@harborline.example", "Mitigate", "Run a restore of the customer-files bucket and file the record.", 9, 2, 2, ["BC-01"]),
  risk("R-07", "Logging gap on the sandbox", "The experiments repository has no required review, so a change can ship without a trace in the ticket.", 3, 3, "Dev Patel", "dev.patel@harborline.example", "Accept", "Accept until the repository is archived. Review again next quarter.", 28, 3, 3, ["LG-03"]),
  risk("R-08", "Training overdue", "Several people are past the security-training date.", 3, 2, "Lena Cho", "lena.cho@harborline.example", "Mitigate", "Send the reminder and record completion.", 5, 2, 1, ["HR-06"]),
];

function risk(code, title, statement, likelihood, impact, ownerName, ownerEmail, treatment, treatmentPlan, reviewOffset, residualLikelihood, residualImpact, controlCodes) {
  return { code, title, statement, likelihood, impact, ownerName, ownerEmail, treatment, treatmentPlan, reviewOffset, residualLikelihood, residualImpact, controlCodes };
}

const vendors = [
  vendor("GitHub", "Source hosting", "Critical", "Source code and pull requests", "Approved", 60, 3, "GitHub SOC 2 report", -40),
  vendor("Google Workspace", "Email and directory", "High", "Mail, files, and the employee directory", "InReview", 10, 3, "Workspace security whitepaper", -80),
  vendor("Amazon Web Services", "Production hosting", "Critical", "Customer application data in the example buckets", "Approved", 90, 4, "AWS SOC 2 report", -20),
  vendor("Pixel Fonts", "Marketing site fonts", "Low", "No customer data", "Expired", -12, 1, "Font license", -400),
  vendor("Northline Payments", "Subscription billing", "Critical", "Customer billing details", "Approved", 45, 4, "Northline SOC 2 report", -70),
  vendor("Pagerly", "On-call paging", "High", "Incident contact names", "InReview", 8, 2, "Pagerly security overview", -30),
  vendor("Mailfold", "Product email", "Medium", "Customer notification addresses", "Approved", 70, 2, "Mailfold SOC 2 report", -100),
  vendor("Frost Warehouse", "Product analytics", "High", "Pseudonymous product events", "NotStarted", 15, 3, "Frost security questionnaire", -5),
];

function vendor(name, service, criticality, dataAccess, reviewState, reviewOffset, riskRating, documentTitle, issuedOffset) {
  return {
    name,
    service,
    criticality,
    dataAccess,
    reviewState,
    reviewOffset,
    riskRating,
    documents: [{ title: documentTitle, kind: "soc2", issuedOffset }],
  };
}

const history = Array.from({ length: 27 }, (_, index) => {
  const week = index - 26;
  const passing = week <= -16 ? 6 : week <= -8 ? 4 : week <= -4 ? 3 : 2;
  return { week, passing, failing: 7 - passing };
});

const notifications = [
  note("Leaver still has GitHub access", "Sam Leaved is offboarded and still has an active GitHub account.", "/people", -1),
  note("Marketing site is public", "Block Public Access is off for marketing-site.", "/controls/CFG-01", -2),
  note("Vendor review expired", "Pixel Fonts is past its review date.", "/vendors", -3),
  note("Security training overdue", "Lena Cho is past the training date.", "/people", -4),
  note("Access review is open", "The Q4 privileged access review still has pending accounts.", "/access-reviews", -1),
  note("Auditor asked for terminations", "REQ-12 is open and due this week.", "/auditor", -2),
  note("Policy acknowledgements outstanding", "The information security policy has people past the due date.", "/policies", -5),
  note("Collection finished", "The example integrations collected resources and updated the automated tests.", "/integrations", 0),
];

function note(title, body, href, dayOffset) {
  return { title, body, href, dayOffset };
}

const devices = people.filter((item) => item.leaveOffset === null && item.hireOffset < -21).slice(0, 86).map((item, index) => ({
  ownerName: item.name,
  ownerEmail: item.email,
  platform: index % 5 === 0 ? "macOS" : index % 2 === 0 ? "Windows" : "Linux",
  serial: `HL-${String(10000 + index)}`,
  seenOffset: -(index % 6),
}));

const activity = [
  event(-160, "collection.finished", "ok", "Example integrations"),
  event(-120, "policy.approved", "ok", "POL-ISP"),
  event(-90, "policy.approved", "ok", "POL-ACC"),
  event(-40, "risk.saved", "ok", "R-01"),
  event(-20, "vendor.saved", "ok", "Pixel Fonts"),
  event(-12, "people.synced", "ok", "120"),
  event(-6, "access.started", "ok", "Q4 access review"),
  event(-3, "audit.request.created", "ok", "REQ-12"),
  event(-2, "soa.viewed", "ok", "iso27001"),
  event(-1, "collection.finished", "ok", "Example integrations"),
];

function event(dayOffset, action, result, detail) {
  return { dayOffset, action, result, detail, actor: dayOffset < -10 ? "chandra.vell@harborline.example" : "priya.nair@harborline.example" };
}

const findings = [
  {
    code: "F-1",
    title: "MFA gap",
    severity: "Exception",
    detail: "Two active accounts can sign in without a second factor.",
    requirementCode: "CC6.1",
    managementResponse: "Enrollment is required before the account is marked active. The two accounts are in the access review.",
    responseByName: "Priya Nair",
    comments: [
      { author: "Rachel Ortiz", dayOffset: -4, body: "Please name the two accounts in the management response." },
      { author: "Priya Nair", dayOffset: -3, body: "Jordan Pell and Casey Ruiz. Both are in the open access review." },
    ],
  },
  {
    code: "F-2",
    title: "Late access review",
    severity: "Observation",
    detail: "The previous quarterly review was signed after the period closed.",
    requirementCode: "CC6.2",
    managementResponse: null,
    responseByName: null,
    comments: [
      { author: "Rachel Ortiz", dayOffset: -2, body: "The Q4 review can close this observation if it is finished before fieldwork." },
    ],
  },
];

const requests = [
  request("REQ-14", "New-hire onboarding", "23 hires in the period", 5, "HR system", 6, "Open", -1, null),
  request("REQ-13", "Change tickets", "612 pull requests", 25, "GitHub", 4, "Submitted", -2, "The pull request list for the sample is attached."),
  request("REQ-12", "Terminations", "9 leavers", 4, "Workspace", 2, "Open", -3, null),
  request("REQ-11", "Prior access review", "Privileged population", null, "Access reviews", -8, "Accepted", -8, "The completed review is in the evidence library."),
  request("REQ-10", "Backup restores", "6 restores", 2, "AWS", -12, "Accepted", -10, "Both restore records are in the evidence library."),
];

function request(code, title, population, sampleSize, source, dueOffset, status, createdOffset, response) {
  return { code, title, population, sampleSize, source, dueOffset, status, createdOffset, response };
}

const controls = [
  control("GV-05", "Security objectives are reviewed quarterly", "A.5.2", "CC1.1"),
  control("GV-06", "Interested parties are listed", "A.5.4", "CC1.2"),
  control("RM-02", "Risk owners accept residual risk", "A.5.7", "CC3.2"),
  control("HR-02", "Employment terms include security duties", "A.6.2", "CC1.4"),
  control("HR-03", "Security awareness is refreshed yearly", "A.6.3", "CC1.4"),
  control("AC-02", "Access is approved before it is granted", "A.5.18", "CC6.2"),
  control("AC-05", "Privileged access is limited to the role", "A.8.2", "CC6.3"),
  control("AC-09", "Shared accounts are prohibited", "A.5.16", "CC6.1"),
  control("CM-02", "Production changes have a ticket", "A.8.32", "CC8.1"),
  control("CM-06", "Emergency changes are reviewed after the fact", "A.8.32", "CC8.1"),
  control("LG-02", "Clocks are synchronized", "A.8.17", "CC7.2"),
  control("LG-05", "Admin activity is retained", "A.8.15", "CC7.2"),
  control("CR-03", "Keys are rotated on a schedule", "A.8.24", "CC6.7"),
  control("CR-08", "Secrets are not stored in the repository", "A.8.4", "CC6.7"),
  control("VM-01", "Production dependencies are scanned", "A.8.8", "CC7.1"),
  control("VM-04", "Critical findings have an owner", "A.8.8", "CC7.1"),
  control("BC-02", "Restore owners are named", "A.5.29", "A1.2"),
  control("IR-01", "Incidents have a severity scale", "A.5.24", "CC7.3"),
  control("IR-04", "Customers are told when their data is affected", "A.5.26", "CC7.4"),
  control("CFG-02", "Baseline images are versioned", "A.8.9", "CC6.1"),
  control("PH-03", "The office network is separated from production", "A.7.9", "CC6.5"),
  control("EP-01", "Endpoints encrypt their disk", "A.8.1", "CC6.7"),
];

function control(code, title, iso, soc) {
  const mappings = [{ framework: "iso27001", requirement: iso, coverage: "full" }];
  if (!soc.startsWith("A")) mappings.push({ framework: "soc2", requirement: soc, coverage: "partial" });
  else mappings.push({ framework: "soc2", requirement: "A1.2", coverage: "partial" });
  return {
    code,
    title,
    description: `${title}. Harborline operates this control for the example workspace.`,
    status: "Passing",
    frequency: "Annual",
    ownerName: "Priya Nair",
    ownerEmail: "priya.nair@harborline.example",
    mappings,
    tests: [
      {
        name: `${code} record`,
        kind: "document",
        outcome: "pass",
        detail: "Example record in the Harborline workspace.",
      },
    ],
  };
}

const document = {
  company: {
    name: "Harborline",
    slug: "harborline",
    domain,
    region: "US",
    summary: "Fictional B2B SaaS company, about 120 people, preparing for SOC 2 Type II and ISO 27001.",
  },
  people: people.map((item) => ({
    name: item.name,
    email: item.email,
    department: item.department,
    role: item.role,
    hireOffset: item.hireOffset,
    leaveOffset: item.leaveOffset,
    training: item.training,
    trainingDueOffset: item.trainingDueOffset,
  })),
  resources,
  risks,
  vendors,
  history,
  notifications,
  devices,
  activity,
  findings,
  requests,
  controls,
  evidence: [
    { control: "GV-01", title: "Signed information security policy", body: "Harborline approved the information security policy on the annual cycle. Example record." },
    { control: "HR-06", title: "Security training completion export", body: "Harborline training export. Most of the company is complete. A few people are overdue. Example record." },
    { control: "BC-01", title: "Restore drill for customer files", body: "Harborline restored a sample of customer-files and compared the checksums. Example record." },
    { control: "IR-02", title: "Tabletop incident notes", body: "Harborline ran a tabletop for a lost laptop. The notes name the owner and the customers told. Example record." },
    { control: "AC-04", title: "Quarterly access review minutes", body: "Harborline started the Q4 access review. Decisions are still open for part of the population. Example record." },
    { control: "VM-02", title: "Dependency scan", body: "Harborline scanned production dependencies. Two findings remain open with owners. Example record." },
  ],
  accessReview: {
    name: "Q4 access review",
    dueOffset: 12,
    startedOffset: -6,
  },
};

const target = join(dirname(fileURLToPath(import.meta.url)), "harborline.json");
writeFileSync(target, `${JSON.stringify(document, null, 2)}\n`);
console.log(`wrote ${people.length} people, ${resources.length} resources, ${devices.length} devices`);
