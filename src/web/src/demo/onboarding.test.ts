import { beforeEach, describe, expect, it } from "vitest";
import { examplePassword } from "./accounts";
import { demoResponse, resetDemo } from "./backend";
import type {
  AcceptInviteResult,
  BillingOverview,
  ChangePlanResult,
  CreatedInvite,
  GateDecision,
  Invite,
  InvitePreview,
  OnboardingState,
  ReadinessPlan,
  Recommendation,
  ScimToken,
  SecuritySettings,
  SignupAccepted,
  SignupOptions,
  SignupStatus,
} from "../features/onboarding/types";

async function loginAs(email: string, password = examplePassword) {
  const res = await demoResponse("/api/v1/auth/login", {
    method: "POST",
    body: JSON.stringify({ email, password }),
  });
  return res;
}

async function call<T>(path: string, method = "GET", body?: unknown) {
  const res = await demoResponse(path, {
    method,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  return { status: res.status, body: (text ? JSON.parse(text) : null) as T };
}

function dateIn(days: number) {
  return new Date(Date.now() + days * 86_400_000).toISOString().slice(0, 10);
}

describe("M11 self-serve signup, onboarding wizard, billing, team invites, and risk acceptance demo", () => {
  beforeEach(async () => {
    await resetDemo();
  });

  it("completes self-serve signup, email verification, provisioning poll, and login", async () => {
    const options = await call<SignupOptions>("/api/v1/signup/options");
    expect(options.status).toBe(200);
    expect(options.body.passwordRequired).toBe(true);

    const freeEmail = await call<{ code: string }>("/api/v1/signup", "POST", {
      companyName: "Acme Corp",
      email: "founder@gmail.com",
      password: "StrongPassword123",
      region: "US",
    });
    expect(freeEmail.status).toBe(400);
    expect(freeEmail.body.code).toBe("work_email_required");

    const accepted = await call<SignupAccepted>("/api/v1/signup", "POST", {
      companyName: "Northwind Health",
      email: "maya@northwindhealth.example",
      password: "StrongPassword123",
      region: "EU",
      fullName: "Maya Brooks",
    });
    expect(accepted.status).toBe(200);
    expect(accepted.body.devVerificationUrl).toContain("/signup/verify?token=");
    const token = new URL(accepted.body.devVerificationUrl!, "http://demo.local").searchParams.get(
      "token",
    );

    const verified = await call<SignupStatus>("/api/v1/signup/verify", "POST", { token });
    expect(verified.status).toBe(200);
    expect(verified.body.status).toBe("Provisioning");

    const polled = await call<SignupStatus>(`/api/v1/signup/${accepted.body.signupId}`);
    expect(polled.status).toBe(200);
    expect(polled.body.status).toBe("Ready");
    expect(polled.body.percent).toBe(100);

    const signIn = await loginAs("maya@northwindhealth.example", "StrongPassword123");
    expect(signIn.status).toBe(200);
  });

  it("runs onboarding wizard recommendation, persistence, readiness plan, and dismiss", async () => {
    expect((await loginAs("chandra.vell@harborline.example")).status).toBe(200);

    const initial = await call<OnboardingState>("/api/v1/onboarding");
    expect(initial.status).toBe(200);
    expect(initial.body.options.frameworks.length).toBeGreaterThan(0);

    const rec = await call<Recommendation>("/api/v1/onboarding/recommend", "POST", {
      companySize: "51-200",
      industry: "healthcare",
      cloudProviders: ["aws"],
      dataTypes: ["health", "eu_personal_data"],
    });
    expect(rec.status).toBe(200);
    const keys = rec.body.frameworks.map((f) => f.frameworkKey);
    expect(keys).toContain("soc2");
    expect(keys).toContain("hipaa");
    expect(keys).toContain("gdpr");

    const saved = await call<OnboardingState>("/api/v1/onboarding", "PUT", {
      companySize: "51-200",
      industry: "healthcare",
      frameworks: ["soc2", "hipaa"],
      completeStep: "frameworks",
    });
    expect(saved.status).toBe(200);
    expect(saved.body.frameworks).toEqual(["soc2", "hipaa"]);

    const tooSoon = await call<{ code: string }>("/api/v1/onboarding/plan", "POST", {
      targetDate: dateIn(10),
    });
    expect(tooSoon.status).toBe(400);
    expect(tooSoon.body.code).toBe("target_too_soon");

    const plan = await call<ReadinessPlan>("/api/v1/onboarding/plan", "POST", {
      targetDate: dateIn(120),
    });
    expect(plan.status).toBe(200);
    expect(plan.body.milestones.length).toBe(6);
    expect(plan.body.tasksCreated).toBe(12);

    const dismissed = await call<OnboardingState>("/api/v1/onboarding/dismiss", "POST");
    expect(dismissed.status).toBe(200);
    expect(dismissed.body.dismissed).toBe(true);
  });

  it("manages billing plans, entitlements, team invites, acceptance, and security settings", async () => {
    expect((await loginAs("chandra.vell@harborline.example")).status).toBe(200);

    const billing = await call<BillingOverview>("/api/v1/billing");
    expect(billing.status).toBe(200);
    expect(billing.body.plans.length).toBe(3);

    const changed = await call<ChangePlanResult>("/api/v1/billing/plan", "POST", {
      plan: "Enterprise",
    });
    expect(changed.status).toBe(200);
    expect(changed.body.plan).toBe("Enterprise");

    const ent = await call<GateDecision>("/api/v1/billing/entitlements/trust_center");
    expect(ent.status).toBe(200);
    expect(ent.body.allowed).toBe(true);

    const created = await call<CreatedInvite>("/api/v1/settings/invites", "POST", {
      email: "jordan.lee@harborline.example",
      role: "Editor",
    });
    expect(created.status).toBe(200);
    expect(created.body.devAcceptUrl).toContain("/invite/accept?token=");
    const inviteToken = new URL(created.body.devAcceptUrl!, "http://demo.local").searchParams.get(
      "token",
    );

    const list = await call<Invite[]>("/api/v1/settings/invites");
    expect(list.status).toBe(200);
    expect(list.body.some((i) => i.email === "jordan.lee@harborline.example")).toBe(true);

    const sec = await call<SecuritySettings>("/api/v1/settings/security", "PUT", {
      ssoEnforced: true,
    });
    expect(sec.status).toBe(200);
    expect(sec.body.ssoEnforced).toBe(true);

    const scim = await call<ScimToken>("/api/v1/settings/security/scim-token", "POST");
    expect(scim.status).toBe(200);
    expect(scim.body.token).toMatch(/^scim_/);

    // Public invite preview & acceptance
    const preview = await call<InvitePreview>(
      `/api/v1/invites/preview?token=${encodeURIComponent(inviteToken!)}`,
    );
    expect(preview.status).toBe(200);
    expect(preview.body.state).toBe("Pending");

    const accepted = await call<AcceptInviteResult>("/api/v1/invites/accept", "POST", {
      token: inviteToken,
      displayName: "Jordan Lee",
      password: "StrongPassword123",
    });
    expect(accepted.status).toBe(200);
    expect(accepted.body.email).toBe("jordan.lee@harborline.example");

    const login = await loginAs("jordan.lee@harborline.example", "StrongPassword123");
    expect(login.status).toBe(200);
  });

  it("serves risk heatmap and formal time-bounded risk acceptance", async () => {
    expect((await loginAs("chandra.vell@harborline.example")).status).toBe(200);
    const heatmap = await call<{ inherent: unknown[]; residual: unknown[] }>(
      "/api/v1/risks/heatmap",
    );
    expect(heatmap.status).toBe(200);
    expect(heatmap.body.inherent.length).toBe(25);

    const risks = await call<{ code: string }[]>("/api/v1/risks");
    const first = risks.body[0];
    const accepted = await call<{
      treatment: string;
      acceptedBy: string;
      acceptanceExpiresOn: string;
    }>(`/api/v1/risks/${encodeURIComponent(first.code)}/accept`, "POST", {
      approver: "VP Security",
      expiresOn: dateIn(90),
      rationale: "Compensating controls in place.",
    });
    expect(accepted.status).toBe(200);
    expect(accepted.body.treatment).toBe("Accept");
    expect(accepted.body.acceptedBy).toBe("VP Security");
  });

  it("blocks non-admin access to billing plan change and team security settings", async () => {
    expect((await loginAs("auditor@northgate.example")).status).toBe(200);
    expect((await call("/api/v1/billing/plan", "POST", { plan: "Growth" })).status).toBe(403);
    expect((await call("/api/v1/settings/invites")).status).toBe(403);
    expect((await call("/api/v1/onboarding/dismiss", "POST")).status).toBe(403);
  });
});
