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

describe("M11 Device Agent & Security Awareness Academy demo parity", () => {
  beforeEach(async () => {
    await resetDemo();
    await loginAs("chandra.vell@harborline.example");
  });

  it("lists enrolled agent fleet, inspects heartbeat history, issues & revokes tokens, and enrolls a device", async () => {
    const devicesRes = await demoResponse("/api/v1/agent/devices");
    expect(devicesRes.status).toBe(200);
    const devices = (await devicesRes.json()) as {
      id: string;
      serial: string;
      stale: boolean;
      passing: number;
      failing: number;
      checks: { key: string; pass: boolean; remediation: string[] }[];
    }[];
    expect(devices.length).toBeGreaterThanOrEqual(4);
    expect(devices.some((d) => d.stale)).toBe(true);
    expect(
      devices.some((d) => d.failing > 0 && d.checks.some((c) => c.remediation.length > 0)),
    ).toBe(true);

    const histRes = await demoResponse(`/api/v1/agent/devices/${devices[0].id}/history`);
    expect(histRes.status).toBe(200);
    const history = (await histRes.json()) as { passing: number; failing: number }[];
    expect(history.length).toBeGreaterThanOrEqual(1);

    const createRes = await demoResponse("/api/v1/agent/enrollment-tokens", {
      method: "POST",
      body: JSON.stringify({ personEmail: "dev.patel@harborline.example" }),
    });
    expect(createRes.status).toBe(200);
    const created = (await createRes.json()) as { id: string; token: string; personEmail: string };
    expect(created.token.startsWith("d2de_")).toBe(true);
    expect(created.personEmail).toBe("dev.patel@harborline.example");

    const enrollRes = await demoResponse("/api/v1/agent/enroll", {
      method: "POST",
      body: JSON.stringify({
        token: created.token,
        serial: "HL-AGT-NEW-99",
        hostname: "dev-linux-box",
        operatingSystem: "Ubuntu 24.04",
        platform: "linux",
        agentKind: "bash",
        agentVersion: "1.0.0",
      }),
    });
    expect(enrollRes.status).toBe(200);
    const enrolled = (await enrollRes.json()) as {
      deviceId: string;
      secret: string;
      intervalHours: number;
    };
    expect(enrolled.deviceId.startsWith("d2dd_")).toBe(true);
    expect(enrolled.secret).toHaveLength(64);
    expect(enrolled.intervalHours).toBe(6);

    // Reusing the same enrollment token is rejected
    const reuseRes = await demoResponse("/api/v1/agent/enroll", {
      method: "POST",
      body: JSON.stringify({
        token: created.token,
        serial: "HL-AGT-REUSE",
        operatingSystem: "Ubuntu 24.04",
        platform: "linux",
      }),
    });
    expect(reuseRes.status).toBe(401);

    // Revoking a device marks it revoked
    const revokeRes = await demoResponse(`/api/v1/agent/devices/${devices[0].id}/revoke`, {
      method: "POST",
    });
    expect(revokeRes.status).toBe(200);
    const revoked = (await revokeRes.json()) as { revoked: boolean };
    expect(revoked.revoked).toBe(true);
  });

  it("runs interactive Security Academy lessons, quiz grading, PDF certificates, HR-06 evidence, and audience assignments", async () => {
    const overviewRes = await demoResponse("/api/v1/academy");
    expect(overviewRes.status).toBe(200);
    const overview = (await overviewRes.json()) as {
      modules: { key: string; status: string; sectionsTotal: number }[];
      passMark: number;
      renewalMonths: number;
    };
    expect(overview.modules).toHaveLength(7);
    expect(overview.passMark).toBe(4);
    expect(overview.renewalMonths).toBe(12);

    const detailRes = await demoResponse("/api/v1/academy/phishing-social-engineering");
    expect(detailRes.status).toBe(200);
    const detail = (await detailRes.json()) as {
      sections: { heading: string }[];
      questions: { prompt: string; choices: string[] }[];
    };
    expect(detail.sections).toHaveLength(4);
    expect(detail.questions).toHaveLength(5);

    const progRes = await demoResponse("/api/v1/academy/phishing-social-engineering/progress", {
      method: "POST",
      body: JSON.stringify({ sectionsCompleted: 4 }),
    });
    expect(progRes.status).toBe(200);
    const prog = (await progRes.json()) as { sectionsCompleted: number };
    expect(prog.sectionsCompleted).toBe(4);

    // Fail quiz first
    const failRes = await demoResponse("/api/v1/academy/phishing-social-engineering/quiz", {
      method: "POST",
      body: JSON.stringify({ answers: [0, 0, 0, 0, 0] }),
    });
    expect(failRes.status).toBe(200);
    const failed = (await failRes.json()) as {
      passed: boolean;
      wrong: number[];
      explanations: string[];
    };
    expect(failed.passed).toBe(false);
    expect(failed.wrong.length).toBeGreaterThanOrEqual(2);
    expect(failed.explanations.length).toBe(failed.wrong.length);

    // Pass quiz with 5/5 correct answers: [1, 2, 1, 2, 1]
    const passRes = await demoResponse("/api/v1/academy/phishing-social-engineering/quiz", {
      method: "POST",
      body: JSON.stringify({ answers: [1, 2, 1, 2, 1] }),
    });
    expect(passRes.status).toBe(200);
    const passed = (await passRes.json()) as {
      passed: boolean;
      correct: number;
      certificateNumber: string;
      evidenceId: string;
    };
    expect(passed.passed).toBe(true);
    expect(passed.correct).toBe(5);
    expect(passed.certificateNumber).toMatch(/^D2D-\d{8}-[A-F0-9]{10}$/);
    expect(passed.evidenceId).toBeTruthy();

    // Download generated PDF certificate
    const pdfRes = await demoResponse("/api/v1/academy/phishing-social-engineering/certificate");
    expect(pdfRes.status).toBe(200);
    expect(pdfRes.headers.get("content-type")).toContain("application/pdf");
    const pdfText = new TextDecoder().decode(await pdfRes.arrayBuffer());
    expect(pdfText.startsWith("%PDF-")).toBe(true);
    expect(pdfText).toContain(passed.certificateNumber);

    // Update audience assignments
    const assignRes = await demoResponse("/api/v1/academy/assignments/secure-coding", {
      method: "PUT",
      body: JSON.stringify({ audiences: ["Department:Engineering", "Role:Admin"] }),
    });
    expect(assignRes.status).toBe(200);
    const assigned = (await assignRes.json()) as { audiences: string[] };
    expect(assigned.audiences).toEqual(["Department:Engineering", "Role:Admin"]);
  });

  it("enforces Auditor isolation on Agent & Academy write/admin routes", async () => {
    await loginAs("auditor@northgate.example");
    const devicesRes = await demoResponse("/api/v1/agent/devices");
    expect(devicesRes.status).toBe(200);
    expect(await devicesRes.json()).toEqual([]);

    const tokenCreate = await demoResponse("/api/v1/agent/enrollment-tokens", {
      method: "POST",
      body: JSON.stringify({}),
    });
    expect(tokenCreate.status).toBe(403);

    const assignmentsRes = await demoResponse("/api/v1/academy/assignments");
    expect(assignmentsRes.status).toBe(403);

    const quizRes = await demoResponse("/api/v1/academy/security-basics/quiz", {
      method: "POST",
      body: JSON.stringify({ answers: [1, 1, 1, 1, 0] }),
    });
    expect(quizRes.status).toBe(403);
  });
});
