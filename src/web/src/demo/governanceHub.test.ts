import { beforeEach, describe, expect, it } from "vitest";
import { demoResponse, resetDemo } from "./backend";
import { examplePassword } from "./accounts";

async function loginAs(email: string) {
  const res = await demoResponse("/api/v1/auth/login", {
    method: "POST",
    body: JSON.stringify({ email, password: examplePassword }),
  });
  expect(res.status).toBe(200);
}

describe("M10 Enterprise Governance Hub & Differentiators demo parity", () => {
  beforeEach(async () => {
    await resetDemo();
    await loginAs("chandra.vell@harborline.example");
  });

  it("returns consolidated multi-entity rollup and saves a new legal entity", async () => {
    const getRes = await demoResponse("/api/v1/governance/entities");
    expect(getRes.status).toBe(200);
    const before = (await getRes.json()) as {
      summary: { entityCount: number; totalHeadcount: number };
      entities: { code: string }[];
    };
    expect(before.summary.entityCount).toBe(3);
    expect(before.summary.totalHeadcount).toBeGreaterThan(1000);

    const postRes = await demoResponse("/api/v1/governance/entities", {
      method: "POST",
      body: JSON.stringify({
        code: "ENT-UK",
        name: "Harborline UK Ltd.",
        jurisdiction: "United Kingdom",
        region: "eu-west-2",
        headcount: 75,
        controlsPassing: 42,
        controlsTotal: 45,
        primaryFramework: "ISO 27001 + UK GDPR",
        status: "Active",
      }),
    });
    expect(postRes.status).toBe(200);
    const afterRes = await demoResponse("/api/v1/governance/entities");
    const after = (await afterRes.json()) as { summary: { entityCount: number } };
    expect(after.summary.entityCount).toBe(4);
  });

  it("syncs Checkr, KnowBe4, and executes SCIM 2.0 deprovisioning with leaver access detection", async () => {
    const checkrRes = await demoResponse("/api/v1/governance/checkr/sync", { method: "POST" });
    expect(checkrRes.status).toBe(200);
    const checkr = (await checkrRes.json()) as { provider: string; summary: string };
    expect(checkr.provider).toBe("Checkr");

    const kb4Res = await demoResponse("/api/v1/governance/knowbe4/sync", { method: "POST" });
    expect(kb4Res.status).toBe(200);

    const scimRes = await demoResponse(
      "/api/v1/scim/v2/Users/dev.patel%40harborline.example/deprovision",
      {
        method: "POST",
        body: JSON.stringify({
          email: "dev.patel@harborline.example",
          reason: "SCIM exit",
          revokeDownstreamAccounts: false,
        }),
      },
    );
    expect(scimRes.status).toBe(200);
    const scim = (await scimRes.json()) as {
      state: string;
      leaverAccessFlagged: boolean;
      offboardedCheckOutcome: string;
    };
    expect(scim.state).toBe("Offboarded");
    expect(scim.leaverAccessFlagged).toBe(true);
    expect(scim.offboardedCheckOutcome).toBe("Fail");
  });

  it("generates a 1-click Remediation PR signed into EvidenceChain and compares Audit Time Machine dates", async () => {
    const prRes = await demoResponse("/api/v1/governance/remediation-prs", {
      method: "POST",
      body: JSON.stringify({
        controlCode: "CFG-01",
        targetResource: "harborline-staging-exports",
        repository: "harborline-data/infra-terraform",
        artifactType: "Terraform",
      }),
    });
    expect(prRes.status).toBe(200);
    const pr = (await prRes.json()) as {
      prNumber: number;
      branchName: string;
      unifiedDiff: string;
      evidenceSha256: string;
    };
    expect(pr.prNumber).toBeGreaterThanOrEqual(420);
    expect(pr.branchName).toContain("d2d/fix-cfg-01");
    expect(pr.unifiedDiff).toContain("aws_vpc_security_group_ingress_rule");
    expect(pr.evidenceSha256).toHaveLength(64);

    const tmRes = await demoResponse(
      "/api/v1/governance/time-machine?asOf=2026-08-01&compareTo=2026-11-20",
    );
    expect(tmRes.status).toBe(200);
    const tm = (await tmRes.json()) as { summary: string; asOf: { date: string } };
    expect(tm.asOf.date).toBe("2026-08-01");
    expect(tm.summary).toContain("EvidenceChain");
  });

  it("blocks external Auditor from internal Governance Hub routes", async () => {
    await loginAs("auditor@northgate.example");
    const res = await demoResponse("/api/v1/governance/overview");
    expect(res.status).toBe(403);
  });
});
