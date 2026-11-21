import { beforeEach, describe, expect, it } from "vitest";
import { examplePassword } from "./accounts";
import { demoResponse, resetDemo } from "./backend";
import type {
  LifecycleChecklist,
  LifecycleOverview,
  PersonLifecycle,
  RevokeAllResult,
} from "../features/people/lifecycleTypes";
import type {
  SentVendorQuestionnaire,
  VendorDiscovery,
  VendorDiscoveryScanResult,
  VendorPortalView,
  VendorReminderSyncResult,
  VendorReviewDocument,
  VendorRiskDetail,
  VendorRiskRow,
} from "../features/vendors/vendorRiskTypes";

async function loginAs(email: string) {
  const res = await demoResponse("/api/v1/auth/login", {
    method: "POST",
    body: JSON.stringify({ email, password: examplePassword }),
  });
  expect(res.status).toBe(200);
}

async function logout() {
  const res = await demoResponse("/api/v1/auth/logout", { method: "POST" });
  expect(res.status).toBe(204);
}

async function call<T>(path: string, method = "GET", body?: unknown) {
  const res = await demoResponse(path, {
    method,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  return { status: res.status, body: (text ? JSON.parse(text) : null) as T };
}

describe("M11 personnel lifecycle and vendor risk v2 demo", () => {
  beforeEach(async () => {
    await resetDemo();
    await loginAs("chandra.vell@harborline.example");
  });

  it("loads lifecycle overview, saves settings, groups, templates, and revokes leaver access", async () => {
    const overview = await call<LifecycleOverview>("/api/v1/lifecycle/overview");
    expect(overview.status).toBe(200);
    expect(overview.body.groups.length).toBeGreaterThanOrEqual(3);
    expect(overview.body.templates.length).toBeGreaterThanOrEqual(12);
    expect(overview.body.tests.length).toBe(2);
    expect(overview.body.tests[0].outcome).toBe("Fail");

    const samChecklist = overview.body.checklists.find(
      (c) => c.personEmail === "sam.leaved@harborline.example" && c.kind === "Offboarding",
    );
    expect(samChecklist).toBeDefined();
    expect(samChecklist!.steps.some((s) => s.revokesAccess && !s.done)).toBe(true);

    const revoked = await call<RevokeAllResult>(
      `/api/v1/lifecycle/checklists/${samChecklist!.id}/revoke-all`,
      "POST",
    );
    expect(revoked.status).toBe(200);
    expect(revoked.body.revoked).toBeGreaterThan(0);
    expect(revoked.body.checklist.accessRevokedAt).not.toBeNull();
    expect(revoked.body.checklist.steps.filter((s) => s.revokesAccess).every((s) => s.done)).toBe(
      true,
    );
    expect(revoked.body.checklist.steps.some((s) => s.revokesAccess && Boolean(s.evidenceId))).toBe(
      true,
    );

    const savedSettings = await call<{ offboardingAccessSlaHours: number }>(
      "/api/v1/lifecycle/settings",
      "POST",
      { offboardingAccessSlaHours: 8, offboardingCompleteSlaHours: 48, onboardingDays: 10 },
    );
    expect(savedSettings.status).toBe(200);
    expect(savedSettings.body.offboardingAccessSlaHours).toBe(8);

    const badSettings = await call("/api/v1/lifecycle/settings", "POST", {
      offboardingAccessSlaHours: 72,
      offboardingCompleteSlaHours: 24,
      onboardingDays: 10,
    });
    expect(badSettings.status).toBe(400);

    const group = await call<{ code: string }>("/api/v1/lifecycle/groups", "POST", {
      code: "finance",
      name: "Finance",
      departments: ["Finance"],
      roleKeywords: ["analyst"],
      policyCodes: ["POL-ISP"],
      trainingCodes: ["SEC-ANNUAL"],
    });
    expect(group.status).toBe(200);
    expect(group.body.code).toBe("finance");

    const step = await call<{ id: string }>("/api/v1/lifecycle/templates", "POST", {
      kind: "Onboarding",
      code: "finance-controls",
      title: "Review financial controls handbook",
      category: "Policy",
      slaHours: 48,
      groupCode: "finance",
      revokesAccess: false,
      perAccount: false,
    });
    expect(step.status).toBe(200);
    const removed = await call<boolean>(`/api/v1/lifecycle/templates/${step.body.id}`, "DELETE");
    expect(removed.status).toBe(200);
    expect(removed.body).toBe(true);
  });

  it("supports employee self-service onboarding on /api/v1/me/lifecycle while blocking HR steps", async () => {
    await loginAs("dev.patel@harborline.example");
    const mine = await call<PersonLifecycle>("/api/v1/me/lifecycle");
    expect(mine.status).toBe(200);
    const open = mine.body.checklists.find((c) => c.kind === "Onboarding" && c.state === "Open");
    expect(open).toBeDefined();
    const selfStep = open!.steps.find((s) => s.category === "Policy" && !s.done);
    expect(selfStep).toBeDefined();
    const done = await call<LifecycleChecklist>(
      `/api/v1/me/lifecycle/steps/${selfStep!.id}`,
      "POST",
      {
        done: true,
      },
    );
    expect(done.status).toBe(200);
    expect(done.body.steps.find((s) => s.id === selfStep!.id)?.done).toBe(true);

    const hrStep = open!.steps.find((s) => s.category === "Screening");
    expect(hrStep).toBeDefined();
    const denied = await call(`/api/v1/me/lifecycle/steps/${hrStep!.id}`, "POST", { done: false });
    expect(denied.status).toBe(403);
  });

  it("runs vendor risk discovery, profiles, questionnaires, public vendor portal, and SOC 2 sign-off", async () => {
    const inventory = await call<VendorRiskRow[]>("/api/v1/vendor-risk/inventory");
    expect(inventory.status).toBe(200);
    expect(inventory.body.length).toBeGreaterThan(0);
    const vendor = inventory.body[0];

    const updated = await call<VendorRiskRow>(
      `/api/v1/vendor-risk/vendors/${vendor.id}/profile`,
      "POST",
      {
        ownerName: "Priya Nair",
        ownerEmail: "priya.nair@harborline.example",
        contractRenewalOn: "2027-01-15",
        dataCategories: ["CustomerData", "Financial"],
        subprocessor: true,
        reviewCadenceMonths: 6,
        lastReviewedOn: null,
        contactName: "Security Desk",
        contactEmail: "sec@vendor.example",
      },
    );
    expect(updated.status).toBe(200);
    expect(updated.body.subprocessor).toBe(true);
    expect(updated.body.reviewCadenceMonths).toBe(6);

    const scan = await call<VendorDiscoveryScanResult>(
      "/api/v1/vendor-risk/discovery/scan",
      "POST",
    );
    expect(scan.status).toBe(200);
    expect(scan.body.items.length).toBeGreaterThan(0);
    const candidate = scan.body.items.find((i) => i.status === "New");
    expect(candidate).toBeDefined();
    const accepted = await call<VendorDiscovery>(
      `/api/v1/vendor-risk/discovery/${candidate!.id}/accept`,
      "POST",
    );
    expect(accepted.status).toBe(200);
    expect(accepted.body.status).toBe("Accepted");
    expect(accepted.body.vendorId).toBeTruthy();

    // Send privacy-addendum questionnaire (5 questions) and complete it via unauthenticated public portal
    const sent = await call<SentVendorQuestionnaire>(
      `/api/v1/vendor-risk/vendors/${vendor.id}/questionnaires`,
      "POST",
      {
        templateCode: "privacy-addendum",
        contactName: "Alex Vendor",
        contactEmail: "alex@vendor.example",
        expiresInDays: 14,
      },
    );
    expect(sent.status).toBe(200);
    expect(sent.body.portalPath).toMatch(/^\/vendor-portal\//);
    const token = sent.body.portalPath.replace("/vendor-portal/", "");

    await logout();
    const portal = await call<VendorPortalView>(`/api/v1/public/vendor-portal/${token}`);
    expect(portal.status).toBe(200);
    expect(portal.body.template.code).toBe("privacy-addendum");

    const sampleSoc2 = [
      "SOC 2 Type II Report",
      "For the period January 1, 2026 to December 31, 2026",
      "In our opinion, the controls present fairly with an unqualified opinion.",
      "No exceptions noted.",
      "Complementary User Entity Controls:",
      "- Enable MFA for all workspace users",
    ].join("\n");
    const portalUpload = await call<VendorPortalView>(
      `/api/v1/public/vendor-portal/${token}/documents`,
      "POST",
      { kind: "Soc2", fileName: "soc2-report.txt", contentBase64: btoa(sampleSoc2) },
    );
    expect(portalUpload.status).toBe(200);
    expect(portalUpload.body.documents.length).toBe(1);

    const submitted = await call<VendorPortalView>(
      `/api/v1/public/vendor-portal/${token}/answers`,
      "POST",
      {
        answers: portal.body.template.questions.map((q) => ({
          questionCode: q.code,
          answer: "Yes",
          comment: "Confirmed",
        })),
        submit: true,
      },
    );
    expect(submitted.status).toBe(200);
    expect(submitted.body.status).toBe("Submitted");

    // Sign back in as Admin to review questionnaire, sign off SOC 2 document, and sync reminders
    await loginAs("chandra.vell@harborline.example");
    const reviewed = await call<{ status: string }>(
      `/api/v1/vendor-risk/assessments/${sent.body.assessment.id}/review`,
      "POST",
      { decision: "Approve", note: "Looks solid" },
    );
    expect(reviewed.status).toBe(200);
    expect(reviewed.body.status).toBe("Reviewed");

    const detail = await call<VendorRiskDetail>(`/api/v1/vendor-risk/vendors/${vendor.id}`);
    expect(detail.status).toBe(200);
    const doc = detail.body.documents[0];
    expect(doc).toBeDefined();
    expect(doc.opinion).toBe("Unqualified");
    expect(doc.cuecs.length).toBe(1);

    const signedOff = await call<VendorReviewDocument>(
      `/api/v1/vendor-risk/documents/${doc.id}/sign-off`,
      "POST",
      { note: "Reviewed CUECs" },
    );
    expect(signedOff.status).toBe(200);
    expect(signedOff.body.signedOffBy).toBeTruthy();

    const reminders = await call<VendorReminderSyncResult>(
      "/api/v1/vendor-risk/reminders/sync",
      "POST",
    );
    expect(reminders.status).toBe(200);
    expect(reminders.body.reminders.length).toBeGreaterThanOrEqual(0);
  });

  it("blocks auditor access to vendor risk and lifecycle writes", async () => {
    await loginAs("auditor@northgate.example");
    expect((await call<VendorRiskRow[]>("/api/v1/vendor-risk/inventory")).body).toEqual([]);
    expect((await call("/api/v1/lifecycle/sync", "POST")).status).toBe(403);
    expect((await call("/api/v1/vendor-risk/discovery/scan", "POST")).status).toBe(403);
    expect((await call("/api/v1/me/lifecycle")).status).toBe(204);
  });
});
