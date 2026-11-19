import { beforeEach, describe, expect, it } from "vitest";
import { demoResponse, resetDemo } from "./backend";
import { examplePassword } from "./accounts";

async function signIn(email: string) {
  const response = await demoResponse("/api/v1/auth/login", {
    method: "POST",
    body: JSON.stringify({ email, password: examplePassword }),
  });
  expect(response.status).toBe(200);
}

describe("M9 Enterprise Super-App demo routes", () => {
  beforeEach(async () => {
    await resetDemo();
  });

  it("loads all 13 frameworks, runs What-If simulator, adopts gaps, and uploads a custom framework", async () => {
    await signIn("chandra.vell@harborline.example");
    const frameworks = (await (await demoResponse("/api/v1/frameworks")).json()) as {
      key: string;
    }[];
    expect(frameworks.length).toBeGreaterThanOrEqual(13);
    expect(frameworks.map((f) => f.key)).toEqual(
      expect.arrayContaining([
        "pci-dss",
        "nist-csf",
        "dora",
        "iso42001",
        "nist-ai-rmf",
        "eu-ai-act",
        "iec62443",
      ]),
    );

    const sim = (await (await demoResponse("/api/v1/frameworks/iso42001/simulator")).json()) as {
      overlapPercent: number;
      gaps: { code: string }[];
    };
    expect(sim.overlapPercent).toBeGreaterThan(0);
    expect(sim.gaps.length).toBeGreaterThan(0);

    const adoptRes = await demoResponse("/api/v1/frameworks/iso42001/simulator/adopt", {
      method: "POST",
      body: JSON.stringify({}),
    });
    expect(adoptRes.status).toBe(200);
    expect(await adoptRes.json()).toBe(sim.gaps.length);

    const after = (await (await demoResponse("/api/v1/frameworks/iso42001/simulator")).json()) as {
      gaps: { code: string }[];
    };
    expect(after.gaps.length).toBe(0);

    const customRes = await demoResponse("/api/v1/frameworks/custom", {
      method: "POST",
      body: JSON.stringify({
        key: "internal-zero-trust",
        name: "Internal Zero Trust",
        version: "1.0",
        description: "Baseline clauses.",
        requirementsCsv:
          "code,title,summary\nZT-1,Marshall MFA,Marshall handlers shall verify FIDO2 on every admin session.",
      }),
    });
    expect(customRes.status).toBe(200);
    expect(((await customRes.json()) as { requirementCount: number }).requirementCount).toBe(1);
    const customFw = (await (
      await demoResponse("/api/v1/frameworks/internal-zero-trust")
    ).json()) as {
      requirements: { summary: string }[];
    };
    expect(customFw.requirements[0].summary).toContain("Marshall handlers must");
    expect(customFw.requirements[0].summary).not.toContain("Marmust");
  });

  it("issues verifiable cryptographic trust receipts, gates NDA downloads, and answers public Trust Q&A", async () => {
    const receiptRes = await demoResponse("/api/v1/public/trust/harborline/receipt");
    expect(receiptRes.status).toBe(200);
    const receipt = (await receiptRes.json()) as {
      chainStatus: string;
      canonicalPayload: string;
      receiptSha256: string;
    };
    expect(receipt.chainStatus).toBe("Verified");
    expect(receipt.receiptSha256).toHaveLength(64);
    const digest = await crypto.subtle.digest(
      "SHA-256",
      new TextEncoder().encode(receipt.canonicalPayload),
    );
    const computed = [...new Uint8Array(digest)]
      .map((b) => b.toString(16).padStart(2, "0"))
      .join("");
    expect(computed).toBe(receipt.receiptSha256);

    const pubPage = (await (await demoResponse("/api/v1/public/trust/harborline")).json()) as {
      documents: { id: string; access: string }[];
    };
    const ndaDoc = pubPage.documents.find((d) => d.access === "Nda")!;
    const pubDoc = pubPage.documents.find((d) => d.access === "Public")!;

    expect(
      (await demoResponse(`/api/v1/public/trust/harborline/documents/${pubDoc.id}/download`))
        .status,
    ).toBe(200);
    expect(
      (await demoResponse(`/api/v1/public/trust/harborline/documents/${ndaDoc.id}/download`))
        .status,
    ).toBe(403);

    const askRes = await demoResponse("/api/v1/public/trust/harborline/ask", {
      method: "POST",
      body: JSON.stringify({ question: "Are you SOC 2 and ISO 27001 compliant?" }),
    });
    expect(askRes.status).toBe(200);
    const reply = (await askRes.json()) as { answer: string; citations: { code: string }[] };
    expect(reply.citations.length).toBeGreaterThan(0);
  });

  it("scopes workspace assistant by role and manages admin API keys and webhooks", async () => {
    await signIn("auditor@northgate.example");
    const audReply = (await (
      await demoResponse("/api/v1/assistant/ask", {
        method: "POST",
        body: JSON.stringify({ question: "Show FinOps spend and attack paths" }),
      })
    ).json()) as { answer: string };
    expect(audReply.answer).toContain("auditor scope");
    expect(audReply.answer).not.toContain("$2,075");

    await signIn("chandra.vell@harborline.example");
    const adminReply = (await (
      await demoResponse("/api/v1/assistant/ask", {
        method: "POST",
        body: JSON.stringify({ question: "Show FinOps spend and attack paths" }),
      })
    ).json()) as { citations: { kind: string }[] };
    expect(adminReply.citations.some((c) => c.kind === "AttackPath" || c.kind === "FinOps")).toBe(
      true,
    );

    const keyCreated = (await (
      await demoResponse("/api/v1/developer/keys", {
        method: "POST",
        body: JSON.stringify({ name: "Audit CI", scopes: "read:controls" }),
      })
    ).json()) as { plaintextKey: string; key: { id: string } };
    expect(keyCreated.plaintextKey.startsWith("d2d_api_")).toBe(true);
    expect(
      (await demoResponse(`/api/v1/developer/keys/${keyCreated.key.id}`, { method: "DELETE" }))
        .status,
    ).toBe(200);

    const hookCreated = (await (
      await demoResponse("/api/v1/developer/webhooks", {
        method: "POST",
        body: JSON.stringify({
          name: "PagerDuty",
          url: "https://events.example.com/hook",
          events: "exposure.critical",
        }),
      })
    ).json()) as { signingSecret: string; webhook: { id: string } };
    expect(hookCreated.signingSecret.startsWith("whsec_")).toBe(true);

    // SSRF webhook targets are blocked.
    const ssrfRes = await demoResponse("/api/v1/developer/webhooks", {
      method: "POST",
      body: JSON.stringify({
        name: "Metadata",
        url: "http://169.254.169.254/latest/meta-data/",
        events: "*",
      }),
    });
    expect(ssrfRes.status).toBe(400);
    expect(((await ssrfRes.json()) as { code: string }).code).toBe("url_not_allowed");

    // Webhook test endpoint signs and reports delivery.
    const testRes = await demoResponse(
      `/api/v1/developer/webhooks/${hookCreated.webhook.id}/test`,
      {
        method: "POST",
        body: JSON.stringify({}),
      },
    );
    expect(testRes.status).toBe(200);
    const testDto = (await testRes.json()) as { delivered: boolean; signaturePreview: string };
    expect(testDto.delivered).toBe(true);
    expect(testDto.signaturePreview).toMatch(/^t=\d+,v1=/);

    expect(
      (
        await demoResponse(`/api/v1/developer/webhooks/${hookCreated.webhook.id}`, {
          method: "DELETE",
        })
      ).status,
    ).toBe(200);

    // Public trust access requests require clickwrap NDA acceptance.
    const unaccepted = await demoResponse("/api/v1/public/trust/harborline/requests", {
      method: "POST",
      body: JSON.stringify({
        name: "Buyer",
        email: "buyer@prospect.example",
        company: "Prospect Co",
        purpose: "SOC 2 review",
        ndaAccepted: false,
      }),
    });
    expect(unaccepted.status).toBe(403);
    expect(((await unaccepted.json()) as { code: string }).code).toBe("nda_required");
  });
});
