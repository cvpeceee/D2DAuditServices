import { describe, expect, it } from "vitest";
import { examplePassword } from "./accounts";
import { company } from "./client";
import { demoResponse, resetDemo, setDemoStepDelay } from "./backend";

async function login(email: string) {
  const response = await demoResponse("/api/v1/auth/login", {
    method: "POST",
    body: JSON.stringify({ email, password: examplePassword }),
  });
  expect(response.status).toBe(200);
  return response.json() as Promise<{ tenants: { role: string }[] }>;
}

async function get(path: string) {
  const response = await demoResponse(path, { headers: { "X-Tenant-Id": "acme" } });
  expect(response.ok).toBe(true);
  return response.json();
}

describe("demo backend", () => {
  it("signs in the example accounts and keeps the auditor inside SOC 2", async () => {
    await resetDemo();
    const admin = await login("chandra.vell@harborline.example");
    expect(admin.tenants[0].role).toBe("Admin");
    const policies = (await get("/api/v1/policies")) as { code: string }[];
    expect(policies.map((item) => item.code)).toContain("POL-PRI");
    const checks = (await get("/api/v1/tests")) as {
      name: string;
      outcome: string;
      detail: string;
    }[];
    expect(checks.find((item) => item.name === "S3 public access blocked")?.detail).toContain(
      "marketing-site",
    );
    expect(checks.find((item) => item.name === "MFA enforced for all users")?.outcome).toBe("fail");
    expect(
      checks.find((item) => item.name === "Branch protection on the default branch")?.detail,
    ).toContain("experiments");
    expect(checks.find((item) => item.name === "Offboarded users removed")?.detail).toContain(
      "Sam Leaved",
    );

    await demoResponse("/api/v1/auth/logout", { method: "POST" });
    const auditor = await login("auditor@northgate.example");
    expect(auditor.tenants[0].role).toBe("Auditor");
    const visible = (await get("/api/v1/policies")) as { code: string }[];
    expect(visible.map((item) => item.code)).not.toContain("POL-PRI");
    const hidden = await demoResponse("/api/v1/policies/POL-PRI");
    expect(hidden.status).toBe(404);
    const approve = await demoResponse("/api/v1/policies/POL-ACC/approve", {
      method: "POST",
      body: "{}",
    });
    expect(approve.status).toBe(403);
    const frameworks = (await get("/api/v1/frameworks")) as { key: string }[];
    expect(frameworks.map((item) => item.key)).toEqual(["soc2"]);
  });

  it("builds a package whose manifest matches the evidence hashes", async () => {
    await resetDemo();
    setDemoStepDelay(0);
    await login("auditor@northgate.example");
    const started = await demoResponse("/api/v1/audits/package", { method: "POST", body: "{}" });
    expect(started.status).toBe(200);
    const jobId = (await started.json()) as string;
    let status = "";
    for (let attempt = 0; attempt < 30 && status !== "Succeeded"; attempt += 1) {
      await new Promise((resolve) => setTimeout(resolve, 0));
      const jobs = (await get("/api/v1/jobs")) as {
        id: string;
        status: string;
        currentStep: string;
      }[];
      status = jobs.find((job) => job.id === jobId)?.status ?? "";
    }
    expect(status).toBe("Succeeded");
    const file = await demoResponse(`/api/v1/audits/packages/${jobId}`);
    expect(file.status).toBe(200);
    const bytes = new Uint8Array(await file.arrayBuffer());
    expect(bytes[0]).toBe(0x50);
    expect(bytes[1]).toBe(0x4b);
    const text = new TextDecoder().decode(bytes);
    expect(text).toContain("manifest.json");
    expect(text).toContain("marketing-site");
    expect(text).not.toContain("PP-01");
    const hash = text.match(/"sha256": "([a-f0-9]{64})"/);
    const recorded = text.match(/"recordedSha256": "([a-f0-9]{64})"/);
    expect(hash?.[1]).toBe(recorded?.[1]);
    expect(text).toContain('"intact": true');
  });

  it("serves the statement of applicability, leaver flag, and a hashed access review", async () => {
    await resetDemo();
    await login(`chandra.vell@${company.domain}`);
    const soa = (await get("/api/v1/soa")) as { code: string; applicable: boolean }[];
    expect(soa).toHaveLength(93);
    expect(soa[0]?.code).toBe("A.5.1");
    expect(soa.at(-1)?.code).toBe("A.8.34");
    expect(soa.find((row) => row.code === "A.7.5")?.applicable).toBe(false);
    const pdf = await demoResponse("/api/v1/soa/export?format=pdf");
    expect(pdf.headers.get("Content-Type")).toContain("application/pdf");
    const pdfText = new TextDecoder().decode(await pdf.arrayBuffer());
    expect(pdfText.startsWith("%PDF-")).toBe(true);
    expect(pdfText).toContain("A.8.34");

    const people = (await get(`/api/v1/people?q=sam.leaved@${company.domain}`)) as {
      items: { name: string; email: string; leaverStillHasAccess: boolean }[];
      total: number;
    };
    const directory = (await get("/api/v1/people?pageSize=1")) as {
      total: number;
      items: unknown[];
    };
    expect(directory.total).toBeGreaterThanOrEqual(1000);
    expect(directory.items).toHaveLength(1);
    const sam = people.items.find((person) => person.email === `sam.leaved@${company.domain}`);
    expect(sam?.leaverStillHasAccess).toBe(true);

    const campaigns = (await get("/api/v1/access-reviews")) as { id: string; name: string }[];
    const campaign = campaigns.find((item) => item.name === "Q4 access review");
    expect(campaign).toBeTruthy();
    const open = (await get(`/api/v1/access-reviews/${campaign?.id}`)) as {
      items: { id: string }[];
    };
    for (const item of open.items) {
      const decided = await demoResponse(
        `/api/v1/access-reviews/${campaign?.id}/items/${item.id}`,
        { method: "POST", body: JSON.stringify({ decision: "Approved" }) },
      );
      expect(decided.status).toBe(200);
    }
    const completed = await demoResponse(`/api/v1/access-reviews/${campaign?.id}/complete`, {
      method: "POST",
      body: "{}",
    });
    expect(completed.status).toBe(200);
    const review = (await completed.json()) as { evidenceSha256: string; state: string };
    expect(review.state).toBe("Completed");
    const evidence = (await get("/api/v1/evidence")) as {
      items: { title: string; sha256: string }[];
    };
    const stored = evidence.items.find((item) => item.title === "Access review Q4 access review");
    expect(stored?.sha256).toBe(review.evidenceSha256);
    expect(review.evidenceSha256).toMatch(/^[a-f0-9]{64}$/);

    const dashboard = (await get("/api/v1/dashboard")) as {
      tenantName: string;
      dueItems: { title: string; overdue: boolean }[];
      frameworks: { key: string; percent: number }[];
      history: { weekOf: string }[];
      devicesReporting: number;
      readinessPercent: number;
    };
    expect(dashboard.tenantName).toBe(company.name);
    expect(dashboard.history.length).toBeGreaterThanOrEqual(26);
    expect(dashboard.devicesReporting).toBeGreaterThan(0);
    expect(dashboard.readinessPercent).toBeGreaterThanOrEqual(70);
    expect(dashboard.readinessPercent).toBeLessThanOrEqual(80);
    expect(
      dashboard.dueItems.some((item) => item.title === "Leaver still has access" && item.overdue),
    ).toBe(true);
    expect(dashboard.frameworks.map((item) => item.key)).toEqual(
      expect.arrayContaining(["soc2", "iso27001", "iso27017", "iso27018"]),
    );
    const resources = (await get("/api/v1/resources")) as { provider: string; name: string }[];
    expect(resources.some((item) => item.provider === "azure")).toBe(true);
    expect(resources.some((item) => item.provider === "gcp")).toBe(true);
    expect(resources.some((item) => item.name === "marketing-site")).toBe(true);

    await demoResponse("/api/v1/auth/logout", { method: "POST" });
    await login("auditor@northgate.example");
    const visibleSoa = await demoResponse("/api/v1/soa");
    expect(visibleSoa.status).toBe(200);
    const denied = await demoResponse("/api/v1/soa/A.5.1", {
      method: "PUT",
      body: JSON.stringify({
        applicable: true,
        justification: "Applies.",
        implementation: "Implemented",
      }),
    });
    expect(denied.status).toBe(403);
  }, 60_000);

  it("serves the employee portal, training, assets, tasks, and settings", async () => {
    await resetDemo();
    await login("chandra.vell@harborline.example");
    const cards = (await get("/api/v1/me/cards")) as { kind: string; status: string }[];
    expect(cards.map((card) => card.kind)).toEqual([
      "Policy",
      "Training",
      "Mfa",
      "Device",
      "BackgroundCheck",
    ]);
    expect(cards.every((card) => card.status === "Complete")).toBe(true);

    const modules = (await get("/api/v1/training")) as { id: string; code: string; type: string }[];
    expect(modules.map((item) => item.code)).toEqual(["SEC-ANNUAL", "SEC-LMS"]);
    await demoResponse("/api/v1/auth/logout", { method: "POST" });
    await login("dev.patel@harborline.example");
    const quiz = await demoResponse("/api/v1/training/module-annual/quiz", {
      method: "POST",
      headers: { "X-Tenant-Id": "acme" },
      body: JSON.stringify({ answers: [0, 0, 0] }),
    });
    expect(quiz.status).toBe(200);
    const passed = (await quiz.json()) as { passed: boolean; evidenceSha256: string | null };
    expect(passed.passed).toBe(true);
    expect(passed.evidenceSha256).toMatch(/^[a-f0-9]{64}$/);
    await demoResponse("/api/v1/auth/logout", { method: "POST" });
    await login("chandra.vell@harborline.example");

    const trainingControl = (await get("/api/v1/controls/HR-06")) as {
      status: string;
      tests: { name: string; outcome: string }[];
    };
    expect(trainingControl.status).toBe("failing");
    expect(
      trainingControl.tests.find(
        (item) => item.name === "Personnel completed training within 12 months",
      )?.outcome,
    ).toBe("fail");

    const assets = (await get("/api/v1/assets?page=1&pageSize=40")) as {
      items: { type: string; name: string }[];
      total: number;
      pageSize: number;
    };
    expect(assets.pageSize).toBe(40);
    expect(assets.total).toBeGreaterThan(assets.pageSize);
    expect(assets.items.length).toBeLessThanOrEqual(40);
    const exported = await demoResponse("/api/v1/assets/export", {
      headers: { "X-Tenant-Id": "acme" },
    });
    expect(exported.status).toBe(200);
    const csv = await exported.text();
    expect(csv).toContain("marketing-site");
    expect(csv).toContain("Laptop");

    const tasks = (await get("/api/v1/tasks?mine=true")) as { title: string; overdue: boolean }[];
    expect(tasks.some((task) => task.title === "Prepare the next access review")).toBe(true);
    const allTasks = (await get("/api/v1/tasks")) as { overdue: boolean }[];
    expect(allTasks.some((task) => task.overdue)).toBe(true);

    const members = (await get("/api/v1/settings/members")) as {
      email: string;
      active: boolean;
      role: string;
    }[];
    expect(
      members.some(
        (member) => member.email === "chandra.vell@harborline.example" && member.role === "Admin",
      ),
    ).toBe(true);
    expect(members.some((member) => member.active === false)).toBe(true);
    const sso = (await get("/api/v1/settings/sso")) as { connected: boolean };
    expect(sso.connected).toBe(false);

    const risks = (await get("/api/v1/risks")) as { code: string; approved: boolean }[];
    expect(risks.find((risk) => risk.code === "R-03")?.approved).toBe(false);
    expect(risks.some((risk) => risk.approved)).toBe(true);
    const library = (await get("/api/v1/risks/library")) as { code: string; added: boolean }[];
    expect(library.find((item) => item.code === "LIB-01")?.added).toBe(false);

    const vendors = (await get("/api/v1/vendors")) as {
      name: string;
      decision: string;
      questions: { prompt: string }[];
    }[];
    const vendor = vendors.find((item) => item.questions.length > 0);
    expect(vendor?.decision).toBeTruthy();

    const campaigns = (await get("/api/v1/access-reviews")) as {
      id: string;
      name: string;
      state: string;
    }[];
    const draft = campaigns.find((item) => item.name === "Q1 contractor review");
    expect(draft?.state).toBe("Draft");
  });
});
