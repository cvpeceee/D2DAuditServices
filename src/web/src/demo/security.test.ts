import { beforeEach, describe, expect, it } from "vitest";
import { demoResponse, resetDemo } from "./backend";
import { examplePassword } from "./accounts";

async function signInAdmin() {
  const response = await demoResponse("/api/v1/auth/login", {
    method: "POST",
    body: JSON.stringify({ email: "chandra.vell@harborline.example", password: examplePassword }),
  });
  expect(response.status).toBe(200);
}

describe("M8 Cloud Security Intelligence demo routes", () => {
  beforeEach(async () => {
    await resetDemo();
    await signInAdmin();
  });

  it("serves public exposure board, resolves a finding, and updates open count", async () => {
    const initialRes = await demoResponse("/api/v1/security/exposure");
    expect(initialRes.status).toBe(200);
    const initial = (await initialRes.json()) as {
      openCount: number;
      findings: { id: string; status: string; remediationTerraform: string }[];
    };
    expect(initial.openCount).toBeGreaterThanOrEqual(4);
    const open = initial.findings.find((item) => item.status === "Open");
    expect(open?.remediationTerraform).toContain("resource ");

    const resolveRes = await demoResponse(`/api/v1/security/exposure/${open!.id}/resolve`, {
      method: "POST",
      body: JSON.stringify({ note: "Restricted security group via Terraform" }),
    });
    expect(resolveRes.status).toBe(200);

    const after = (await (await demoResponse("/api/v1/security/exposure")).json()) as {
      openCount: number;
    };
    expect(after.openCount).toBe(initial.openCount - 1);
  });

  it("ingests SARIF findings and redacts secret tokens", async () => {
    const sarif = JSON.stringify({
      version: "2.1.0",
      runs: [
        {
          tool: { driver: { name: "Gitleaks" } },
          results: [
            {
              ruleId: "GITLEAKS-SECRET-KEY",
              level: "error",
              message: { text: "Secret credential found in Dockerfile" },
              locations: [
                {
                  physicalLocation: {
                    artifactLocation: { uri: "Dockerfile" },
                    region: { startLine: 9, snippet: { text: "API_TOKEN=abcd1234efgh5678ijkl" } },
                  },
                },
              ],
            },
          ],
        },
      ],
    });
    const ingestRes = await demoResponse("/api/v1/security/scans/sarif", {
      method: "POST",
      body: JSON.stringify({ assetName: "ecr/demo:1.0", sarifJson: sarif }),
    });
    expect(ingestRes.status).toBe(200);
    expect(await ingestRes.json()).toBe(1);

    const board = (await (await demoResponse("/api/v1/security/scans")).json()) as {
      secrets: { location: string; snippet: string }[];
    };
    const imported = board.secrets.find((item) => item.location === "Dockerfile");
    expect(imported).toBeDefined();
    expect(imported?.snippet).toContain("[REDACTED]");
    expect(imported?.snippet).not.toContain("abcd1234efgh5678ijkl");
  });

  it("serves DSPM, FinOps, and correlated Attack Paths", async () => {
    const dspm = (await (await demoResponse("/api/v1/security/dspm")).json()) as {
      totalStores: number;
      publicSensitiveStores: number;
    };
    expect(dspm.totalStores).toBeGreaterThanOrEqual(5);
    expect(dspm.publicSensitiveStores).toBeGreaterThanOrEqual(1);

    const finops = (await (await demoResponse("/api/v1/security/finops")).json()) as {
      summary: { openMonthlyUsd: number; complianceLinkedCount: number };
      recommendations: { id: string; status: string }[];
    };
    expect(finops.summary.openMonthlyUsd).toBeGreaterThan(1000);
    expect(finops.summary.complianceLinkedCount).toBeGreaterThanOrEqual(4);

    const openCost = finops.recommendations.find((item) => item.status === "Open");
    const applyRes = await demoResponse(`/api/v1/security/finops/${openCost!.id}/status`, {
      method: "POST",
      body: JSON.stringify({ status: "Applied" }),
    });
    expect(applyRes.status).toBe(200);

    const paths = (await (await demoResponse("/api/v1/security/attack-paths")).json()) as {
      totalPaths: number;
      highestBlastRadius: number;
      paths: { hops: { stage: string }[] }[];
    };
    expect(paths.totalPaths).toBeGreaterThanOrEqual(3);
    expect(paths.highestBlastRadius).toBeGreaterThanOrEqual(80);
    expect(paths.paths[0].hops.map((hop) => hop.stage)).toContain("Crown Jewel");
  });
});
