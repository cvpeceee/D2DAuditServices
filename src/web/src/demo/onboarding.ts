import { company, type DemoRisk, type DemoWorld } from "./catalog";
import { buildHeatmap, criticalCount } from "../features/risks/heatmap";
import type {
  AcceptInviteResult,
  BillingOverview,
  ChangePlanResult,
  CreatedInvite,
  GateDecision,
  Invite,
  InvitePreview,
  Invoice,
  OnboardingState,
  PlanEntitlements,
  ReadinessPlan,
  Recommendation,
  ScimToken,
  SecuritySettings,
  SignupAccepted,
  SignupOptions,
  SignupStatus,
} from "../features/onboarding/types";

type Actor = { email: string; name: string; role: string };
type Log = (action: string, detail: string) => void;

export type DynamicDemoAccount = {
  email: string;
  name: string;
  role: string;
  password: string;
};

class Fail extends Error {
  constructor(
    public status: number,
    public code: string,
  ) {
    super(code);
  }
}

type StoredSignup = {
  id: string;
  companyName: string;
  email: string;
  emailDomain: string;
  displayName: string;
  region: string;
  password: string;
  token: string;
  expiresAt: string;
  status: "PendingVerification" | "Provisioning" | "Ready" | "Failed";
  polls: number;
  tenantId: string | null;
  error: string | null;
};

type StoredInvite = {
  id: string;
  email: string;
  role: string;
  token: string;
  invitedBy: string;
  createdAt: string;
  expiresAt: string;
  acceptedAt: string | null;
  revokedAt: string | null;
};

type StoredOnboarding = {
  companySize: string;
  industry: string;
  cloudProviders: string[];
  dataTypes: string[];
  frameworks: string[];
  scopedControls: string[];
  completedSteps: string[];
  auditTargetDate: string | null;
  dismissed: boolean;
};

type StoredBilling = {
  plan: string;
  status: string;
  provider: string;
  currentPeriodEnd: string | null;
};

type StoredSecurity = {
  ssoEnforced: boolean;
  scimTokenPrefix: string | null;
  scimTokenCreatedAt: string | null;
};

type State = {
  signups: StoredSignup[];
  invites: StoredInvite[];
  accounts: DynamicDemoAccount[];
  onboarding: StoredOnboarding;
  billing: StoredBilling;
  security: StoredSecurity;
  counter: number;
};

const stateByWorld = new WeakMap<DemoWorld, State>();
const tenantId = "a360c8ce-0988-4dfc-9722-bce45697b553";
const dayMs = 86_400_000;

const sizes = ["1-10", "11-50", "51-200", "201-1000", "1000+"];
const industries = [
  "saas",
  "fintech",
  "healthcare",
  "ecommerce",
  "manufacturing",
  "public_sector",
  "ai",
  "other",
];
const cloudOptions = ["aws", "azure", "gcp", "on_prem"];
const dataTypeOptions = [
  "customer_pii",
  "eu_personal_data",
  "health",
  "payment_cards",
  "financial",
  "ai_training",
  "ot_telemetry",
];
const onboardingSteps = ["profile", "frameworks", "integrations", "team", "policies", "plan"];
const validRoles = ["Admin", "ControlOwner", "Editor", "Viewer", "Employee", "Auditor"];
const baseControls = [
  "GV-01",
  "GV-02",
  "RM-01",
  "AC-04",
  "AC-07",
  "AC-12",
  "HR-01",
  "HR-08",
  "IR-02",
  "LG-03",
  "VM-02",
  "BC-04",
];

const disposableDomains = new Set([
  "mailinator.com",
  "yopmail.com",
  "guerrillamail.com",
  "guerrillamail.net",
  "sharklasers.com",
  "10minutemail.com",
  "tempmail.com",
  "temp-mail.org",
  "throwawaymail.com",
  "trashmail.com",
  "getnada.com",
  "dispostable.com",
  "maildrop.cc",
  "mintemail.com",
  "fakeinbox.com",
  "mohmal.com",
  "emailondeck.com",
  "spamgourmet.com",
]);

const freeMailDomains = new Set([
  "gmail.com",
  "googlemail.com",
  "outlook.com",
  "hotmail.com",
  "live.com",
  "msn.com",
  "yahoo.com",
  "icloud.com",
  "me.com",
  "aol.com",
  "proton.me",
  "protonmail.com",
  "gmx.com",
  "mail.com",
  "zoho.com",
  "yandex.com",
]);

const planCatalog: PlanEntitlements[] = [
  {
    plan: "Starter",
    frameworks: 1,
    seats: 10,
    integrations: 3,
    trustCenter: false,
    apiAccess: false,
    monthlyPriceUsd: 399,
  },
  {
    plan: "Growth",
    frameworks: 4,
    seats: 50,
    integrations: 15,
    trustCenter: true,
    apiAccess: true,
    monthlyPriceUsd: 1199,
  },
  {
    plan: "Enterprise",
    frameworks: null,
    seats: null,
    integrations: null,
    trustCenter: true,
    apiAccess: true,
    monthlyPriceUsd: null,
  },
];

const planFeatures = ["frameworks", "seats", "integrations", "trust_center", "api_access"] as const;

const readinessTemplate: { key: string; title: string; at: number; tasks: string[] }[] = [
  {
    key: "scope",
    title: "Scope and kickoff",
    at: 0.05,
    tasks: ["Confirm audit scope and systems", "Name control owners"],
  },
  {
    key: "policies",
    title: "Policies approved",
    at: 0.25,
    tasks: ["Install policy templates", "Collect policy approvals and acknowledgements"],
  },
  {
    key: "controls",
    title: "Controls implemented",
    at: 0.5,
    tasks: ["Close open control gaps", "Connect integrations for automated tests"],
  },
  {
    key: "evidence",
    title: "Evidence collected",
    at: 0.75,
    tasks: ["Collect evidence for every in-scope control", "Run a full test sweep"],
  },
  {
    key: "review",
    title: "Internal readiness review",
    at: 0.9,
    tasks: ["Review open findings with owners", "Prepare the auditor request list"],
  },
  {
    key: "fieldwork",
    title: "Auditor fieldwork",
    at: 1.0,
    tasks: ["Share the audit workspace with the auditor", "Start fieldwork"],
  },
];

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function nextId(state: State) {
  state.counter += 1;
  return `40000000-0000-4000-8000-${state.counter.toString(16).padStart(12, "0")}`;
}

function text(value: unknown) {
  return typeof value === "string" ? value.trim() : "";
}

function todayStr(now = new Date()) {
  return now.toISOString().slice(0, 10);
}

function addDays(base: string, days: number) {
  const date = new Date(`${base}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

function addMonths(base: string, months: number) {
  const date = new Date(`${base}T00:00:00Z`);
  date.setUTCMonth(date.getUTCMonth() + months);
  return date.toISOString().slice(0, 10);
}

function dayDiff(from: string, to: string) {
  return Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / dayMs);
}

function domainOf(email: string) {
  const at = email.lastIndexOf("@");
  return at < 0
    ? ""
    : email
        .slice(at + 1)
        .trim()
        .toLowerCase();
}

function isDisposable(domain: string) {
  let current = domain.trim().toLowerCase();
  while (current.includes(".")) {
    if (disposableDomains.has(current)) return true;
    current = current.slice(current.indexOf(".") + 1);
  }
  return false;
}

function strongPassword(password: string) {
  return (
    password.length >= 12 &&
    password.length <= 256 &&
    /[A-Za-z]/.test(password) &&
    /[0-9]/.test(password)
  );
}

function displayNameFor(email: string) {
  const local = email.split("@")[0] ?? "";
  const parts = local.split(/[._-]+/).filter(Boolean);
  if (parts.length === 0) return email;
  return parts.map((p) => p.charAt(0).toUpperCase() + p.slice(1)).join(" ");
}

function planFor(name: string): PlanEntitlements {
  return planCatalog.find((p) => p.plan === name) ?? planCatalog[0];
}

function nextPlanAbove(name: string): string | null {
  if (name === "Starter") return "Growth";
  if (name === "Growth") return "Enterprise";
  return null;
}

function checkGate(plan: PlanEntitlements, feature: string, used = 0): GateDecision {
  if (feature === "trust_center" || feature === "api_access") {
    const enabled = feature === "trust_center" ? plan.trustCenter : plan.apiAccess;
    let upgradeTo: string | null = null;
    if (!enabled) {
      let next = nextPlanAbove(plan.plan);
      while (next) {
        const candidate = planFor(next);
        if (feature === "trust_center" ? candidate.trustCenter : candidate.apiAccess) {
          upgradeTo = next;
          break;
        }
        next = nextPlanAbove(next);
      }
    }
    return { feature, allowed: enabled, used: 0, limit: null, overLimit: false, upgradeTo };
  }
  const limit =
    feature === "frameworks"
      ? plan.frameworks
      : feature === "seats"
        ? plan.seats
        : plan.integrations;
  const overLimit = limit !== null && used > limit;
  const atOrOver = limit !== null && used >= limit;
  let upgradeTo: string | null = null;
  if (atOrOver) {
    let next = nextPlanAbove(plan.plan);
    while (next) {
      const candidate = planFor(next);
      const cap =
        feature === "frameworks"
          ? candidate.frameworks
          : feature === "seats"
            ? candidate.seats
            : candidate.integrations;
      if (cap === null || cap > used) {
        upgradeTo = next;
        break;
      }
      next = nextPlanAbove(next);
    }
  }
  return { feature, allowed: !atOrOver, used, limit, overLimit, upgradeTo };
}

function inviteState(invite: StoredInvite, nowMs: number): string {
  if (invite.acceptedAt) return "Accepted";
  if (invite.revokedAt) return "Revoked";
  return nowMs >= Date.parse(invite.expiresAt) ? "Expired" : "Pending";
}

function toInviteDto(invite: StoredInvite, nowMs: number): Invite {
  return {
    id: invite.id,
    email: invite.email,
    role: invite.role,
    state: inviteState(invite, nowMs),
    invitedBy: invite.invitedBy,
    createdAt: invite.createdAt,
    expiresAt: invite.expiresAt,
  };
}

function stateFor(world: DemoWorld): State {
  let state = stateByWorld.get(world);
  if (state) return state;
  const now = new Date();
  const today = todayStr(now);
  const firstOfMonth = `${today.slice(0, 8)}01`;
  const periodEnd = addDays(addMonths(firstOfMonth, 1), -1);
  state = {
    signups: [],
    invites: [
      {
        id: "40000000-0000-4000-8000-000000000001",
        email: `maya.lin@${company.domain}`,
        role: "ControlOwner",
        token: "demo_invite_maya_lin_0001",
        invitedBy: "Chandra Vell",
        createdAt: new Date(now.getTime() - dayMs).toISOString(),
        expiresAt: new Date(now.getTime() + 6 * dayMs).toISOString(),
        acceptedAt: null,
        revokedAt: null,
      },
    ],
    accounts: [],
    onboarding: {
      companySize: "51-200",
      industry: "saas",
      cloudProviders: ["aws", "gcp"],
      dataTypes: ["customer_pii", "eu_personal_data"],
      frameworks: ["soc2", "iso27001"],
      scopedControls: [...baseControls, "GV-03", "GV-04", "AC-15", "CFG-01", "PP-01"],
      completedSteps: ["profile", "frameworks", "integrations"],
      auditTargetDate: addDays(today, 120),
      dismissed: false,
    },
    billing: {
      plan: "Growth",
      status: "Active",
      provider: "mock",
      currentPeriodEnd: periodEnd,
    },
    security: {
      ssoEnforced: false,
      scimTokenPrefix: "scim_demo9f2",
      scimTokenCreatedAt: new Date(now.getTime() - 14 * dayMs).toISOString(),
    },
    counter: 10,
  };
  stateByWorld.set(world, state);
  return state;
}

export function findDynamicAccount(world: DemoWorld, email: string): DynamicDemoAccount | null {
  const state = stateByWorld.get(world);
  if (!state) return null;
  const low = email.toLowerCase();
  return state.accounts.find((a) => a.email.toLowerCase() === low) ?? null;
}

function onboardingDto(world: DemoWorld, state: State): OnboardingState {
  const completed = state.onboarding.completedSteps.filter((s) => onboardingSteps.includes(s));
  const percent = Math.round((100 * completed.length) / onboardingSteps.length);
  return {
    companySize: state.onboarding.companySize,
    industry: state.onboarding.industry,
    cloudProviders: [...state.onboarding.cloudProviders],
    dataTypes: [...state.onboarding.dataTypes],
    frameworks: [...state.onboarding.frameworks],
    scopedControls: [...state.onboarding.scopedControls],
    steps: [...onboardingSteps],
    completedSteps: completed,
    auditTargetDate: state.onboarding.auditTargetDate,
    dismissed: state.onboarding.dismissed,
    complete: completed.length === onboardingSteps.length,
    percent,
    options: {
      sizes: [...sizes],
      industries: [...industries],
      cloudProviders: [...cloudOptions],
      dataTypes: [...dataTypeOptions],
      frameworks: world.frameworks.map((f) => f.key).sort(),
    },
  };
}

function recommendFrameworks(body: Record<string, unknown>): Recommendation {
  const companySize = text(body.companySize);
  const industry = text(body.industry);
  const cloudProviders = Array.isArray(body.cloudProviders)
    ? body.cloudProviders.map(text).filter((c) => cloudOptions.includes(c))
    : [];
  const dataTypes = Array.isArray(body.dataTypes)
    ? body.dataTypes.map(text).filter((d) => dataTypeOptions.includes(d))
    : [];
  const international = Boolean(body.international);
  const usesAi = Boolean(body.usesAi);

  const data = new Set(dataTypes);
  const clouds = cloudProviders.filter((c) => c !== "on_prem");
  const picks: { frameworkKey: string; reason: string }[] = [
    { frameworkKey: "soc2", reason: "customer_trust" },
  ];
  const controls = new Set<string>(baseControls);

  const add = (key: string, reason: string, ...scoped: string[]) => {
    if (!picks.some((p) => p.frameworkKey === key)) {
      picks.push({ frameworkKey: key, reason });
    }
    scoped.forEach((code) => controls.add(code));
  };

  const larger = ["51-200", "201-1000", "1000+"].includes(companySize);
  if (larger || international) {
    add("iso27001", "international_or_scale", "GV-03", "GV-04", "AC-15");
    if (clouds.length > 0) add("iso27017", "cloud_services", "CFG-01", "CR-02");
    if (data.has("customer_pii") || data.has("eu_personal_data")) {
      add("iso27018", "cloud_pii", "PP-01", "CR-05");
    }
  }
  if (data.has("health") || industry === "healthcare") {
    add("hipaa", "health_data", "CR-02", "LG-08", "PP-01");
  }
  if (data.has("payment_cards")) {
    add("pci-dss", "payment_cards", "CR-02", "VM-05", "CFG-01", "MAL-01");
  }
  if (data.has("eu_personal_data")) {
    add("gdpr", "eu_personal_data", "PP-01", "CR-05");
  }
  if (industry === "fintech" && (data.has("eu_personal_data") || international)) {
    add("dora", "eu_financial_services", "BC-01", "IR-02", "GV-04");
  }
  if (usesAi || industry === "ai" || data.has("ai_training")) {
    add("iso42001", "ai_management", "GV-03");
    add("nist-ai-rmf", "ai_risk", "RM-01");
    if (data.has("eu_personal_data") || international) {
      add("eu-ai-act", "eu_ai_market");
    }
  }
  if (industry === "manufacturing" || data.has("ot_telemetry")) {
    add("iec62443", "operational_technology", "CFG-01", "PH-02", "EP-08");
  }
  if (industry === "public_sector" || picks.length === 1) {
    add("nist-csf", "baseline", "CM-11", "EP-03");
  }
  if (clouds.length > 0) controls.add("CFG-01");

  return { frameworks: picks, scopedControls: [...controls].sort() };
}

function usageFor(world: DemoWorld, state: State): GateDecision[] {
  const plan = planFor(state.billing.plan);
  const nowMs = Date.now();
  const frameworksUsed = state.onboarding.frameworks.length;
  const activeMembers = world.members.filter((m) => m.active && m.role !== "Auditor").length;
  const pendingInvites = state.invites.filter(
    (i) => inviteState(i, nowMs) === "Pending" && i.role !== "Auditor",
  ).length;
  const seatsUsed = activeMembers + pendingInvites;
  const integrationsUsed = 6;
  return [
    checkGate(plan, "frameworks", frameworksUsed),
    checkGate(plan, "seats", seatsUsed),
    checkGate(plan, "integrations", integrationsUsed),
    checkGate(plan, "trust_center"),
    checkGate(plan, "api_access"),
  ];
}

function invoicesFor(planName: string): Invoice[] {
  const price = planFor(planName).monthlyPriceUsd ?? 4500;
  const today = todayStr();
  const first = `${today.slice(0, 8)}01`;
  const prefix = tenantId.replace(/-/g, "").slice(0, 6).toUpperCase();
  return [0, 1, 2].map((offset) => {
    const issuedOn = addMonths(first, -offset);
    const ym = issuedOn.slice(0, 7).replace("-", "");
    return {
      id: `mock_${prefix}_${ym}`,
      number: `MOCK-${prefix}-${ym}`,
      amountUsd: price,
      status: offset === 0 ? "open" : "paid",
      issuedOn,
      url: null,
    };
  });
}

function signupStatusDto(record: StoredSignup): SignupStatus {
  const percent = record.status === "Ready" ? 100 : record.status === "Provisioning" ? 50 : 0;
  const currentStep =
    record.status === "Ready"
      ? "Workspace is ready"
      : record.status === "Provisioning"
        ? "Seeding controls"
        : record.status;
  const nextAction =
    record.status === "PendingVerification"
      ? "verify_email"
      : record.status === "Provisioning"
        ? "wait"
        : record.status === "Ready"
          ? "login"
          : "contact_support";
  return {
    signupId: record.id,
    status: record.status,
    email: record.email,
    tenantId: record.tenantId,
    companyName: record.companyName,
    percent,
    currentStep,
    error: record.error,
    nextAction,
  };
}

export async function handlePublicOnboarding(
  method: string,
  path: string,
  query: URLSearchParams,
  body: Record<string, unknown>,
  world: DemoWorld,
  knownEmails: string[],
): Promise<Response | null> {
  if (!path.startsWith("/api/v1/signup") && !path.startsWith("/api/v1/invites")) {
    return null;
  }

  try {
    const state = stateFor(world);

    if (path === "/api/v1/signup/options" && method === "GET") {
      const options: SignupOptions = {
        identityMode: "local",
        passwordRequired: true,
        regions: ["US", "EU"],
        minimumPasswordLength: 12,
      };
      return json(options);
    }

    if (path === "/api/v1/signup" && method === "POST") {
      const companyName = text(body.companyName);
      const email = text(body.email).toLowerCase();
      const password = typeof body.password === "string" ? body.password : "";
      const region = text(body.region).toUpperCase();
      const fullName = text(body.fullName);

      if (companyName.length < 2 || companyName.length > 120) {
        throw new Fail(400, "invalid_company");
      }
      if (!/^[^@\s]{1,64}@[a-z0-9.-]+\.[a-z]{2,}$/.test(email)) {
        throw new Fail(400, "invalid_email");
      }
      const domain = domainOf(email);
      if (isDisposable(domain)) throw new Fail(400, "disposable_email");
      if (freeMailDomains.has(domain)) throw new Fail(400, "work_email_required");
      if (region !== "US" && region !== "EU") throw new Fail(400, "invalid_region");
      if (!strongPassword(password)) throw new Fail(400, "weak_password");

      const allEmails = new Set([
        ...knownEmails.map((e) => e.toLowerCase()),
        ...state.accounts.map((a) => a.email.toLowerCase()),
      ]);
      if (allEmails.has(email)) throw new Fail(400, "email_taken");
      if (domain === company.domain.toLowerCase()) throw new Fail(400, "domain_taken");

      const now = new Date();
      const id = nextId(state);
      const token = `demo_signup_${id.replace(/-/g, "")}`;
      const record: StoredSignup = {
        id,
        companyName,
        email,
        emailDomain: domain,
        displayName: fullName ? fullName.slice(0, 200) : displayNameFor(email),
        region,
        password,
        token,
        expiresAt: new Date(now.getTime() + dayMs).toISOString(),
        status: "PendingVerification",
        polls: 0,
        tenantId: null,
        error: null,
      };
      state.signups = state.signups.filter(
        (s) => !(s.email === email && s.status === "PendingVerification"),
      );
      state.signups.push(record);
      const accepted: SignupAccepted = {
        signupId: record.id,
        status: record.status,
        email: record.email,
        expiresAt: record.expiresAt,
        devVerificationUrl: `/signup/verify?token=${encodeURIComponent(token)}`,
      };
      return json(accepted);
    }

    if (path === "/api/v1/signup/verify" && method === "POST") {
      const token = text(body.token);
      if (!token || token.length > 200) throw new Fail(400, "invalid_token");
      const record = state.signups.find((s) => s.token === token);
      if (!record) throw new Fail(400, "invalid_token");
      if (record.status !== "PendingVerification") return json(signupStatusDto(record));
      if (Date.now() >= Date.parse(record.expiresAt)) throw new Fail(400, "token_expired");
      record.status = "Provisioning";
      record.tenantId = tenantId;
      return json(signupStatusDto(record));
    }

    const signupStatusMatch = /^\/api\/v1\/signup\/([^/]+)$/.exec(path);
    if (signupStatusMatch && method === "GET") {
      const id = decodeURIComponent(signupStatusMatch[1]);
      const record = state.signups.find((s) => s.id === id);
      if (!record) throw new Fail(404, "not_found");
      if (record.status === "Provisioning") {
        record.polls += 1;
        record.status = "Ready";
        record.tenantId = tenantId;
        if (!state.accounts.some((a) => a.email === record.email)) {
          state.accounts.push({
            email: record.email,
            name: record.displayName,
            role: "Admin",
            password: record.password,
          });
        }
      }
      return json(signupStatusDto(record));
    }

    if (path === "/api/v1/invites/preview" && method === "GET") {
      const token = text(query.get("token"));
      const invite = state.invites.find((i) => i.token === token);
      if (!invite) throw new Fail(400, "invalid_token");
      const preview: InvitePreview = {
        tenantName: company.name,
        email: invite.email,
        role: invite.role,
        state: inviteState(invite, Date.now()),
        passwordRequired: true,
        expiresAt: invite.expiresAt,
      };
      return json(preview);
    }

    if (path === "/api/v1/invites/accept" && method === "POST") {
      const token = text(body.token);
      const invite = state.invites.find((i) => i.token === token);
      if (!invite) throw new Fail(400, "invalid_token");
      const current = inviteState(invite, Date.now());
      if (current === "Expired") throw new Fail(400, "token_expired");
      if (current === "Revoked") throw new Fail(400, "invite_revoked");
      if (current === "Accepted") throw new Fail(400, "invite_used");
      const password = typeof body.password === "string" ? body.password : "";
      if (!strongPassword(password)) throw new Fail(400, "weak_password");
      const name = text(body.displayName) || displayNameFor(invite.email);
      invite.acceptedAt = new Date().toISOString();
      const existingMember = world.members.find(
        (m) => m.email.toLowerCase() === invite.email.toLowerCase(),
      );
      if (existingMember) {
        existingMember.role = invite.role;
        existingMember.active = true;
      } else {
        world.members.push({
          id: nextId(state),
          name,
          email: invite.email,
          role: invite.role,
          active: true,
        });
      }
      state.accounts = state.accounts.filter((a) => a.email !== invite.email);
      state.accounts.push({ email: invite.email, name, role: invite.role, password });
      const result: AcceptInviteResult = {
        tenantId,
        email: invite.email,
        nextAction: "login",
      };
      return json(result);
    }

    return null;
  } catch (error) {
    if (error instanceof Fail) return json({ code: error.code }, error.status);
    throw error;
  }
}

export async function handleOnboarding(
  method: string,
  path: string,
  body: Record<string, unknown>,
  world: DemoWorld,
  actor: Actor,
  log: Log,
): Promise<Response | null> {
  if (
    !path.startsWith("/api/v1/onboarding") &&
    !path.startsWith("/api/v1/billing") &&
    !path.startsWith("/api/v1/settings/invites") &&
    !path.startsWith("/api/v1/settings/security") &&
    path !== "/api/v1/risks/heatmap" &&
    !/^\/api\/v1\/risks\/[^/]+\/accept$/.test(path)
  ) {
    return null;
  }

  try {
    const state = stateFor(world);
    const canWrite = actor.role !== "Auditor" && actor.role !== "Employee";
    const isAdmin = actor.role === "Admin";

    // --- Onboarding ---
    if (path === "/api/v1/onboarding" && method === "GET") {
      return json(onboardingDto(world, state));
    }

    if (path === "/api/v1/onboarding" && method === "PUT") {
      if (!canWrite) throw new Fail(403, "forbidden");
      const size =
        body.companySize === undefined || body.companySize === null ? null : text(body.companySize);
      const industry =
        body.industry === undefined || body.industry === null ? null : text(body.industry);
      const completeStep =
        body.completeStep === undefined || body.completeStep === null
          ? null
          : text(body.completeStep);
      const clouds = Array.isArray(body.cloudProviders) ? body.cloudProviders.map(text) : null;
      const dataTypes = Array.isArray(body.dataTypes) ? body.dataTypes.map(text) : null;
      const frameworks = Array.isArray(body.frameworks) ? body.frameworks.map(text) : null;
      const scoped = Array.isArray(body.scopedControls) ? body.scopedControls.map(text) : null;
      const knownFrameworks = new Set(world.frameworks.map((f) => f.key));
      if (
        (size !== null && !sizes.includes(size)) ||
        (industry !== null && !industries.includes(industry)) ||
        (clouds !== null && clouds.some((c) => !cloudOptions.includes(c))) ||
        (dataTypes !== null && dataTypes.some((d) => !dataTypeOptions.includes(d))) ||
        (frameworks !== null && frameworks.some((f) => !knownFrameworks.has(f))) ||
        (scoped !== null && (scoped.length > 400 || scoped.some((c) => !c || c.length > 32))) ||
        (completeStep !== null && !onboardingSteps.includes(completeStep))
      ) {
        throw new Fail(400, "invalid_onboarding");
      }
      if (size !== null) state.onboarding.companySize = size;
      if (industry !== null) state.onboarding.industry = industry;
      if (clouds !== null) state.onboarding.cloudProviders = [...new Set(clouds)];
      if (dataTypes !== null) state.onboarding.dataTypes = [...new Set(dataTypes)];
      if (frameworks !== null) state.onboarding.frameworks = [...new Set(frameworks)];
      if (scoped !== null) state.onboarding.scopedControls = [...new Set(scoped)];
      if (completeStep !== null && !state.onboarding.completedSteps.includes(completeStep)) {
        state.onboarding.completedSteps.push(completeStep);
      }
      log("onboarding.saved", completeStep ?? "profile");
      return json(onboardingDto(world, state));
    }

    if (path === "/api/v1/onboarding/recommend" && method === "POST") {
      return json(recommendFrameworks(body));
    }

    if (path === "/api/v1/onboarding/plan" && method === "POST") {
      if (!canWrite) throw new Fail(403, "forbidden");
      const targetDate = text(body.targetDate);
      if (!/^\d{4}-\d{2}-\d{2}$/.test(targetDate)) throw new Fail(400, "invalid_date");
      const today = todayStr();
      const span = dayDiff(today, targetDate);
      if (span < 30) throw new Fail(400, "target_too_soon");
      if (targetDate > addMonths(today, 36)) throw new Fail(400, "target_too_far");
      const label =
        state.onboarding.frameworks.length === 0
          ? ""
          : ` (${state.onboarding.frameworks.map((f) => f.toUpperCase()).join(", ")})`;
      const milestones = readinessTemplate.map((step) => {
        const dueOn = addDays(today, Math.round(span * step.at));
        const tasks = step.tasks.map((title, index) => {
          const rawDue = addDays(dueOn, -3 * (step.tasks.length - 1 - index));
          return {
            title: title + (step.key === "scope" && index === 0 ? label : ""),
            dueOn: rawDue < today ? today : rawDue,
          };
        });
        return { key: step.key, title: step.title, dueOn, tasks };
      });
      world.workTasks = world.workTasks.filter(
        (task) => !(task.linkedKind === "Readiness" && task.status === "Open"),
      );
      let tasksCreated = 0;
      for (const milestone of milestones) {
        for (const task of milestone.tasks) {
          world.workTasks.push({
            id: nextId(state),
            title: task.title,
            linkedKind: "Readiness",
            linkedCode: milestone.key,
            ownerName: actor.name,
            ownerEmail: actor.email,
            dueOn: task.dueOn,
            status: "Open",
            recurrence: "None",
          });
          tasksCreated += 1;
        }
      }
      state.onboarding.auditTargetDate = targetDate;
      if (!state.onboarding.completedSteps.includes("plan")) {
        state.onboarding.completedSteps.push("plan");
      }
      const frameworksGate = checkGate(
        planFor(state.billing.plan),
        "frameworks",
        state.onboarding.frameworks.length,
      );
      log("onboarding.plan_generated", `${targetDate} tasks=${tasksCreated}`);
      const plan: ReadinessPlan = {
        targetDate,
        milestones,
        tasksCreated,
        frameworks: frameworksGate,
      };
      return json(plan);
    }

    if (path === "/api/v1/onboarding/dismiss" && method === "POST") {
      if (!canWrite) throw new Fail(403, "forbidden");
      state.onboarding.dismissed = true;
      return json(onboardingDto(world, state));
    }

    // --- Billing ---
    if (path === "/api/v1/billing" && method === "GET") {
      const overview: BillingOverview = {
        plan: state.billing.plan,
        status: state.billing.status,
        provider: state.billing.provider,
        providerConfigured: false,
        entitlements: planFor(state.billing.plan),
        usage: usageFor(world, state),
        plans: planCatalog,
        invoices: invoicesFor(state.billing.plan),
        currentPeriodEnd: state.billing.currentPeriodEnd,
      };
      return json(overview);
    }

    if (path === "/api/v1/billing/plan" && method === "POST") {
      if (!isAdmin) throw new Fail(403, "forbidden");
      const targetPlan = text(body.plan);
      if (!planCatalog.some((p) => p.plan === targetPlan)) throw new Fail(400, "invalid_plan");
      const previous = state.billing.plan;
      state.billing.plan = targetPlan;
      state.billing.status = "Active";
      log("billing.plan_changed", `${previous}->${targetPlan}`);
      const result: ChangePlanResult = {
        plan: state.billing.plan,
        status: state.billing.status,
        redirectUrl: null,
      };
      return json(result);
    }

    if (path === "/api/v1/billing/portal" && method === "POST") {
      if (!isAdmin) throw new Fail(403, "forbidden");
      return json({ url: "/settings/billing?portal=mock" });
    }

    const entitlementMatch = /^\/api\/v1\/billing\/entitlements\/([^/]+)$/.exec(path);
    if (entitlementMatch && method === "GET") {
      const feature = decodeURIComponent(entitlementMatch[1]);
      if (!(planFeatures as readonly string[]).includes(feature)) {
        throw new Fail(404, "not_found");
      }
      const decision = usageFor(world, state).find((item) => item.feature === feature)!;
      return json(decision);
    }

    // --- Team Invites & Security ---
    if (path === "/api/v1/settings/invites" && method === "GET") {
      if (!isAdmin) throw new Fail(403, "forbidden");
      const nowMs = Date.now();
      return json(
        state.invites
          .slice()
          .sort((a, b) => b.createdAt.localeCompare(a.createdAt))
          .map((invite) => toInviteDto(invite, nowMs)),
      );
    }

    if (path === "/api/v1/settings/invites" && method === "POST") {
      if (!isAdmin) throw new Fail(403, "forbidden");
      const email = text(body.email).toLowerCase();
      const role = text(body.role);
      if (
        !validRoles.includes(role) ||
        email.length < 3 ||
        email.length > 320 ||
        !domainOf(email)
      ) {
        throw new Fail(400, "invalid_member");
      }
      if (world.members.some((m) => m.active && m.email.toLowerCase() === email)) {
        throw new Fail(400, "already_member");
      }
      const now = new Date();
      const nowIso = now.toISOString();
      for (const prev of state.invites) {
        if (prev.email === email && !prev.acceptedAt && !prev.revokedAt) {
          prev.revokedAt = nowIso;
        }
      }
      const seats = usageFor(world, state).find((u) => u.feature === "seats")!;
      const id = nextId(state);
      const token = `demo_invite_${id.replace(/-/g, "")}`;
      const invite: StoredInvite = {
        id,
        email,
        role,
        token,
        invitedBy: actor.name,
        createdAt: nowIso,
        expiresAt: new Date(now.getTime() + 7 * dayMs).toISOString(),
        acceptedAt: null,
        revokedAt: null,
      };
      state.invites.unshift(invite);
      log("member.invite_sent", role);
      const created: CreatedInvite = {
        invite: toInviteDto(invite, now.getTime()),
        devAcceptUrl: `/invite/accept?token=${encodeURIComponent(token)}`,
        seats,
      };
      return json(created);
    }

    const revokeInviteMatch = /^\/api\/v1\/settings\/invites\/([^/]+)\/revoke$/.exec(path);
    if (revokeInviteMatch && method === "POST") {
      if (!isAdmin) throw new Fail(403, "forbidden");
      const id = decodeURIComponent(revokeInviteMatch[1]);
      const invite = state.invites.find((i) => i.id === id);
      if (!invite) throw new Fail(404, "not_found");
      const nowMs = Date.now();
      if (inviteState(invite, nowMs) === "Pending") {
        invite.revokedAt = new Date(nowMs).toISOString();
        log("member.invite_revoked", invite.role);
      }
      return json(toInviteDto(invite, nowMs));
    }

    if (path === "/api/v1/settings/security" && method === "GET") {
      if (!isAdmin) throw new Fail(403, "forbidden");
      const sso = world.sso[0];
      const dto: SecuritySettings = {
        ssoEnforced: state.security.ssoEnforced,
        mode: "mock",
        domain: sso?.domain ?? company.domain,
        connected: Boolean(sso?.connected),
        scimTokenPrefix: state.security.scimTokenPrefix,
        scimTokenCreatedAt: state.security.scimTokenCreatedAt,
        scimEndpoint: `${window.location.origin}/api/v1/scim/v2`,
      };
      return json(dto);
    }

    if (path === "/api/v1/settings/security" && method === "PUT") {
      if (!isAdmin) throw new Fail(403, "forbidden");
      state.security.ssoEnforced = Boolean(body.ssoEnforced);
      log("security.sso_enforced", state.security.ssoEnforced ? "on" : "off");
      const sso = world.sso[0];
      const dto: SecuritySettings = {
        ssoEnforced: state.security.ssoEnforced,
        mode: "mock",
        domain: sso?.domain ?? company.domain,
        connected: Boolean(sso?.connected),
        scimTokenPrefix: state.security.scimTokenPrefix,
        scimTokenCreatedAt: state.security.scimTokenCreatedAt,
        scimEndpoint: `${window.location.origin}/api/v1/scim/v2`,
      };
      return json(dto);
    }

    if (path === "/api/v1/settings/security/scim-token" && method === "POST") {
      if (!isAdmin) throw new Fail(403, "forbidden");
      const nowIso = new Date().toISOString();
      const raw = `scim_${nextId(state).replace(/-/g, "")}`;
      const prefix = raw.slice(0, 12);
      state.security.scimTokenPrefix = prefix;
      state.security.scimTokenCreatedAt = nowIso;
      log("security.scim_token_rotated", prefix);
      const token: ScimToken = { token: raw, prefix, createdAt: nowIso };
      return json(token);
    }

    // --- Risk Heatmap & Formal Risk Acceptance ---
    if (path === "/api/v1/risks/heatmap" && method === "GET") {
      return json({
        inherent: buildHeatmap(world.risks, "inherent"),
        residual: buildHeatmap(world.risks, "residual"),
        criticalInherent: criticalCount(world.risks, "inherent"),
        criticalResidual: criticalCount(world.risks, "residual"),
      });
    }

    const acceptRiskMatch = /^\/api\/v1\/risks\/([^/]+)\/accept$/.exec(path);
    if (acceptRiskMatch && method === "POST") {
      if (!canWrite) throw new Fail(403, "forbidden");
      const code = decodeURIComponent(acceptRiskMatch[1]);
      const today = todayStr();
      const expiresOn = text(body.expiresOn);
      const approver = text(body.approver) || actor.name;
      const rationale = text(body.rationale);
      if (
        !/^\d{4}-\d{2}-\d{2}$/.test(expiresOn) ||
        expiresOn <= today ||
        expiresOn > addMonths(today, 12) ||
        approver.length > 200 ||
        rationale.length > 2000
      ) {
        throw new Fail(400, "invalid_acceptance");
      }
      const risk = world.risks.find((r) => r.code === code) as
        | (DemoRisk & {
            acceptedBy?: string;
            acceptedOn?: string | null;
            acceptanceExpiresOn?: string | null;
          })
        | undefined;
      if (!risk) throw new Fail(404, "not_found");
      risk.treatment = "Accept";
      risk.acceptedBy = approver;
      risk.acceptedOn = today;
      risk.acceptanceExpiresOn = expiresOn;
      if (rationale) risk.treatmentPlan = rationale;
      if (risk.reviewOn > expiresOn || risk.reviewOn < today) {
        risk.reviewOn = expiresOn;
      }
      log("risk.accepted", `${risk.code} until ${expiresOn}`);
      return json(risk);
    }

    return null;
  } catch (error) {
    if (error instanceof Fail) return json({ code: error.code }, error.status);
    throw error;
  }
}
