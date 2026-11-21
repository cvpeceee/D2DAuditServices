import { company, type DemoPerson, type DemoVendor, type DemoWorld } from "./catalog";
import {
  selfServiceCategories,
  type EmployeeGroup,
  type LifecycleChecklist,
  type LifecycleOverview,
  type LifecycleSettings,
  type LifecycleStep,
  type LifecycleSyncResult,
  type LifecycleTemplateStep,
  type LifecycleTest,
  type PersonLifecycle,
  type RevokeAllResult,
} from "../features/people/lifecycleTypes";
import {
  answerValues,
  documentKinds,
  maxDocumentBytes,
  type QuestionnaireTemplate,
  type SentVendorQuestionnaire,
  type VendorAnswer,
  type VendorAssessment,
  type VendorDiscovery,
  type VendorDiscoveryScanResult,
  type VendorPortalView,
  type VendorReminder,
  type VendorReminderSyncResult,
  type VendorReviewDocument,
  type VendorRiskDetail,
  type VendorRiskRow,
} from "../features/vendors/vendorRiskTypes";

type Actor = { email: string; name: string; role: string };
type Log = (action: string, detail: string) => void;

class Fail extends Error {
  constructor(
    public status: number,
    public code: string,
  ) {
    super(code);
  }
}

type StoredGroup = {
  code: string;
  name: string;
  description: string;
  departments: string[];
  roleKeywords: string[];
  policyCodes: string[];
  trainingCodes: string[];
};

type StoredStep = {
  id: string;
  code: string;
  title: string;
  category: string;
  provider: string;
  accountName: string;
  revokesAccess: boolean;
  order: number;
  dueAt: string;
  done: boolean;
  completedAt: string | null;
  completedBy: string;
  result: string;
  evidenceId: string | null;
};

type StoredChecklist = {
  id: string;
  personId: string;
  personName: string;
  personEmail: string;
  kind: "Onboarding" | "Offboarding";
  trigger: string;
  reason: string;
  effectiveOn: string;
  triggeredAt: string;
  dueAt: string;
  accessDueAt: string | null;
  accessRevokedAt: string | null;
  completedAt: string | null;
  state: "Open" | "Complete";
  steps: StoredStep[];
};

type StoredVendorProfile = {
  vendorId: string;
  ownerName: string;
  ownerEmail: string;
  contractRenewalOn: string | null;
  dataCategories: string[];
  subprocessor: boolean;
  reviewCadenceMonths: number;
  lastReviewedOn: string | null;
  contactName: string;
  contactEmail: string;
};

type StoredAssessment = {
  id: string;
  vendorId: string;
  vendorName: string;
  templateCode: string;
  contactName: string;
  contactEmail: string;
  token: string;
  status: string;
  sentAt: string;
  sentBy: string;
  expiresAt: string;
  submittedAt: string | null;
  reviewedAt: string | null;
  reviewedBy: string;
  reviewNote: string;
  score: number | null;
  rating: string;
  deliveryReceipt: string;
  answers: VendorAnswer[];
};

type StoredDocument = VendorReviewDocument & { rawText: string };

type State = {
  settings: LifecycleSettings;
  groups: StoredGroup[];
  templates: LifecycleTemplateStep[];
  checklists: StoredChecklist[];
  tests: LifecycleTest[];
  profiles: Map<string, StoredVendorProfile>;
  discoveries: VendorDiscovery[];
  assessments: StoredAssessment[];
  documents: StoredDocument[];
  counter: number;
};

const stateByWorld = new WeakMap<DemoWorld, State>();
const hourMs = 3_600_000;
const dayMs = 86_400_000;
const tenantHex = "a360c8ce09884dfc9722bce45697b553";
const offboardingTestName = "Terminated personnel access removed within SLA";
const onboardingTestName = "New hires complete onboarding within N days";
const reviewTaskPrefix = "Vendor security review: ";
const renewalTaskPrefix = "Vendor contract renewal: ";
const maxDocumentsPerAssessment = 10;

const defaultTemplates: Omit<LifecycleTemplateStep, "id" | "order">[] = [
  {
    kind: "Onboarding",
    code: "accept-policies",
    title: "Accept the security policies",
    category: "Policy",
    slaHours: 72,
    groupCode: "",
    revokesAccess: false,
    perAccount: false,
  },
  {
    kind: "Onboarding",
    code: "complete-training",
    title: "Complete security awareness training",
    category: "Training",
    slaHours: 168,
    groupCode: "",
    revokesAccess: false,
    perAccount: false,
  },
  {
    kind: "Onboarding",
    code: "enroll-device",
    title: "Enroll the work computer in device management",
    category: "Device",
    slaHours: 72,
    groupCode: "",
    revokesAccess: false,
    perAccount: false,
  },
  {
    kind: "Onboarding",
    code: "enable-mfa",
    title: "Turn on multi-factor sign-in",
    category: "Identity",
    slaHours: 24,
    groupCode: "",
    revokesAccess: false,
    perAccount: false,
  },
  {
    kind: "Onboarding",
    code: "background-check",
    title: "Background check cleared",
    category: "Screening",
    slaHours: 168,
    groupCode: "",
    revokesAccess: false,
    perAccount: false,
  },
  {
    kind: "Onboarding",
    code: "secure-coding",
    title: "Complete secure coding training",
    category: "Training",
    slaHours: 240,
    groupCode: "engineering",
    revokesAccess: false,
    perAccount: false,
  },
  {
    kind: "Onboarding",
    code: "contractor-nda",
    title: "Sign the contractor confidentiality agreement",
    category: "Policy",
    slaHours: 24,
    groupCode: "contractors",
    revokesAccess: false,
    perAccount: false,
  },
  {
    kind: "Offboarding",
    code: "revoke-sso",
    title: "Revoke single sign-on",
    category: "Access",
    slaHours: 0,
    groupCode: "",
    revokesAccess: true,
    perAccount: false,
  },
  {
    kind: "Offboarding",
    code: "revoke-app",
    title: "Revoke connected app account",
    category: "Access",
    slaHours: 0,
    groupCode: "",
    revokesAccess: true,
    perAccount: true,
  },
  {
    kind: "Offboarding",
    code: "remove-github",
    title: "Remove from the GitHub organization",
    category: "Access",
    slaHours: 0,
    groupCode: "engineering",
    revokesAccess: true,
    perAccount: false,
  },
  {
    kind: "Offboarding",
    code: "recover-device",
    title: "Recover the work computer",
    category: "Device",
    slaHours: 72,
    groupCode: "",
    revokesAccess: false,
    perAccount: false,
  },
  {
    kind: "Offboarding",
    code: "transfer-ownership",
    title: "Transfer document and system ownership",
    category: "Ownership",
    slaHours: 72,
    groupCode: "",
    revokesAccess: false,
    perAccount: false,
  },
];

const defaultGroups: StoredGroup[] = [
  {
    code: "everyone",
    name: "Everyone",
    description: "All active personnel",
    departments: ["*"],
    roleKeywords: [],
    policyCodes: ["POL-ISP", "POL-ACC"],
    trainingCodes: ["SEC-ANNUAL"],
  },
  {
    code: "engineering",
    name: "Engineering",
    description: "People who ship code or run production",
    departments: ["Engineering", "Security"],
    roleKeywords: ["engineer", "developer", "sre"],
    policyCodes: ["POL-ISP", "POL-ACC", "POL-CHG", "POL-INC"],
    trainingCodes: ["SEC-ANNUAL", "SEC-CODE"],
  },
  {
    code: "contractors",
    name: "Contractors",
    description: "Non-employee personnel with workspace access",
    departments: [],
    roleKeywords: ["contractor", "consultant"],
    policyCodes: ["POL-ISP", "POL-ACC", "POL-PRI"],
    trainingCodes: ["SEC-ANNUAL"],
  },
];

const discoveryFixtures: {
  name: string;
  source: string;
  detail: string;
  suggestedCriticality: string;
  dataCategories: string[];
}[] = [
  {
    name: "Figma",
    source: "SSO",
    detail: "38 people sign in through single sign-on",
    suggestedCriticality: "Medium",
    dataCategories: ["CustomerData"],
  },
  {
    name: "Notion",
    source: "SSO",
    detail: "112 people sign in through single sign-on",
    suggestedCriticality: "High",
    dataCategories: ["CustomerData", "EmployeeData"],
  },
  {
    name: "Loom",
    source: "SSO",
    detail: "21 people sign in through single sign-on",
    suggestedCriticality: "Low",
    dataCategories: ["EmployeeData"],
  },
  {
    name: "Datadog",
    source: "Expense",
    detail: "Monthly card charge of USD 4,850",
    suggestedCriticality: "High",
    dataCategories: ["CustomerData", "Credentials"],
  },
  {
    name: "Zapier",
    source: "Expense",
    detail: "Monthly card charge of USD 299",
    suggestedCriticality: "Medium",
    dataCategories: ["PersonalData"],
  },
  {
    name: "Calendly",
    source: "Expense",
    detail: "Monthly card charge of USD 96",
    suggestedCriticality: "Low",
    dataCategories: ["PersonalData"],
  },
];

export const vendorQuestionnaireTemplates: QuestionnaireTemplate[] = [
  {
    code: "core-security",
    name: "Core vendor security review",
    description:
      "A short security review covering governance, access, data protection, operations, and resilience.",
    questions: [
      {
        code: "GOV-1",
        domain: "Governance",
        text: "Do you keep a written information security program that leadership approves at least once a year?",
        weight: 3,
        critical: false,
      },
      {
        code: "GOV-2",
        domain: "Governance",
        text: "Is a named person accountable for security across your company?",
        weight: 2,
        critical: false,
      },
      {
        code: "GOV-3",
        domain: "Governance",
        text: "Do you hold a current independent security attestation, such as a SOC 2 Type II report or ISO 27001 certificate?",
        weight: 4,
        critical: true,
      },
      {
        code: "HR-1",
        domain: "People",
        text: "Do staff with access to customer data pass a background check where local law allows?",
        weight: 2,
        critical: false,
      },
      {
        code: "HR-2",
        domain: "People",
        text: "Do all staff complete security awareness training when they join and every year after?",
        weight: 2,
        critical: false,
      },
      {
        code: "AC-1",
        domain: "Access",
        text: "Is multi-factor sign-in required for every account that can reach customer data or production systems?",
        weight: 4,
        critical: true,
      },
      {
        code: "AC-2",
        domain: "Access",
        text: "Do you review who has access to production systems at least every quarter?",
        weight: 3,
        critical: false,
      },
      {
        code: "AC-3",
        domain: "Access",
        text: "Is access removed within one business day when someone leaves?",
        weight: 3,
        critical: true,
      },
      {
        code: "DP-1",
        domain: "Data",
        text: "Is customer data encrypted while stored, using industry standard algorithms?",
        weight: 4,
        critical: true,
      },
      {
        code: "DP-2",
        domain: "Data",
        text: "Is customer data encrypted in transit with TLS 1.2 or newer?",
        weight: 4,
        critical: true,
      },
      {
        code: "DP-3",
        domain: "Data",
        text: "Can you delete or return customer data when the contract ends, and confirm it in writing?",
        weight: 3,
        critical: false,
      },
      {
        code: "DP-4",
        domain: "Data",
        text: "Do you keep a current list of subprocessors and tell customers before adding one?",
        weight: 2,
        critical: false,
      },
      {
        code: "OP-1",
        domain: "Operations",
        text: "Do you scan for vulnerabilities and fix critical findings within a defined window?",
        weight: 3,
        critical: false,
      },
      {
        code: "OP-2",
        domain: "Operations",
        text: "Has an independent firm tested your product for security weaknesses in the last twelve months?",
        weight: 3,
        critical: false,
      },
      {
        code: "OP-3",
        domain: "Operations",
        text: "Are code changes reviewed by someone other than the author before release?",
        weight: 2,
        critical: false,
      },
      {
        code: "OP-4",
        domain: "Operations",
        text: "Do you keep security logs for at least one year and watch them for alerts?",
        weight: 2,
        critical: false,
      },
      {
        code: "IR-1",
        domain: "Incidents",
        text: "Do you have a written incident response plan that you test at least once a year?",
        weight: 3,
        critical: false,
      },
      {
        code: "IR-2",
        domain: "Incidents",
        text: "Will you notify customers of a breach affecting their data within 72 hours of confirming it?",
        weight: 4,
        critical: true,
      },
      {
        code: "BC-1",
        domain: "Resilience",
        text: "Do you back up customer data and test restoring it at least once a year?",
        weight: 3,
        critical: false,
      },
      {
        code: "BC-2",
        domain: "Resilience",
        text: "Do you have a business continuity plan with recovery targets for the service you provide us?",
        weight: 2,
        critical: false,
      },
    ],
  },
  {
    code: "privacy-addendum",
    name: "Privacy addendum",
    description: "Follow-up questions for vendors that process personal data.",
    questions: [
      {
        code: "PR-1",
        domain: "Privacy",
        text: "Do you process personal data only on documented customer instructions?",
        weight: 4,
        critical: true,
      },
      {
        code: "PR-2",
        domain: "Privacy",
        text: "Do you sign a data processing agreement that covers international transfers?",
        weight: 3,
        critical: false,
      },
      {
        code: "PR-3",
        domain: "Privacy",
        text: "Can you help us respond to data subject requests within 30 days?",
        weight: 3,
        critical: false,
      },
      {
        code: "PR-4",
        domain: "Privacy",
        text: "Do you keep a record of processing activities for the service you provide us?",
        weight: 2,
        critical: false,
      },
      {
        code: "PR-5",
        domain: "Privacy",
        text: "Is personal data minimized and kept only as long as the contract requires?",
        weight: 2,
        critical: false,
      },
    ],
  },
];

const categoryWeights: Record<string, number> = {
  customerdata: 20,
  personaldata: 20,
  financial: 25,
  health: 30,
  credentials: 25,
  sourcecode: 15,
  employeedata: 10,
  none: 0,
};

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function nextId(state: State) {
  state.counter += 1;
  return `30000000-0000-4000-8000-${state.counter.toString(16).padStart(12, "0")}`;
}

function text(value: unknown) {
  return typeof value === "string" ? value.trim() : "";
}

function listOfStrings(value: unknown): string[] {
  if (!Array.isArray(value)) return [];
  const seen = new Set<string>();
  const out: string[] = [];
  for (const item of value) {
    const trimmed = text(item);
    if (!trimmed) continue;
    const key = trimmed.toLowerCase();
    if (!seen.has(key)) {
      seen.add(key);
      out.push(trimmed);
    }
  }
  return out;
}

function canWrite(actor: Actor) {
  return actor.role !== "Auditor" && actor.role !== "Employee";
}

function requireWrite(actor: Actor) {
  if (!canWrite(actor)) throw new Fail(403, "forbidden");
}

function optionalEmail(value: string) {
  if (!value) return true;
  if (value.length > 320 || /\s/.test(value)) return false;
  const parts = value.split("@");
  return parts.length === 2 && parts[0].length > 0 && parts[1].length > 0;
}

function todayStr(now = new Date()) {
  return now.toISOString().slice(0, 10);
}

function addDaysStr(base: string, days: number) {
  const date = new Date(`${base}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

function addMonthsStr(base: string, months: number) {
  const date = new Date(`${base}T00:00:00Z`);
  date.setUTCMonth(date.getUTCMonth() + months);
  return date.toISOString().slice(0, 10);
}

function daysBetween(from: string, to: string) {
  const a = Date.parse(`${from}T00:00:00Z`);
  const b = Date.parse(`${to}T00:00:00Z`);
  return Math.round((b - a) / dayMs);
}

function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, value));
}

function inGroup(group: StoredGroup, department: string, role: string) {
  if (group.departments.includes("*")) return true;
  const dep = department.trim().toLowerCase();
  if (group.departments.some((item) => item.toLowerCase() === dep)) return true;
  const lowRole = role.toLowerCase();
  return group.roleKeywords.some((keyword) => lowRole.includes(keyword.toLowerCase()));
}

function groupsForPerson(groups: StoredGroup[], person: DemoPerson) {
  return groups
    .filter((group) => inGroup(group, person.department, person.role))
    .map((group) => group.code)
    .sort();
}

function slaState(dueAt: string, doneAt: string | null, nowMs: number) {
  const dueMs = Date.parse(dueAt);
  if (doneAt) return Date.parse(doneAt) <= dueMs ? "Met" : "Missed";
  if (nowMs > dueMs) return "Overdue";
  return dueMs - nowMs <= 12 * hourMs ? "DueSoon" : "OnTrack";
}

function generateSteps(
  state: State,
  kind: "Onboarding" | "Offboarding",
  groupCodes: string[],
  person: DemoPerson,
  triggeredAtMs: number,
): StoredStep[] {
  const accessDueMs =
    triggeredAtMs + clamp(state.settings.offboardingAccessSlaHours, 1, 720) * hourMs;
  const completeDueMs =
    kind === "Offboarding"
      ? triggeredAtMs + clamp(state.settings.offboardingCompleteSlaHours, 1, 1440) * hourMs
      : triggeredAtMs + clamp(state.settings.onboardingDays, 1, 120) * dayMs;
  const activeAccounts = person.accounts
    .filter((account) => account.active)
    .slice()
    .sort(
      (a, b) => a.provider.localeCompare(b.provider) || a.accountName.localeCompare(b.accountName),
    );
  const steps: StoredStep[] = [];
  let order = 0;
  for (const template of state.templates
    .filter((item) => item.kind === kind)
    .sort((a, b) => a.order - b.order)) {
    if (
      template.groupCode &&
      !groupCodes.some((code) => code.toLowerCase() === template.groupCode.toLowerCase())
    ) {
      continue;
    }
    let dueMs = completeDueMs;
    if (template.revokesAccess) {
      dueMs = accessDueMs;
    } else if (template.slaHours > 0) {
      dueMs = Math.min(triggeredAtMs + template.slaHours * hourMs, completeDueMs);
    }
    const dueAt = new Date(dueMs).toISOString();
    if (template.perAccount) {
      for (const account of activeAccounts) {
        steps.push({
          id: nextId(state),
          code: template.code,
          title: `${template.title}: ${account.provider} ${account.accountName}`,
          category: template.category,
          provider: account.provider,
          accountName: account.accountName,
          revokesAccess: template.revokesAccess,
          order: order++,
          dueAt,
          done: false,
          completedAt: null,
          completedBy: "",
          result: "",
          evidenceId: null,
        });
      }
      continue;
    }
    steps.push({
      id: nextId(state),
      code: template.code,
      title: template.title,
      category: template.category,
      provider: "",
      accountName: "",
      revokesAccess: template.revokesAccess,
      order: order++,
      dueAt,
      done: false,
      completedAt: null,
      completedBy: "",
      result: "",
      evidenceId: null,
    });
  }
  return steps;
}

function startChecklist(
  state: State,
  person: DemoPerson,
  kind: "Onboarding" | "Offboarding",
  trigger: string,
  reason: string,
  triggeredAt: Date,
  effectiveOn: string,
): StoredChecklist {
  const groups = groupsForPerson(state.groups, person);
  const triggeredMs = triggeredAt.getTime();
  const accessDueAt =
    kind === "Offboarding"
      ? new Date(
          triggeredMs + clamp(state.settings.offboardingAccessSlaHours, 1, 720) * hourMs,
        ).toISOString()
      : null;
  const dueAt =
    kind === "Offboarding"
      ? new Date(
          triggeredMs + clamp(state.settings.offboardingCompleteSlaHours, 1, 1440) * hourMs,
        ).toISOString()
      : new Date(triggeredMs + clamp(state.settings.onboardingDays, 1, 120) * dayMs).toISOString();
  const checklist: StoredChecklist = {
    id: nextId(state),
    personId: person.id,
    personName: person.name,
    personEmail: person.email,
    kind,
    trigger,
    reason,
    effectiveOn,
    triggeredAt: triggeredAt.toISOString(),
    dueAt,
    accessDueAt,
    accessRevokedAt: null,
    completedAt: null,
    state: "Open",
    steps: generateSteps(state, kind, groups, person, triggeredMs),
  };
  state.checklists.unshift(checklist);
  return checklist;
}

function settleChecklist(world: DemoWorld, checklist: StoredChecklist) {
  const access = checklist.steps.filter((step) => step.revokesAccess);
  checklist.accessRevokedAt =
    access.length > 0 && access.every((step) => step.done)
      ? access.reduce<string | null>(
          (max, step) =>
            !max || (step.completedAt && step.completedAt > max) ? step.completedAt : max,
          null,
        )
      : null;
  const complete = checklist.steps.length > 0 && checklist.steps.every((step) => step.done);
  checklist.state = complete ? "Complete" : "Open";
  checklist.completedAt = complete
    ? checklist.steps.reduce<string | null>(
        (max, step) =>
          !max || (step.completedAt && step.completedAt > max) ? step.completedAt : max,
        null,
      )
    : null;
  if (complete && checklist.kind === "Offboarding") {
    const person = world.people.find((item) => item.id === checklist.personId);
    if (person) person.state = "Offboarded";
  }
}

function evaluateTests(state: State, nowMs: number): LifecycleTest[] {
  const horizon = nowMs - 180 * dayMs;
  const rows = state.checklists.filter(
    (item) =>
      (Date.parse(item.triggeredAt) >= horizon || item.state === "Open") &&
      Date.parse(item.triggeredAt) <= nowMs,
  );
  const offboardings = rows.filter((item) => item.kind === "Offboarding");
  let offOutcome = "Pass";
  let offDetail = "No terminations need access removal.";
  if (offboardings.length > 0) {
    const missed = offboardings
      .filter((item) => {
        const due = Date.parse(
          item.accessDueAt ??
            new Date(
              Date.parse(item.triggeredAt) + state.settings.offboardingAccessSlaHours * hourMs,
            ).toISOString(),
        );
        return item.accessRevokedAt ? Date.parse(item.accessRevokedAt) > due : nowMs > due;
      })
      .map((item) => item.personName);
    if (missed.length === 0) {
      offDetail = `${offboardings.length} terminations had access removed within ${state.settings.offboardingAccessSlaHours} hours.`;
    } else {
      offOutcome = "Fail";
      offDetail = `Access not removed within ${state.settings.offboardingAccessSlaHours} hours for: ${missed.slice(0, 12).join(", ")}.`;
    }
  }

  const onboardings = rows.filter((item) => item.kind === "Onboarding");
  let onOutcome = "Pass";
  let onDetail = "No new hires are onboarding.";
  if (onboardings.length > 0) {
    const late = onboardings
      .filter((item) => {
        const due = Date.parse(item.dueAt);
        return item.completedAt ? Date.parse(item.completedAt) > due : nowMs > due;
      })
      .map((item) => item.personName);
    if (late.length === 0) {
      onDetail = `${onboardings.length} new hires completed or are on track to complete onboarding within ${state.settings.onboardingDays} days.`;
    } else {
      onOutcome = "Fail";
      onDetail = `Onboarding not complete within ${state.settings.onboardingDays} days for: ${late.slice(0, 12).join(", ")}.`;
    }
  }

  state.tests = [
    { name: offboardingTestName, controlCode: "HR-08", outcome: offOutcome, detail: offDetail },
    { name: onboardingTestName, controlCode: "HR-01", outcome: onOutcome, detail: onDetail },
  ];
  return state.tests;
}

function toChecklistDto(world: DemoWorld, state: State, item: StoredChecklist): LifecycleChecklist {
  const nowMs = Date.now();
  const person = world.people.find((p) => p.id === item.personId);
  const groups = person ? groupsForPerson(state.groups, person) : [];
  const steps: LifecycleStep[] = item.steps
    .slice()
    .sort((a, b) => a.order - b.order)
    .map((step) => ({
      id: step.id,
      code: step.code,
      title: step.title,
      category: step.category,
      provider: step.provider,
      accountName: step.accountName,
      revokesAccess: step.revokesAccess,
      dueAt: step.dueAt,
      done: step.done,
      completedAt: step.completedAt,
      completedBy: step.completedBy,
      result: step.result,
      evidenceId: step.evidenceId,
      sla: slaState(step.dueAt, step.completedAt, nowMs),
    }));
  return {
    id: item.id,
    personId: item.personId,
    personName: item.personName,
    personEmail: item.personEmail,
    kind: item.kind,
    trigger: item.trigger,
    reason: item.reason,
    effectiveOn: item.effectiveOn,
    triggeredAt: item.triggeredAt,
    dueAt: item.dueAt,
    accessDueAt: item.accessDueAt,
    accessRevokedAt: item.accessRevokedAt,
    completedAt: item.completedAt,
    state: item.state,
    sla: slaState(item.dueAt, item.completedAt, nowMs),
    accessSla: item.accessDueAt ? slaState(item.accessDueAt, item.accessRevokedAt, nowMs) : "",
    doneCount: steps.filter((step) => step.done).length,
    totalCount: steps.length,
    groups,
    steps,
  };
}

function toPersonLifecycle(world: DemoWorld, state: State, person: DemoPerson): PersonLifecycle {
  const mine = state.groups.filter((group) => inGroup(group, person.department, person.role));
  const policies = [...new Set(mine.flatMap((group) => group.policyCodes))].sort();
  const training = [...new Set(mine.flatMap((group) => group.trainingCodes))].sort();
  const checklists = state.checklists
    .filter((item) => item.personId === person.id)
    .sort((a, b) => b.triggeredAt.localeCompare(a.triggeredAt))
    .map((item) => toChecklistDto(world, state, item));
  return {
    personId: person.id,
    name: person.name,
    email: person.email,
    state: person.state,
    hireOn: person.hireOn,
    terminationOn: person.leftOn,
    groups: mine.map((group) => group.code).sort(),
    requiredPolicies: policies,
    requiredTraining: training,
    checklists,
  };
}

function inferCategories(dataAccess: string): string[] {
  const low = (dataAccess ?? "").toLowerCase();
  const out: string[] = [];
  const add = (cat: string, keywords: string[]) => {
    if (keywords.some((kw) => low.includes(kw))) out.push(cat);
  };
  add("CustomerData", ["customer", "warehouse", "tenant data"]);
  add("PersonalData", ["personal", "pii", "contact", "email address"]);
  add("Financial", ["payment", "card", "billing", "financ", "invoice"]);
  add("Credentials", ["credential", "secret", "token", "key"]);
  add("SourceCode", ["source", "repositor", "code"]);
  add("EmployeeData", ["employee", "staff", "workforce"]);
  return out.length === 0 ? ["None"] : out;
}

function inherentRisk(criticality: string, categories: string[], subprocessor: boolean) {
  const critWeight =
    criticality === "Critical"
      ? 55
      : criticality === "High"
        ? 40
        : criticality === "Medium"
          ? 25
          : 10;
  const seen = new Set<string>();
  let data = 0;
  for (const cat of categories) {
    const key = cat.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    data += categoryWeights[key] ?? 5;
  }
  const score = clamp(critWeight + Math.min(data, 40) + (subprocessor ? 5 : 0), 0, 100);
  const tier = score >= 75 ? "Tier1" : score >= 50 ? "Tier2" : score >= 25 ? "Tier3" : "Tier4";
  const defaultCadenceMonths = tier === "Tier1" ? 6 : tier === "Tier4" ? 24 : 12;
  return { score, tier, defaultCadenceMonths };
}

function windowState(days: number, soon: number, past: string, near: string, later: string) {
  if (days < 0) return past;
  return days <= soon ? near : later;
}

function effectiveAssessmentStatus(assessment: StoredAssessment, nowMs: number) {
  return (assessment.status === "Sent" || assessment.status === "InProgress") &&
    nowMs >= Date.parse(assessment.expiresAt)
    ? "Expired"
    : assessment.status;
}

function scoreAssessment(template: QuestionnaireTemplate, answers: VendorAnswer[]) {
  const byCode = new Map(answers.map((item) => [item.questionCode, item.answer]));
  let earned = 0;
  let possible = 0;
  let answered = 0;
  const flagged: string[] = [];
  let criticalMiss = false;
  for (const question of template.questions) {
    const answer = byCode.get(question.code);
    if (!answer || !(answerValues as readonly string[]).includes(answer)) {
      possible += question.weight;
      continue;
    }
    answered += 1;
    if (answer === "NotApplicable") continue;
    possible += question.weight;
    const credit = answer === "Yes" ? 1 : answer === "Partial" ? 0.5 : 0;
    earned += question.weight * credit;
    if (credit < 1) {
      flagged.push(question.code);
      if (question.critical && credit === 0) criticalMiss = true;
    }
  }
  const percent = possible <= 0 ? 100 : Math.round((earned * 100) / possible);
  const rating = criticalMiss || percent < 65 ? "High" : percent >= 85 ? "Low" : "Moderate";
  return { percent, rating, answered, total: template.questions.length, flagged };
}

function toAssessmentDto(item: StoredAssessment): VendorAssessment {
  const template =
    vendorQuestionnaireTemplates.find((tpl) => tpl.code === item.templateCode) ??
    vendorQuestionnaireTemplates[0];
  const scored = scoreAssessment(template, item.answers);
  const order = new Map(template.questions.map((q, idx) => [q.code, idx]));
  return {
    id: item.id,
    vendorId: item.vendorId,
    vendorName: item.vendorName,
    templateCode: item.templateCode,
    templateName: template.name,
    contactName: item.contactName,
    contactEmail: item.contactEmail,
    status: effectiveAssessmentStatus(item, Date.now()),
    sentAt: item.sentAt,
    sentBy: item.sentBy,
    expiresAt: item.expiresAt,
    submittedAt: item.submittedAt,
    reviewedAt: item.reviewedAt,
    reviewedBy: item.reviewedBy,
    reviewNote: item.reviewNote,
    score: item.score,
    rating: item.rating,
    answered: scored.answered,
    total: scored.total,
    flagged: scored.flagged,
    deliveryReceipt: item.deliveryReceipt,
    answers: item.answers
      .slice()
      .sort((a, b) => (order.get(a.questionCode) ?? 999) - (order.get(b.questionCode) ?? 999)),
  };
}

function toVendorRiskRow(
  vendor: DemoVendor,
  profile: StoredVendorProfile | undefined,
  latest: StoredAssessment | undefined,
): VendorRiskRow {
  const today = todayStr();
  const categories = profile ? profile.dataCategories : inferCategories(vendor.dataAccess);
  const subprocessor = profile?.subprocessor ?? false;
  const risk = inherentRisk(vendor.criticality, categories, subprocessor);
  const cadence = profile?.reviewCadenceMonths ?? risk.defaultCadenceMonths;
  const nextReviewOn = profile?.lastReviewedOn
    ? addMonthsStr(profile.lastReviewedOn, clamp(cadence, 1, 60))
    : vendor.reviewDueOn;
  const reviewDays = daysBetween(today, nextReviewOn);
  const reviewStatus = windowState(reviewDays, 30, "Overdue", "DueSoon", "Current");
  const renewalState = profile?.contractRenewalOn
    ? windowState(
        daysBetween(today, profile.contractRenewalOn),
        60,
        "Lapsed",
        "Upcoming",
        "Current",
      )
    : "None";
  return {
    id: vendor.id,
    name: vendor.name,
    service: vendor.service,
    criticality: vendor.criticality,
    dataAccess: vendor.dataAccess,
    reviewState: vendor.reviewState,
    decision: vendor.decision ?? "Pending",
    inherentScore: risk.score,
    tier: risk.tier,
    ownerName: profile?.ownerName ?? "",
    ownerEmail: profile?.ownerEmail ?? "",
    contractRenewalOn: profile?.contractRenewalOn ?? null,
    renewalState,
    dataCategories: categories,
    subprocessor,
    reviewCadenceMonths: cadence,
    lastReviewedOn: profile?.lastReviewedOn ?? null,
    nextReviewOn,
    reviewStatus,
    contactName: profile?.contactName ?? "",
    contactEmail: profile?.contactEmail ?? "",
    latestAssessmentStatus: latest ? effectiveAssessmentStatus(latest, Date.now()) : "",
    latestAssessmentScore: latest?.score ?? null,
  };
}

function summarizeSoc2(vendorName: string, rawText: string) {
  const body = rawText.replace(/\r\n/g, "\n").replace(/\r/g, "\n");
  const lower = body.toLowerCase();
  const lines = body
    .split("\n")
    .map((line) => line.trim())
    .filter(Boolean);
  const periodMatch =
    /(?:for\s+the\s+period|period\s+of|period)\s+(?:from\s+)?([A-Z][a-z]+\s+\d{1,2},?\s+\d{4}|\d{4}-\d{2}-\d{2})\s+(?:to|through|and\s+ending|-)\s+([A-Z][a-z]+\s+\d{1,2},?\s+\d{4}|\d{4}-\d{2}-\d{2})/i.exec(
      body,
    );
  const asOfMatch = /as\s+of\s+([A-Z][a-z]+\s+\d{1,2},?\s+\d{4}|\d{4}-\d{2}-\d{2})/i.exec(body);
  const reportPeriod = periodMatch
    ? `${periodMatch[1].trim()} to ${periodMatch[2].trim()}`
    : asOfMatch
      ? `As of ${asOfMatch[1].trim()}`
      : "Not stated in the document";

  let opinion = "Not stated in the document";
  if (lower.includes("adverse opinion")) opinion = "Adverse";
  else if (lower.includes("disclaimer of opinion") || lower.includes("do not express an opinion"))
    opinion = "Disclaimer";
  else if (
    (lower.includes("qualified opinion") && !lower.includes("unqualified opinion")) ||
    (lower.includes("except for") && lower.includes("opinion"))
  )
    opinion = "Qualified";
  else if (
    lower.includes("unqualified") ||
    lower.includes("unmodified") ||
    lower.includes("present fairly") ||
    lower.includes("presents fairly")
  )
    opinion = "Unqualified";

  const reportType =
    lower.includes("type ii") || lower.includes("type 2")
      ? "SOC 2 Type II"
      : lower.includes("type i") || lower.includes("type 1")
        ? "SOC 2 Type I"
        : lower.includes("soc 2")
          ? "SOC 2"
          : "Unknown";

  const exceptions = [
    ...new Set(
      lines
        .filter(
          (line) => /exception/i.test(line) && !/\bno\s+(?:\w+\s+){0,2}exceptions?\b/i.test(line),
        )
        .map((line) => line.slice(0, 300)),
    ),
  ].slice(0, 10);

  const cuecs: string[] = [];
  let inCuecs = false;
  for (const line of lines) {
    if (/complementary user entity control/i.test(line) || line.includes("CUEC")) {
      inCuecs = true;
      continue;
    }
    if (!inCuecs) continue;
    if (/^(?:[-*\u2022]|\d+[.)])\s+/.test(line)) {
      cuecs.push(line.replace(/^(?:[-*\u2022]|\d+[.)])\s+/, "").slice(0, 300));
      if (cuecs.length >= 15) break;
      continue;
    }
    if (cuecs.length > 0 && line.endsWith(":")) break;
  }
  const uniqueCuecs = [...new Set(cuecs)];
  const summaryText =
    `${reportType} report for ${vendorName}. Period: ${reportPeriod}. Opinion: ${opinion}. ` +
    (exceptions.length === 0
      ? "No exceptions were found in the text. "
      : `${exceptions.length} exception(s) noted; review each before sign-off. `) +
    (uniqueCuecs.length === 0
      ? "No complementary user entity controls were listed."
      : `${uniqueCuecs.length} complementary user entity control(s) must be operated by your company.`);

  return { reportPeriod, opinion, exceptions, cuecs: uniqueCuecs, summaryText };
}

function decodeBase64(value: string): Uint8Array | null {
  try {
    const trimmed = value.trim();
    if (!trimmed) return null;
    const raw = atob(trimmed);
    if (raw.length === 0 || raw.length > maxDocumentBytes) return null;
    const bytes = new Uint8Array(raw.length);
    for (let i = 0; i < raw.length; i += 1) bytes[i] = raw.charCodeAt(i);
    return bytes;
  } catch {
    return null;
  }
}

async function sha256Bytes(bytes: Uint8Array) {
  const copy = new Uint8Array(bytes.byteLength);
  copy.set(bytes);
  const digest = await crypto.subtle.digest("SHA-256", copy);
  return [...new Uint8Array(digest)].map((item) => item.toString(16).padStart(2, "0")).join("");
}

function slug(name: string) {
  return name.toLowerCase().replace(/[^a-z0-9]/g, "");
}

function stateFor(world: DemoWorld): State {
  let state = stateByWorld.get(world);
  if (state) return state;
  state = {
    settings: {
      offboardingAccessSlaHours: 24,
      offboardingCompleteSlaHours: 72,
      onboardingDays: 14,
    },
    groups: defaultGroups.map((group) => ({ ...group })),
    templates: [],
    checklists: [],
    tests: [],
    profiles: new Map(),
    discoveries: [],
    assessments: [],
    documents: [],
    counter: 0,
  };
  defaultTemplates.forEach((tpl, order) => {
    state!.templates.push({ ...tpl, id: nextId(state!), order });
  });
  stateByWorld.set(world, state);
  seedWorld(world, state);
  return state;
}

function seedWorld(world: DemoWorld, state: State) {
  const now = new Date();
  const today = todayStr(now);

  // Seed personnel lifecycle checklists matching LifecyclePortal.SeedExampleAsync
  const sam = world.people.find((p) => p.email === "sam.leaved@harborline.example");
  if (sam) {
    if (!sam.accounts.some((a) => a.active)) {
      sam.accounts.push({ provider: "Slack", accountName: sam.email, active: true });
    }
    const triggered = new Date(now.getTime() - 3 * dayMs);
    const overdue = startChecklist(
      state,
      sam,
      "Offboarding",
      "Hris",
      "Termination date from the HR system",
      triggered,
      todayStr(triggered),
    );
    sam.state = "Offboarding";
    for (const step of overdue.steps.filter((s) => s.code === "revoke-sso")) {
      step.done = true;
      step.completedAt = new Date(triggered.getTime() + 2 * hourMs).toISOString();
      step.completedBy = "Lena Cho";
      step.result = "Suspended in the identity provider";
    }
  }

  const winter = world.people.find((p) => p.email === "winter.cruz@harborline.example");
  if (winter) {
    const triggered = new Date(now.getTime() - 5 * dayMs);
    const done = startChecklist(
      state,
      winter,
      "Offboarding",
      "Hris",
      "Termination date from the HR system",
      triggered,
      todayStr(triggered),
    );
    for (const step of done.steps) {
      step.done = true;
      step.completedAt = new Date(
        triggered.getTime() + (step.revokesAccess ? 6 : 30) * hourMs,
      ).toISOString();
      step.completedBy = "Lena Cho";
      step.result = step.revokesAccess ? "Access removed" : "Done";
    }
    done.accessRevokedAt = new Date(triggered.getTime() + 6 * hourMs).toISOString();
    done.completedAt = new Date(triggered.getTime() + 30 * hourMs).toISOString();
    done.state = "Complete";
    winter.state = "Offboarded";
  }

  const dev = world.people.find((p) => p.email === "dev.patel@harborline.example");
  if (dev) {
    const triggered = new Date(now.getTime() - 4 * dayMs);
    const mine = startChecklist(
      state,
      dev,
      "Onboarding",
      "Manual",
      "Moved to the platform team; engineering group requirements apply",
      triggered,
      todayStr(triggered),
    );
    for (const step of mine.steps.filter(
      (s) => s.code === "enable-mfa" || s.code === "background-check",
    )) {
      step.done = true;
      step.completedAt = new Date(triggered.getTime() + 3 * hourMs).toISOString();
      step.completedBy = "Lena Cho";
      step.result = "Confirmed";
    }
  }

  evaluateTests(state, now.getTime());

  // Seed vendor risk profiles matching VendorRiskPortal.EnsureExampleAsync
  const owners = [
    ["Priya Nair", "priya.nair@harborline.example"],
    ["Lena Cho", "lena.cho@harborline.example"],
    ["Marcus Lee", "marcus.lee@harborline.example"],
  ] as const;
  const sortedVendors = world.vendors.slice().sort((a, b) => a.name.localeCompare(b.name));
  sortedVendors.forEach((vendor, index) => {
    const [ownerName, ownerEmail] = owners[index % owners.length];
    const categories = inferCategories(vendor.dataAccess);
    const subprocessor = vendor.criticality === "Critical" || vendor.criticality === "High";
    const risk = inherentRisk(vendor.criticality, categories, subprocessor);
    state.profiles.set(vendor.id, {
      vendorId: vendor.id,
      ownerName,
      ownerEmail,
      contractRenewalOn: addDaysStr(today, 20 + index * 37),
      dataCategories: categories,
      subprocessor,
      reviewCadenceMonths: risk.defaultCadenceMonths,
      lastReviewedOn: vendor.reviewState === "Approved" ? addMonthsStr(today, -3) : null,
      contactName: `${vendor.name} security team`,
      contactEmail: `security@${slug(vendor.name)}.example`,
    });
  });

  // Seed discovery suggestions
  addSuggestions(state, world, discoveryFixtures, now.toISOString());

  // Seed Pagerly in-progress questionnaire with a deterministic demo portal token
  const pagerly = world.vendors.find((v) => v.name === "Pagerly") ?? sortedVendors[0];
  if (pagerly) {
    const core = vendorQuestionnaireTemplates[0];
    const seededAnswers = ["Yes", "Yes", "Partial", "Yes", "Yes", "Yes", "No", "Yes", "Yes"];
    state.assessments.push({
      id: nextId(state),
      vendorId: pagerly.id,
      vendorName: pagerly.name,
      templateCode: core.code,
      contactName: "Rin Okafor",
      contactEmail: "security@pagerly.example",
      token: `${tenantHex}.demo_pagerly_questionnaire_token_0001`,
      status: "InProgress",
      sentAt: new Date(now.getTime() - 5 * dayMs).toISOString(),
      sentBy: "Priya Nair",
      expiresAt: new Date(now.getTime() + 9 * dayMs).toISOString(),
      submittedAt: null,
      reviewedAt: null,
      reviewedBy: "",
      reviewNote: "",
      score: null,
      rating: "",
      deliveryReceipt: "example",
      answers: seededAnswers.map((answer, i) => ({
        questionCode: core.questions[i].code,
        answer,
        comment: answer === "No" ? "Rolling out hardware keys next quarter." : "",
      })),
    });
  }
}

function addSuggestions(
  state: State,
  world: DemoWorld,
  items: typeof discoveryFixtures,
  nowIso: string,
) {
  const known = new Set<string>([
    ...state.discoveries.map((d) => d.name.toLowerCase()),
    ...world.vendors.map((v) => v.name.toLowerCase()),
  ]);
  let added = 0;
  for (const item of items) {
    if (!item.name || known.has(item.name.toLowerCase())) continue;
    known.add(item.name.toLowerCase());
    state.discoveries.push({
      id: nextId(state),
      name: item.name,
      source: item.source,
      detail: item.detail,
      suggestedCriticality: item.suggestedCriticality,
      dataCategories: [...item.dataCategories],
      status: "New",
      firstSeenAt: nowIso,
      decidedAt: null,
      decidedBy: "",
      vendorId: null,
    });
    added += 1;
  }
  return added;
}

function sortedDiscoveries(state: State) {
  return state.discoveries
    .slice()
    .sort(
      (a, b) =>
        (a.status === "New" ? 0 : 1) - (b.status === "New" ? 0 : 1) || a.name.localeCompare(b.name),
    );
}

function portalView(state: State, assessment: StoredAssessment): VendorPortalView {
  const template =
    vendorQuestionnaireTemplates.find((item) => item.code === assessment.templateCode) ??
    vendorQuestionnaireTemplates[0];
  return {
    companyName: company.name,
    vendorName: assessment.vendorName,
    contactName: assessment.contactName,
    status: effectiveAssessmentStatus(assessment, Date.now()),
    expiresAt: assessment.expiresAt,
    template,
    answers: assessment.answers,
    documents: state.documents
      .filter((doc) => doc.assessmentId === assessment.id)
      .sort((a, b) => a.uploadedAt.localeCompare(b.uploadedAt))
      .map((doc) => ({ kind: doc.kind, fileName: doc.fileName, uploadedAt: doc.uploadedAt })),
  };
}

function openPortalAssessment(state: State, token: string): StoredAssessment {
  const assessment = state.assessments.find((item) => item.token === token);
  if (!assessment || assessment.status === "Revoked") throw new Fail(404, "not_found");
  const expired = Date.now() >= Date.parse(assessment.expiresAt);
  if (expired && (assessment.status === "Sent" || assessment.status === "InProgress")) {
    throw new Fail(410, "expired");
  }
  return assessment;
}

export async function handlePublicVendorPortal(
  method: string,
  path: string,
  body: Record<string, unknown>,
  world: DemoWorld,
): Promise<Response | null> {
  if (!path.startsWith("/api/v1/public/vendor-portal/")) return null;
  try {
    const state = stateFor(world);
    const rest = path.slice("/api/v1/public/vendor-portal/".length);
    const parts = rest.split("/");
    const token = decodeURIComponent(parts[0] ?? "");
    const action = parts[1] ?? "";
    const assessment = openPortalAssessment(state, token);

    if (!action && method === "GET") {
      return json(portalView(state, assessment));
    }

    if (action === "answers" && method === "POST") {
      const status = effectiveAssessmentStatus(assessment, Date.now());
      if (status !== "Sent" && status !== "InProgress") throw new Fail(400, "locked");
      const template =
        vendorQuestionnaireTemplates.find((item) => item.code === assessment.templateCode) ??
        vendorQuestionnaireTemplates[0];
      const validCodes = new Set(template.questions.map((q) => q.code));
      const incoming = Array.isArray(body.answers) ? body.answers : [];
      const seen = new Set<string>();
      const parsed: VendorAnswer[] = [];
      for (const raw of incoming) {
        if (!raw || typeof raw !== "object") throw new Fail(400, "invalid_answers");
        const entry = raw as Record<string, unknown>;
        const code = text(entry.questionCode);
        const answer = text(entry.answer);
        const comment = typeof entry.comment === "string" ? entry.comment.trim() : "";
        if (
          !validCodes.has(code) ||
          !(answerValues as readonly string[]).includes(answer) ||
          comment.length > 2000 ||
          seen.has(code)
        ) {
          throw new Fail(400, "invalid_answers");
        }
        seen.add(code);
        parsed.push({ questionCode: code, answer, comment });
      }
      for (const item of parsed) {
        const existing = assessment.answers.find((a) => a.questionCode === item.questionCode);
        if (existing) {
          existing.answer = item.answer;
          existing.comment = item.comment;
        } else {
          assessment.answers.push(item);
        }
      }
      if (body.submit === true) {
        const scored = scoreAssessment(template, assessment.answers);
        if (scored.answered < scored.total) throw new Fail(400, "incomplete");
        assessment.status = "Submitted";
        assessment.submittedAt = new Date().toISOString();
        assessment.score = scored.percent;
        assessment.rating = scored.rating;
      } else {
        assessment.status = "InProgress";
      }
      return json(portalView(state, assessment));
    }

    if (action === "documents" && method === "POST") {
      const status = effectiveAssessmentStatus(assessment, Date.now());
      if (status !== "Sent" && status !== "InProgress") throw new Fail(400, "locked");
      const count = state.documents.filter((doc) => doc.assessmentId === assessment.id).length;
      if (count >= maxDocumentsPerAssessment) throw new Fail(400, "too_many_documents");
      const kind = text(body.kind);
      const fileName = text(body.fileName)
        .replace(/[^a-zA-Z0-9._ -]/g, "")
        .slice(-120);
      const bytes = decodeBase64(String(body.contentBase64 ?? ""));
      if (!(documentKinds as readonly string[]).includes(kind) || !fileName || !bytes) {
        throw new Fail(400, "invalid_document");
      }
      const rawText = new TextDecoder().decode(bytes);
      const nowIso = new Date().toISOString();
      const doc: StoredDocument = {
        id: nextId(state),
        vendorId: assessment.vendorId,
        vendorName: assessment.vendorName,
        assessmentId: assessment.id,
        kind,
        fileName,
        sizeBytes: bytes.byteLength,
        sha256: await sha256Bytes(bytes),
        uploadedAt: nowIso,
        uploadedBy: `${assessment.contactName} (vendor)`,
        reportPeriod: "",
        opinion: "",
        exceptions: [],
        cuecs: [],
        summaryText: "",
        summaryMode: "",
        summarizedAt: null,
        signedOffBy: "",
        signedOffAt: null,
        signOffNote: "",
        rawText,
      };
      if (kind === "Soc2") {
        const summary = summarizeSoc2(assessment.vendorName, rawText);
        Object.assign(doc, summary, { summaryMode: "deterministic", summarizedAt: nowIso });
      }
      state.documents.unshift(doc);
      if (assessment.status === "Sent") assessment.status = "InProgress";
      return json(portalView(state, assessment));
    }

    throw new Fail(404, "not_found");
  } catch (error) {
    if (error instanceof Fail) return json({ code: error.code }, error.status);
    throw error;
  }
}

function publicDoc(doc: StoredDocument): VendorReviewDocument {
  const { rawText: _unused, ...rest } = doc;
  void _unused;
  return rest;
}

async function revokeStep(
  world: DemoWorld,
  state: State,
  checklist: StoredChecklist,
  step: StoredStep,
  actor: Actor,
  log: Log,
) {
  const provider = step.provider || (step.code === "remove-github" ? "GitHub" : "SSO");
  const nowIso = new Date().toISOString();
  const receipt = `mock:${step.id.slice(-8)}`;
  step.done = true;
  step.completedAt = nowIso;
  step.completedBy = actor.name;
  step.result = `${receipt} Simulated revocation in demo workspace`;
  const person = world.people.find((item) => item.id === checklist.personId);
  if (person) {
    for (const account of person.accounts) {
      if (!account.active) continue;
      if (
        (step.code === "revoke-sso" &&
          ["Okta", "Google", "Microsoft", "Entra"].includes(account.provider)) ||
        (step.code === "remove-github" && account.provider.toLowerCase() === "github") ||
        (account.provider.toLowerCase() === step.provider.toLowerCase() &&
          account.accountName === step.accountName)
      ) {
        account.active = false;
      }
    }
  }
  const evidenceBody = JSON.stringify(
    {
      kind: "access_revocation_receipt_v1",
      person: checklist.personEmail,
      checklistId: checklist.id,
      step: step.code,
      provider,
      account: step.accountName,
      receipt,
      revokedAt: nowIso,
      actor: actor.email,
    },
    null,
    2,
  );
  const evidenceId = nextId(state);
  world.evidence.unshift({
    id: evidenceId,
    title: `Access revoked: ${checklist.personName} at ${provider}${step.accountName ? ` ${step.accountName}` : ""}`,
    controlCode: "HR-08",
    source: "Personnel lifecycle",
    collectedAt: nowIso,
    integrity: "verified",
    sha256: await sha256Bytes(new TextEncoder().encode(evidenceBody)),
    lockedUntil: null,
    body: evidenceBody,
  });
  step.evidenceId = evidenceId;
  log("lifecycle.revoke", `${checklist.personEmail} ${provider} ${receipt}`);
}

export async function handleLifecycle(
  method: string,
  path: string,
  body: Record<string, unknown>,
  world: DemoWorld,
  actor: Actor,
  log: Log,
): Promise<Response | null> {
  if (
    !path.startsWith("/api/v1/lifecycle") &&
    !path.startsWith("/api/v1/me/lifecycle") &&
    !path.startsWith("/api/v1/vendor-risk")
  ) {
    return null;
  }

  try {
    const state = stateFor(world);

    // --- Personnel Lifecycle routes ---
    if (path === "/api/v1/lifecycle/overview" && method === "GET") {
      const activePeople = world.people.filter((p) => p.state !== "Offboarded");
      const groups: EmployeeGroup[] = state.groups
        .slice()
        .sort((a, b) => a.code.localeCompare(b.code))
        .map((group) => ({
          ...group,
          memberCount: activePeople.filter((p) => inGroup(group, p.department, p.role)).length,
        }));
      const overview: LifecycleOverview = {
        settings: { ...state.settings },
        groups,
        templates: state.templates
          .slice()
          .sort((a, b) => a.kind.localeCompare(b.kind) || a.order - b.order),
        checklists: state.checklists
          .slice()
          .sort((a, b) => b.triggeredAt.localeCompare(a.triggeredAt))
          .map((item) => toChecklistDto(world, state, item)),
        tests: evaluateTests(state, Date.now()),
        revokerMode: "mock",
      };
      return json(overview);
    }

    if (path === "/api/v1/lifecycle/settings" && method === "POST") {
      requireWrite(actor);
      const access = Number(body.offboardingAccessSlaHours);
      const complete = Number(body.offboardingCompleteSlaHours);
      const onboardingDays = Number(body.onboardingDays);
      if (
        !Number.isFinite(access) ||
        !Number.isFinite(complete) ||
        !Number.isFinite(onboardingDays) ||
        access < 1 ||
        access > 720 ||
        complete < 1 ||
        complete > 1440 ||
        complete < access ||
        onboardingDays < 1 ||
        onboardingDays > 120
      ) {
        throw new Fail(400, "invalid_settings");
      }
      state.settings = {
        offboardingAccessSlaHours: access,
        offboardingCompleteSlaHours: complete,
        onboardingDays,
      };
      evaluateTests(state, Date.now());
      log(
        "lifecycle.settings",
        `access ${access}h, offboarding ${complete}h, onboarding ${onboardingDays}d`,
      );
      return json({ ...state.settings });
    }

    if (path === "/api/v1/lifecycle/groups" && method === "POST") {
      requireWrite(actor);
      const code = text(body.code).toLowerCase();
      const name = text(body.name);
      const description = text(body.description);
      if (
        !code ||
        code.length > 64 ||
        !/^[a-z0-9-]+$/.test(code) ||
        !name ||
        name.length > 120 ||
        description.length > 500
      ) {
        throw new Fail(400, "invalid_group");
      }
      const next: StoredGroup = {
        code,
        name,
        description,
        departments: listOfStrings(body.departments),
        roleKeywords: listOfStrings(body.roleKeywords),
        policyCodes: listOfStrings(body.policyCodes),
        trainingCodes: listOfStrings(body.trainingCodes),
      };
      const existing = state.groups.findIndex((g) => g.code === code);
      if (existing >= 0) state.groups[existing] = next;
      else state.groups.push(next);
      log("lifecycle.group", code);
      const memberCount = world.people.filter(
        (p) => p.state !== "Offboarded" && inGroup(next, p.department, p.role),
      ).length;
      return json({ ...next, memberCount });
    }

    if (path === "/api/v1/lifecycle/templates" && method === "POST") {
      requireWrite(actor);
      const kind = text(body.kind);
      const code = text(body.code).toLowerCase();
      const title = text(body.title);
      const category = text(body.category);
      const groupCode = text(body.groupCode).toLowerCase();
      const slaHours = Number(body.slaHours ?? 0);
      const revokesAccess = Boolean(body.revokesAccess);
      const perAccount = Boolean(body.perAccount);
      if (
        (kind !== "Onboarding" && kind !== "Offboarding") ||
        !code ||
        code.length > 64 ||
        !/^[a-z0-9-]+$/.test(code) ||
        !title ||
        title.length > 200 ||
        !category ||
        category.length > 64 ||
        slaHours < 0 ||
        slaHours > 2880 ||
        (kind === "Onboarding" && revokesAccess) ||
        (groupCode && !state.groups.some((g) => g.code === groupCode))
      ) {
        throw new Fail(400, "invalid_template");
      }
      const existingId = typeof body.id === "string" && body.id ? body.id : null;
      if (
        state.templates.some(
          (item) =>
            item.id !== existingId &&
            item.kind === kind &&
            item.code === code &&
            item.groupCode === groupCode,
        )
      ) {
        throw new Fail(400, "duplicate_template");
      }
      let step: LifecycleTemplateStep;
      if (existingId) {
        const found = state.templates.find((item) => item.id === existingId);
        if (!found) throw new Fail(404, "not_found");
        Object.assign(found, {
          kind,
          code,
          title,
          category,
          slaHours,
          groupCode,
          revokesAccess,
          perAccount,
        });
        step = found;
      } else {
        const maxOrder = state.templates
          .filter((item) => item.kind === kind)
          .reduce((max, item) => Math.max(max, item.order), -1);
        step = {
          id: nextId(state),
          kind,
          code,
          title,
          category,
          slaHours,
          order: maxOrder + 1,
          groupCode,
          revokesAccess,
          perAccount,
        };
        state.templates.push(step);
      }
      log("lifecycle.template", `${kind} ${code}`);
      return json(step);
    }

    const deleteTemplateMatch = /^\/api\/v1\/lifecycle\/templates\/([^/]+)$/.exec(path);
    if (deleteTemplateMatch && method === "DELETE") {
      requireWrite(actor);
      const id = decodeURIComponent(deleteTemplateMatch[1]);
      const index = state.templates.findIndex((item) => item.id === id);
      if (index < 0) throw new Fail(404, "not_found");
      const [removed] = state.templates.splice(index, 1);
      log("lifecycle.template.delete", `${removed.kind} ${removed.code}`);
      return json(true);
    }

    if (path === "/api/v1/lifecycle/sync" && method === "POST") {
      requireWrite(actor);
      const today = todayStr();
      const leaverFrom = addDaysStr(today, -7);
      const hireFrom = addDaysStr(today, -state.settings.onboardingDays);
      const horizon = addDaysStr(today, 30);
      let offboardingStarted = 0;
      let onboardingStarted = 0;
      for (const person of world.people) {
        if (
          person.leftOn &&
          person.leftOn >= leaverFrom &&
          person.leftOn <= horizon &&
          person.state !== "Offboarded" &&
          !state.checklists.some((c) => c.personId === person.id && c.kind === "Offboarding")
        ) {
          startChecklist(
            state,
            person,
            "Offboarding",
            "Hris",
            "Termination date from the HR system",
            new Date(`${person.leftOn}T00:00:00Z`),
            person.leftOn,
          );
          person.state = "Offboarding";
          offboardingStarted += 1;
        }
        if (
          person.hireOn &&
          person.hireOn >= hireFrom &&
          person.hireOn <= horizon &&
          !person.leftOn &&
          person.state === "Active" &&
          !state.checklists.some((c) => c.personId === person.id && c.kind === "Onboarding")
        ) {
          startChecklist(
            state,
            person,
            "Onboarding",
            "Hris",
            "Start date from the HR system",
            new Date(`${person.hireOn}T00:00:00Z`),
            person.hireOn,
          );
          onboardingStarted += 1;
        }
      }
      const tests = evaluateTests(state, Date.now());
      log("lifecycle.sync", `${onboardingStarted} onboarding, ${offboardingStarted} offboarding`);
      const result: LifecycleSyncResult = { onboardingStarted, offboardingStarted, tests };
      return json(result);
    }

    const personOffboardMatch = /^\/api\/v1\/lifecycle\/people\/([^/]+)\/offboard$/.exec(path);
    if (personOffboardMatch && method === "POST") {
      requireWrite(actor);
      const personId = decodeURIComponent(personOffboardMatch[1]);
      const person = world.people.find((item) => item.id === personId);
      if (!person) throw new Fail(404, "not_found");
      const reason = text(body.reason);
      if (reason.length > 500) throw new Fail(400, "invalid_offboarding");
      if (
        state.checklists.some(
          (c) => c.personId === personId && c.kind === "Offboarding" && c.state === "Open",
        )
      ) {
        throw new Fail(400, "already_offboarding");
      }
      const terminationOn = text(body.terminationOn) || todayStr();
      const checklist = startChecklist(
        state,
        person,
        "Offboarding",
        "Manual",
        reason || "Offboarding started manually",
        new Date(),
        terminationOn,
      );
      person.leftOn = terminationOn;
      person.state = "Offboarding";
      person.statusReason = checklist.reason;
      evaluateTests(state, Date.now());
      log("lifecycle.offboard", person.email);
      return json(toChecklistDto(world, state, checklist));
    }

    const personOnboardMatch = /^\/api\/v1\/lifecycle\/people\/([^/]+)\/onboard$/.exec(path);
    if (personOnboardMatch && method === "POST") {
      requireWrite(actor);
      const personId = decodeURIComponent(personOnboardMatch[1]);
      const person = world.people.find((item) => item.id === personId);
      if (!person) throw new Fail(404, "not_found");
      if (person.state !== "Active") throw new Fail(400, "invalid_onboarding");
      if (
        state.checklists.some(
          (c) => c.personId === personId && c.kind === "Onboarding" && c.state === "Open",
        )
      ) {
        throw new Fail(400, "already_onboarding");
      }
      const startOn = text(body.startOn) || person.hireOn || todayStr();
      const triggered = startOn > todayStr() ? new Date(`${startOn}T00:00:00Z`) : new Date();
      const checklist = startChecklist(
        state,
        person,
        "Onboarding",
        "Manual",
        "Onboarding started manually",
        triggered,
        startOn,
      );
      evaluateTests(state, Date.now());
      log("lifecycle.onboard", person.email);
      return json(toChecklistDto(world, state, checklist));
    }

    const personGetMatch = /^\/api\/v1\/lifecycle\/people\/([^/]+)$/.exec(path);
    if (personGetMatch && method === "GET") {
      const personId = decodeURIComponent(personGetMatch[1]);
      const person = world.people.find((item) => item.id === personId);
      if (!person) throw new Fail(404, "not_found");
      return json(toPersonLifecycle(world, state, person));
    }

    const stepRevokeMatch = /^\/api\/v1\/lifecycle\/steps\/([^/]+)\/revoke$/.exec(path);
    if (stepRevokeMatch && method === "POST") {
      requireWrite(actor);
      const stepId = decodeURIComponent(stepRevokeMatch[1]);
      const checklist = state.checklists.find((c) => c.steps.some((s) => s.id === stepId));
      const step = checklist?.steps.find((s) => s.id === stepId);
      if (!checklist || !step) throw new Fail(404, "not_found");
      if (!step.revokesAccess) throw new Fail(400, "not_revocable");
      if (!step.done) {
        await revokeStep(world, state, checklist, step, actor, log);
        settleChecklist(world, checklist);
        evaluateTests(state, Date.now());
      }
      return json(toChecklistDto(world, state, checklist));
    }

    const stepCompleteMatch = /^\/api\/v1\/lifecycle\/steps\/([^/]+)$/.exec(path);
    if (stepCompleteMatch && method === "POST") {
      requireWrite(actor);
      const stepId = decodeURIComponent(stepCompleteMatch[1]);
      const note = text(body.note);
      if (note.length > 500) throw new Fail(400, "invalid_step");
      const checklist = state.checklists.find((c) => c.steps.some((s) => s.id === stepId));
      const step = checklist?.steps.find((s) => s.id === stepId);
      if (!checklist || !step) throw new Fail(404, "not_found");
      const done = body.done !== false;
      step.done = done;
      step.completedAt = done ? new Date().toISOString() : null;
      step.completedBy = done ? actor.name : "";
      step.result = done ? note || "Marked done" : "";
      settleChecklist(world, checklist);
      evaluateTests(state, Date.now());
      log("lifecycle.step", `${checklist.personEmail} ${step.code} ${done ? "done" : "reopened"}`);
      return json(toChecklistDto(world, state, checklist));
    }

    const revokeAllMatch = /^\/api\/v1\/lifecycle\/checklists\/([^/]+)\/revoke-all$/.exec(path);
    if (revokeAllMatch && method === "POST") {
      requireWrite(actor);
      const checklistId = decodeURIComponent(revokeAllMatch[1]);
      const checklist = state.checklists.find((c) => c.id === checklistId);
      if (!checklist) throw new Fail(404, "not_found");
      if (checklist.kind !== "Offboarding") throw new Fail(400, "not_revocable");
      let revoked = 0;
      for (const step of checklist.steps
        .filter((s) => s.revokesAccess && !s.done)
        .sort((a, b) => a.order - b.order)) {
        await revokeStep(world, state, checklist, step, actor, log);
        revoked += 1;
      }
      settleChecklist(world, checklist);
      evaluateTests(state, Date.now());
      const result: RevokeAllResult = {
        revoked,
        failed: 0,
        checklist: toChecklistDto(world, state, checklist),
      };
      return json(result);
    }

    if (path === "/api/v1/lifecycle/tests/refresh" && method === "POST") {
      requireWrite(actor);
      return json(evaluateTests(state, Date.now()));
    }

    if (path === "/api/v1/me/lifecycle" && method === "GET") {
      if (actor.role === "Auditor") return new Response(null, { status: 204 });
      const person = world.people.find(
        (item) => item.email.toLowerCase() === actor.email.toLowerCase(),
      );
      if (!person) return new Response(null, { status: 204 });
      return json(toPersonLifecycle(world, state, person));
    }

    const myStepMatch = /^\/api\/v1\/me\/lifecycle\/steps\/([^/]+)$/.exec(path);
    if (myStepMatch && method === "POST") {
      if (actor.role === "Auditor") throw new Fail(403, "forbidden");
      const stepId = decodeURIComponent(myStepMatch[1]);
      const checklist = state.checklists.find((c) => c.steps.some((s) => s.id === stepId));
      const step = checklist?.steps.find((s) => s.id === stepId);
      if (
        !checklist ||
        !step ||
        checklist.personEmail.toLowerCase() !== actor.email.toLowerCase()
      ) {
        throw new Fail(404, "not_found");
      }
      if (checklist.kind !== "Onboarding" || !selfServiceCategories.includes(step.category)) {
        throw new Fail(403, "forbidden");
      }
      const done = body.done !== false;
      step.done = done;
      step.completedAt = done ? new Date().toISOString() : null;
      step.completedBy = done ? actor.name : "";
      step.result = done ? "Confirmed by the employee" : "";
      settleChecklist(world, checklist);
      evaluateTests(state, Date.now());
      log("lifecycle.self", `${step.code} ${done ? "done" : "reopened"}`);
      return json(toChecklistDto(world, state, checklist));
    }

    // --- Vendor Risk v2 routes ---
    if (path === "/api/v1/vendor-risk/inventory" && method === "GET") {
      if (actor.role === "Auditor") return json([]);
      const latestByVendor = new Map<string, StoredAssessment>();
      for (const assessment of state.assessments
        .slice()
        .sort((a, b) => b.sentAt.localeCompare(a.sentAt))) {
        if (!latestByVendor.has(assessment.vendorId)) {
          latestByVendor.set(assessment.vendorId, assessment);
        }
      }
      return json(
        world.vendors
          .slice()
          .sort((a, b) => a.name.localeCompare(b.name))
          .map((vendor) =>
            toVendorRiskRow(vendor, state.profiles.get(vendor.id), latestByVendor.get(vendor.id)),
          ),
      );
    }

    const vendorProfileMatch = /^\/api\/v1\/vendor-risk\/vendors\/([^/]+)\/profile$/.exec(path);
    if (vendorProfileMatch && method === "POST") {
      requireWrite(actor);
      const vendorId = decodeURIComponent(vendorProfileMatch[1]);
      const vendor = world.vendors.find((v) => v.id === vendorId);
      if (!vendor) throw new Fail(404, "not_found");
      const ownerName = text(body.ownerName);
      const ownerEmail = text(body.ownerEmail);
      const contactName = text(body.contactName);
      const contactEmail = text(body.contactEmail);
      const categories = listOfStrings(body.dataCategories);
      const subprocessor = Boolean(body.subprocessor);
      const cadenceRaw =
        body.reviewCadenceMonths === undefined || body.reviewCadenceMonths === null
          ? null
          : Number(body.reviewCadenceMonths);
      if (
        ownerName.length > 200 ||
        contactName.length > 200 ||
        !optionalEmail(ownerEmail) ||
        !optionalEmail(contactEmail) ||
        categories.length > 12 ||
        (cadenceRaw !== null && (!Number.isFinite(cadenceRaw) || cadenceRaw < 1 || cadenceRaw > 60))
      ) {
        throw new Fail(400, "invalid_vendor_profile");
      }
      const risk = inherentRisk(vendor.criticality, categories, subprocessor);
      const profile: StoredVendorProfile = {
        vendorId: vendor.id,
        ownerName,
        ownerEmail,
        contractRenewalOn: text(body.contractRenewalOn) || null,
        dataCategories: categories,
        subprocessor,
        reviewCadenceMonths: cadenceRaw ?? risk.defaultCadenceMonths,
        lastReviewedOn: text(body.lastReviewedOn) || null,
        contactName,
        contactEmail,
      };
      state.profiles.set(vendor.id, profile);
      log("vendor.profile", vendor.name);
      const latest = state.assessments
        .filter((a) => a.vendorId === vendor.id)
        .sort((a, b) => b.sentAt.localeCompare(a.sentAt))[0];
      return json(toVendorRiskRow(vendor, profile, latest));
    }

    const vendorQuestionnairesMatch =
      /^\/api\/v1\/vendor-risk\/vendors\/([^/]+)\/questionnaires$/.exec(path);
    if (vendorQuestionnairesMatch && method === "POST") {
      requireWrite(actor);
      const vendorId = decodeURIComponent(vendorQuestionnairesMatch[1]);
      const vendor = world.vendors.find((v) => v.id === vendorId);
      if (!vendor) throw new Fail(404, "not_found");
      const templateCode = text(body.templateCode);
      const template = vendorQuestionnaireTemplates.find(
        (tpl) => tpl.code.toLowerCase() === templateCode.toLowerCase(),
      );
      const contactName = text(body.contactName);
      const contactEmail = text(body.contactEmail);
      const days = Number(body.expiresInDays ?? 14);
      if (
        !template ||
        !contactName ||
        contactName.length > 200 ||
        !contactEmail ||
        !optionalEmail(contactEmail) ||
        !Number.isFinite(days) ||
        days < 1 ||
        days > 60
      ) {
        throw new Fail(400, "invalid_questionnaire");
      }
      const now = new Date();
      const id = nextId(state);
      const token = `${tenantHex}.demo_${id.replace(/-/g, "")}`;
      const assessment: StoredAssessment = {
        id,
        vendorId: vendor.id,
        vendorName: vendor.name,
        templateCode: template.code,
        contactName,
        contactEmail,
        token,
        status: "Sent",
        sentAt: now.toISOString(),
        sentBy: actor.name,
        expiresAt: new Date(now.getTime() + days * dayMs).toISOString(),
        submittedAt: null,
        reviewedAt: null,
        reviewedBy: "",
        reviewNote: "",
        score: null,
        rating: "",
        deliveryReceipt: `demo:${id.slice(-8)}`,
        answers: [],
      };
      state.assessments.unshift(assessment);
      if (vendor.reviewState === "NotStarted" || vendor.reviewState === "Expired") {
        vendor.reviewState = "InReview";
      }
      const profile = state.profiles.get(vendor.id);
      if (profile && !profile.contactEmail) {
        profile.contactName = contactName;
        profile.contactEmail = contactEmail;
      }
      log("vendor.questionnaire.send", `${vendor.name} ${template.code}`);
      const sent: SentVendorQuestionnaire = {
        assessment: toAssessmentDto(assessment),
        portalPath: `/vendor-portal/${token}`,
      };
      return json(sent);
    }

    const vendorDocumentsMatch = /^\/api\/v1\/vendor-risk\/vendors\/([^/]+)\/documents$/.exec(path);
    if (vendorDocumentsMatch && method === "POST") {
      requireWrite(actor);
      const vendorId = decodeURIComponent(vendorDocumentsMatch[1]);
      const vendor = world.vendors.find((v) => v.id === vendorId);
      if (!vendor) throw new Fail(404, "not_found");
      const kind = text(body.kind);
      const fileName = text(body.fileName)
        .replace(/[^a-zA-Z0-9._ -]/g, "")
        .slice(-120);
      const bytes = decodeBase64(String(body.contentBase64 ?? ""));
      if (!(documentKinds as readonly string[]).includes(kind) || !fileName || !bytes) {
        throw new Fail(400, "invalid_document");
      }
      const rawText = new TextDecoder().decode(bytes);
      const nowIso = new Date().toISOString();
      const doc: StoredDocument = {
        id: nextId(state),
        vendorId: vendor.id,
        vendorName: vendor.name,
        assessmentId: null,
        kind,
        fileName,
        sizeBytes: bytes.byteLength,
        sha256: await sha256Bytes(bytes),
        uploadedAt: nowIso,
        uploadedBy: actor.name,
        reportPeriod: "",
        opinion: "",
        exceptions: [],
        cuecs: [],
        summaryText: "",
        summaryMode: "",
        summarizedAt: null,
        signedOffBy: "",
        signedOffAt: null,
        signOffNote: "",
        rawText,
      };
      if (kind === "Soc2") {
        const summary = summarizeSoc2(vendor.name, rawText);
        Object.assign(doc, summary, { summaryMode: "deterministic", summarizedAt: nowIso });
      }
      state.documents.unshift(doc);
      log("vendor.document.upload", `${vendor.name} ${kind} ${fileName}`);
      return json(publicDoc(doc));
    }

    const vendorDetailMatch = /^\/api\/v1\/vendor-risk\/vendors\/([^/]+)$/.exec(path);
    if (vendorDetailMatch && method === "GET") {
      if (actor.role === "Auditor") throw new Fail(403, "forbidden");
      const vendorId = decodeURIComponent(vendorDetailMatch[1]);
      const vendor = world.vendors.find((v) => v.id === vendorId);
      if (!vendor) throw new Fail(404, "not_found");
      const assessments = state.assessments
        .filter((a) => a.vendorId === vendor.id)
        .sort((a, b) => b.sentAt.localeCompare(a.sentAt));
      const documents = state.documents
        .filter((d) => d.vendorId === vendor.id)
        .sort((a, b) => b.uploadedAt.localeCompare(a.uploadedAt))
        .map(publicDoc);
      const reminders: VendorReminder[] = world.workTasks
        .filter((task) => task.linkedKind === "Vendor" && task.linkedCode === vendor.name)
        .sort((a, b) => a.dueOn.localeCompare(b.dueOn))
        .map((task) => ({
          id: task.id,
          title: task.title,
          linkedCode: task.linkedCode,
          dueOn: task.dueOn,
          status: task.status,
        }));
      const detail: VendorRiskDetail = {
        vendor: toVendorRiskRow(vendor, state.profiles.get(vendor.id), assessments[0]),
        assessments: assessments.map(toAssessmentDto),
        documents,
        reminders,
      };
      return json(detail);
    }

    if (path === "/api/v1/vendor-risk/discovery" && method === "GET") {
      if (actor.role === "Auditor") return json([]);
      return json(sortedDiscoveries(state));
    }

    if (path === "/api/v1/vendor-risk/discovery/scan" && method === "POST") {
      requireWrite(actor);
      const added = addSuggestions(state, world, discoveryFixtures, new Date().toISOString());
      log("vendor.discovery.scan", `${discoveryFixtures.length} found, ${added} new (mock)`);
      const result: VendorDiscoveryScanResult = {
        found: discoveryFixtures.length,
        added,
        sources: [...new Set(discoveryFixtures.map((f) => f.source))],
        items: sortedDiscoveries(state),
      };
      return json(result);
    }

    const discoveryDecisionMatch =
      /^\/api\/v1\/vendor-risk\/discovery\/([^/]+)\/(accept|ignore)$/.exec(path);
    if (discoveryDecisionMatch && method === "POST") {
      requireWrite(actor);
      const id = decodeURIComponent(discoveryDecisionMatch[1]);
      const accept = discoveryDecisionMatch[2] === "accept";
      const suggestion = state.discoveries.find((d) => d.id === id);
      if (!suggestion) throw new Fail(404, "not_found");
      if (suggestion.status !== "New") throw new Fail(400, "already_decided");
      const nowIso = new Date().toISOString();
      suggestion.status = accept ? "Accepted" : "Ignored";
      suggestion.decidedAt = nowIso;
      suggestion.decidedBy = actor.name;
      if (accept) {
        let vendor = world.vendors.find(
          (v) => v.name.toLowerCase() === suggestion.name.toLowerCase(),
        );
        if (!vendor) {
          const vendorId = nextId(state);
          vendor = {
            id: vendorId,
            name: suggestion.name,
            service: `Discovered from ${suggestion.source}`,
            criticality: suggestion.suggestedCriticality || "Medium",
            dataAccess:
              suggestion.dataCategories.length > 0
                ? suggestion.dataCategories.join(", ")
                : "To be assessed",
            reviewState: "NotStarted",
            reviewDueOn: addDaysStr(todayStr(), 30),
            riskRating: 0,
            documents: [],
            decision: "Pending",
          };
          world.vendors.push(vendor);
          const risk = inherentRisk(vendor.criticality, suggestion.dataCategories, false);
          state.profiles.set(vendor.id, {
            vendorId: vendor.id,
            ownerName: actor.name,
            ownerEmail: actor.email,
            contractRenewalOn: null,
            dataCategories: [...suggestion.dataCategories],
            subprocessor: false,
            reviewCadenceMonths: risk.defaultCadenceMonths,
            lastReviewedOn: null,
            contactName: "",
            contactEmail: "",
          });
        }
        suggestion.vendorId = vendor.id;
      }
      log(accept ? "vendor.discovery.accept" : "vendor.discovery.ignore", suggestion.name);
      return json(suggestion);
    }

    if (path === "/api/v1/vendor-risk/templates" && method === "GET") {
      return json(vendorQuestionnaireTemplates);
    }

    if (path === "/api/v1/vendor-risk/assessments" && method === "GET") {
      if (actor.role === "Auditor") return json([]);
      return json(
        state.assessments
          .slice()
          .sort((a, b) => b.sentAt.localeCompare(a.sentAt))
          .map(toAssessmentDto),
      );
    }

    const reviewAssessmentMatch = /^\/api\/v1\/vendor-risk\/assessments\/([^/]+)\/review$/.exec(
      path,
    );
    if (reviewAssessmentMatch && method === "POST") {
      requireWrite(actor);
      const id = decodeURIComponent(reviewAssessmentMatch[1]);
      const decision = text(body.decision);
      const note = text(body.note);
      if (!["Approve", "Reject", "Reopen"].includes(decision) || note.length > 1000) {
        throw new Fail(400, "invalid_review");
      }
      const assessment = state.assessments.find((a) => a.id === id);
      if (!assessment) throw new Fail(404, "not_found");
      if (assessment.status !== "Submitted") throw new Fail(400, "not_submitted");
      const now = new Date();
      const vendor = world.vendors.find((v) => v.id === assessment.vendorId);
      if (decision === "Reopen") {
        assessment.status = "InProgress";
        assessment.submittedAt = null;
        if (Date.parse(assessment.expiresAt) <= now.getTime()) {
          assessment.expiresAt = new Date(now.getTime() + 7 * dayMs).toISOString();
        }
      } else {
        assessment.status = "Reviewed";
        assessment.reviewedAt = now.toISOString();
        assessment.reviewedBy = actor.name;
        if (vendor) {
          vendor.decision = decision === "Approve" ? "Approved" : "Rejected";
          vendor.reviewState = decision === "Approve" ? "Approved" : "InReview";
          let profile = state.profiles.get(vendor.id);
          if (!profile) {
            const categories = inferCategories(vendor.dataAccess);
            const risk = inherentRisk(vendor.criticality, categories, false);
            profile = {
              vendorId: vendor.id,
              ownerName: actor.name,
              ownerEmail: actor.email,
              contractRenewalOn: null,
              dataCategories: categories,
              subprocessor: false,
              reviewCadenceMonths: risk.defaultCadenceMonths,
              lastReviewedOn: null,
              contactName: "",
              contactEmail: "",
            };
            state.profiles.set(vendor.id, profile);
          }
          if (decision === "Approve") {
            const today = todayStr(now);
            profile.lastReviewedOn = today;
            vendor.reviewDueOn = addMonthsStr(today, profile.reviewCadenceMonths);
          }
        }
      }
      assessment.reviewNote = note;
      log("vendor.questionnaire.review", `${assessment.vendorName} ${decision}`);
      return json(toAssessmentDto(assessment));
    }

    const revokeAssessmentMatch = /^\/api\/v1\/vendor-risk\/assessments\/([^/]+)\/revoke$/.exec(
      path,
    );
    if (revokeAssessmentMatch && method === "POST") {
      requireWrite(actor);
      const id = decodeURIComponent(revokeAssessmentMatch[1]);
      const assessment = state.assessments.find((a) => a.id === id);
      if (!assessment) throw new Fail(404, "not_found");
      if (assessment.status === "Reviewed" || assessment.status === "Revoked") {
        throw new Fail(400, "not_revocable");
      }
      assessment.status = "Revoked";
      log("vendor.questionnaire.revoke", assessment.vendorName);
      return json(toAssessmentDto(assessment));
    }

    if (path === "/api/v1/vendor-risk/documents" && method === "GET") {
      if (actor.role === "Auditor") return json([]);
      return json(
        state.documents
          .slice()
          .sort((a, b) => b.uploadedAt.localeCompare(a.uploadedAt))
          .map(publicDoc),
      );
    }

    const summarizeDocMatch = /^\/api\/v1\/vendor-risk\/documents\/([^/]+)\/summarize$/.exec(path);
    if (summarizeDocMatch && method === "POST") {
      requireWrite(actor);
      const id = decodeURIComponent(summarizeDocMatch[1]);
      const doc = state.documents.find((d) => d.id === id);
      if (!doc) throw new Fail(404, "not_found");
      const summary = summarizeSoc2(doc.vendorName, doc.rawText);
      Object.assign(doc, summary, {
        summaryMode: "deterministic",
        summarizedAt: new Date().toISOString(),
        signedOffAt: null,
        signedOffBy: "",
        signOffNote: "",
      });
      log("vendor.document.summarize", `${doc.vendorName} ${doc.fileName}`);
      return json(publicDoc(doc));
    }

    const signOffDocMatch = /^\/api\/v1\/vendor-risk\/documents\/([^/]+)\/sign-off$/.exec(path);
    if (signOffDocMatch && method === "POST") {
      requireWrite(actor);
      const id = decodeURIComponent(signOffDocMatch[1]);
      const note = text(body.note);
      if (note.length > 1000) throw new Fail(400, "invalid_signoff");
      const doc = state.documents.find((d) => d.id === id);
      if (!doc) throw new Fail(404, "not_found");
      if (!doc.summarizedAt) throw new Fail(400, "not_summarized");
      const nowIso = new Date().toISOString();
      doc.signedOffAt = nowIso;
      doc.signedOffBy = actor.name;
      doc.signOffNote = note;
      const evidenceBody = JSON.stringify(
        {
          kind: "vendor_document_signoff_v1",
          vendor: doc.vendorName,
          document: doc.fileName,
          documentKind: doc.kind,
          documentSha256: doc.sha256,
          reportPeriod: doc.reportPeriod,
          opinion: doc.opinion,
          exceptions: doc.exceptions,
          cuecs: doc.cuecs,
          summary: doc.summaryText,
          signedOffBy: doc.signedOffBy,
          signedOffAt: nowIso,
          note,
        },
        null,
        2,
      );
      world.evidence.unshift({
        id: nextId(state),
        title: `Vendor report reviewed: ${doc.vendorName} ${doc.kind}`,
        controlCode: "GV-04",
        source: "Vendor risk",
        collectedAt: nowIso,
        integrity: "verified",
        sha256: await sha256Bytes(new TextEncoder().encode(evidenceBody)),
        lockedUntil: null,
        body: evidenceBody,
      });
      log("vendor.document.signoff", `${doc.vendorName} ${doc.fileName}`);
      return json(publicDoc(doc));
    }

    if (path === "/api/v1/vendor-risk/reminders/sync" && method === "POST") {
      requireWrite(actor);
      const today = todayStr();
      let created = 0;
      const addTask = (vendor: DemoVendor, row: VendorRiskRow, title: string, dueOn: string) => {
        if (
          world.workTasks.some(
            (task) =>
              task.linkedKind === "Vendor" &&
              task.linkedCode === vendor.name &&
              task.status !== "Done" &&
              task.title === title,
          )
        ) {
          return;
        }
        world.workTasks.push({
          id: nextId(state),
          title,
          linkedKind: "Vendor",
          linkedCode: vendor.name,
          ownerName: row.ownerName,
          ownerEmail: row.ownerEmail,
          dueOn,
          status: "Open",
          recurrence: "None",
        });
        created += 1;
      };
      for (const vendor of world.vendors) {
        const row = toVendorRiskRow(vendor, state.profiles.get(vendor.id), undefined);
        if (row.reviewStatus === "Overdue" || row.reviewStatus === "DueSoon") {
          addTask(vendor, row, reviewTaskPrefix + vendor.name, row.nextReviewOn);
        }
        if (
          (row.renewalState === "Upcoming" || row.renewalState === "Lapsed") &&
          row.contractRenewalOn
        ) {
          const target = addDaysStr(row.contractRenewalOn, -30);
          addTask(vendor, row, renewalTaskPrefix + vendor.name, target < today ? today : target);
        }
      }
      log("vendor.reminders.sync", `${created} created`);
      const reminders: VendorReminder[] = world.workTasks
        .filter(
          (task) =>
            task.linkedKind === "Vendor" &&
            task.status !== "Done" &&
            (task.title.startsWith(reviewTaskPrefix) || task.title.startsWith(renewalTaskPrefix)),
        )
        .sort((a, b) => a.dueOn.localeCompare(b.dueOn))
        .map((task) => ({
          id: task.id,
          title: task.title,
          linkedCode: task.linkedCode,
          dueOn: task.dueOn,
          status: task.status,
        }));
      const result: VendorReminderSyncResult = { created, reminders };
      return json(result);
    }

    return null;
  } catch (error) {
    if (error instanceof Fail) return json({ code: error.code }, error.status);
    throw error;
  }
}
