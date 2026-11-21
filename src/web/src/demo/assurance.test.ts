import { beforeEach, describe, expect, it } from "vitest";
import { examplePassword } from "./accounts";
import { demoResponse, resetDemo } from "./backend";

async function loginAs(email: string) {
  const res = await demoResponse("/api/v1/auth/login", {
    method: "POST",
    body: JSON.stringify({ email, password: examplePassword }),
  });
  expect(res.status).toBe(200);
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

const versioning = {
  resourceKind: "bucket",
  provider: "aws",
  filter: { all: [{ field: "name", op: "contains", value: "-prod" }] },
  assertion: { all: [{ field: "attributes.versioning", op: "equals", value: true }] },
};

describe("M11 custom tests, evidence library, control readiness and policy upgrades demo", () => {
  beforeEach(async () => {
    await resetDemo();
    await loginAs("chandra.vell@harborline.example");
  });

  it("runs the seeded versioning test and lists it with built-in tests", async () => {
    const custom =
      await call<{ key: string; outcome: string; detail: string }[]>("/api/v1/tests/custom");
    expect(custom.body[0].outcome).toBe("fail");
    expect(custom.body[0].detail).toContain("stages-prod");
    const all =
      await call<{ key: string; controlCode: string; outcome: string }[]>("/api/v1/tests");
    const rows = all.body.filter((item) => item.key === custom.body[0].key);
    expect(rows.map((item) => item.controlCode).sort()).toEqual(["BC-01", "CR-02"]);
    const control = await call<{ status: string }>("/api/v1/controls/CR-02");
    expect(control.body.status).toBe("failing");
  });

  it("previews conditions and rejects malicious or invalid trees", async () => {
    const preview = await call<{ outcome: string; total: number; failing: { name: string }[] }>(
      "/api/v1/tests/custom/preview",
      "POST",
      versioning,
    );
    expect(preview.status).toBe(200);
    expect(preview.body.failing.map((item) => item.name)).toEqual(["stages-prod"]);
    const bad = [
      { field: "constructor.prototype", op: "equals", value: "x" },
      { field: "name", op: "eval", value: "x" },
      { field: "name", op: "equals", value: "x", script: "alert(1)" },
      { field: "name", op: "gt", value: "abc" },
      { all: [] },
      {
        any: [
          { any: [{ any: [{ any: [{ any: [{ any: [{ field: "name", op: "exists" }] }] }] }] }] },
        ],
      },
    ];
    for (const assertion of bad) {
      const res = await call<{ code: string }>("/api/v1/tests/custom/preview", "POST", {
        ...versioning,
        assertion,
      });
      expect(res.status).toBe(400);
      expect(res.body.code).toMatch(/^condition_/);
    }
  });

  it("creates a custom test, and snoozes it only with a reason and expiry", async () => {
    const created = await call<{ key: string; outcome: string }>("/api/v1/tests/custom", "POST", {
      ...versioning,
      name: "All prod buckets versioned",
      severity: "Medium",
      controlCodes: ["CR-02"],
      remediation: "Enable versioning.",
    });
    expect(created.status).toBe(200);
    expect(created.body.key).toBe("custom-all-prod-buckets-versioned");
    const missing = await call(`/api/v1/tests/${created.body.key}/state`, "PUT", {
      state: "Snoozed",
      reason: "",
    });
    expect(missing.status).toBe(400);
    const noUntil = await call(`/api/v1/tests/${created.body.key}/state`, "PUT", {
      state: "Snoozed",
      reason: "Vendor fix pending",
    });
    expect(noUntil.status).toBe(400);
    const snoozed = await call<{ active: boolean }>(
      `/api/v1/tests/${created.body.key}/state`,
      "PUT",
      {
        state: "Snoozed",
        reason: "Vendor fix pending",
        until: dateIn(14),
      },
    );
    expect(snoozed.body.active).toBe(true);
    const tests = await call<{ key: string; outcome: string; detail: string }[]>("/api/v1/tests");
    const row = tests.body.find((item) => item.key === created.body.key);
    expect(row?.outcome).toBe("notRun");
    expect(row?.detail).toContain("Snoozed until");
    await call(`/api/v1/tests/${created.body.key}/state`, "PUT", { state: "Enabled" });
    const after = await call<{ key: string; outcome: string }[]>("/api/v1/tests");
    expect(after.body.find((item) => item.key === created.body.key)?.outcome).toBe("fail");
    const activity = await call<{ action: string }[]>("/api/v1/activity");
    if (activity.status === 200 && Array.isArray(activity.body)) {
      expect(activity.body.some((item) => item.action === "test.snoozed")).toBe(true);
    }
  });

  it("reports library expiry, fulfils requests by upload and bulk links", async () => {
    const page = await call<{
      summary: { expired: number; expiringSoon: number; missing: number; openRequests: number };
      documents: { code: string; status: string }[];
      myRequests: { documentCode: string }[];
    }>("/api/v1/evidence/library");
    expect(page.body.summary).toMatchObject({
      expired: 1,
      expiringSoon: 1,
      missing: 1,
      openRequests: 1,
    });
    expect(page.body.myRequests[0].documentCode).toBe("EVD-004");
    const cm14 = await call<{ status: string }>("/api/v1/controls/CM-14");
    expect(cm14.body.status).toBe("needsEvidence");

    const uploaded = await call<{ status: string; requests: { status: string }[] }>(
      "/api/v1/evidence/library/EVD-001/upload",
      "POST",
      { fileName: "pentest-2025.pdf", contentBase64: btoa("report"), validFrom: dateIn(0) },
    );
    expect(uploaded.body.status).toBe("current");
    expect((await call<{ status: string }>("/api/v1/controls/CM-14")).body.status).toBe("passing");

    const linked = await call<number>("/api/v1/evidence/library/links", "POST", {
      documentCodes: ["EVD-002", "EVD-003"],
      controlCodes: ["GV-01", "AC-04"],
    });
    expect(linked.body).toBe(3);
    const renewals = await call<number>("/api/v1/evidence/library/renewals", "POST");
    expect(renewals.body).toBeGreaterThanOrEqual(2);
  });

  it("lets an employee upload only against their own request", async () => {
    const requested = await call("/api/v1/evidence/library/EVD-003/requests", "POST", {
      assigneeName: "Dev Patel",
      assigneeEmail: "dev.patel@harborline.example",
      dueOn: dateIn(7),
      note: "Upload the new BCP summary.",
    });
    expect(requested.status).toBe(200);
    await loginAs("dev.patel@harborline.example");
    const denied = await call("/api/v1/evidence/library/EVD-002/upload", "POST", {
      fileName: "x.pdf",
      contentBase64: btoa("x"),
    });
    expect(denied.status).toBe(403);
    const ok = await call<{ status: string }>("/api/v1/evidence/library/EVD-003/upload", "POST", {
      fileName: "bcp.pdf",
      contentBase64: btoa("bcp"),
    });
    expect(ok.status).toBe(200);
  });

  it("explains readiness and keeps comment edit history", async () => {
    const ready = await call<{ ready: boolean; checks: { key: string; met: boolean }[] }>(
      "/api/v1/controls/CM-14/readiness",
    );
    expect(ready.body.ready).toBe(false);
    expect(ready.body.checks.find((item) => item.key === "library")?.met).toBe(false);
    const comment = await call<{ id: string }>("/api/v1/controls/CM-14/comments", "POST", {
      body: "First",
    });
    const edited = await call<{ body: string; history: { body: string }[] }>(
      `/api/v1/controls/CM-14/comments/${comment.body.id}`,
      "PUT",
      { body: "Second" },
    );
    expect(edited.body.body).toBe("Second");
    expect(edited.body.history[0].body).toBe("First");
    const profile = await call<{ profile: { requiredEvidenceCount: number } }>(
      "/api/v1/controls/GV-01/profile",
      "PUT",
      {
        implementationGuidance: "Board reviews quarterly.",
        evidenceRequirement: "Minutes",
        requiredEvidenceCount: 2,
        requiresPassingTest: true,
      },
    );
    expect(profile.body.profile.requiredEvidenceCount).toBe(2);
  });

  it("diffs policy versions and runs attestation campaigns", async () => {
    const diff = await call<{ added: number; removed: number; lines: { kind: string }[] }>(
      "/api/v1/policies/POL-ACC/diff?from=1&to=2",
    );
    expect(diff.status).toBe(200);
    expect(diff.body.added).toBeGreaterThan(0);
    const started = await call<{ id: string; total: number; status: string }>(
      "/api/v1/policies/POL-ISP/attestations",
      "POST",
      { group: "Engineering", dueOn: dateIn(21) },
    );
    expect(started.status).toBe(200);
    expect(started.body.total).toBeGreaterThan(0);
    const empty = await call<{ code: string }>("/api/v1/policies/POL-ISP/attestations", "POST", {
      group: "Nobody",
      dueOn: dateIn(21),
    });
    expect(empty.body.code).toBe("empty_group");
    const attested = await call<{ attested: number }>("/api/v1/policies/POL-ISP/attest", "POST");
    expect(attested.status).toBe(200);
    const renewed = await call<{ id: string }>(
      `/api/v1/policies/POL-ISP/attestations/${started.body.id}/renew`,
      "POST",
    );
    expect(renewed.status).toBe(200);
    const again = await call<{ code: string }>(
      `/api/v1/policies/POL-ISP/attestations/${started.body.id}/renew`,
      "POST",
    );
    expect(again.body.code).toBe("already_renewed");
    const installed = await call<number>("/api/v1/policies/library", "POST");
    expect(installed.body).toBe(17);
  });

  it("blocks auditor writes", async () => {
    await loginAs("auditor@northgate.example");
    expect((await call("/api/v1/tests/custom/preview", "POST", versioning)).status).toBe(403);
    expect((await call("/api/v1/evidence/library/renewals", "POST")).status).toBe(403);
    expect((await call("/api/v1/controls/CR-02/comments", "POST", { body: "x" })).status).toBe(403);
    expect((await call("/api/v1/evidence/library")).status).toBe(200);
  });
});
