import { describe, expect, it } from "vitest";
import { examplePassword } from "./accounts";
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
    const admin = await login("chandra@acme.example");
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
});
