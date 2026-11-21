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

const admin = "chandra.vell@harborline.example";
type Point = { day: string; percent: number; frameworks: { key: string }[] };
type Result = { kind: string; title: string; href: string };
type Feed = { unread: number; items: { id: string; kind: string; read: boolean }[] };

async function search(q: string) {
  const res = await demoResponse(`/api/v1/search?q=${encodeURIComponent(q)}`);
  expect(res.status).toBe(200);
  return ((await res.json()) as { results: Result[] }).results;
}

describe("M11 insights demo parity", () => {
  beforeEach(async () => {
    await resetDemo();
    await loginAs(admin);
  });

  it("returns 90 days of improving posture history ending today", async () => {
    const res = await demoResponse("/api/v1/insights/trends?days=90");
    expect(res.status).toBe(200);
    const { points } = (await res.json()) as { points: Point[] };
    expect(points).toHaveLength(90);
    const today = new Date().toISOString().slice(0, 10);
    expect(points[points.length - 1].day).toBe(today);
    expect(points[points.length - 1].percent).toBeGreaterThan(points[0].percent);
    expect(points[0].frameworks.length).toBeGreaterThan(0);
    const again = (await (await demoResponse("/api/v1/insights/trends?days=90")).json()) as {
      points: Point[];
    };
    expect(again.points).toEqual(points);
  });

  it("scopes search results to the caller's role", async () => {
    const adminResults = await search("an");
    expect(adminResults.some((item) => item.kind === "control")).toBe(true);
    expect(adminResults.every((item) => item.href.startsWith("/"))).toBe(true);
    expect((await search("iso")).some((item) => item.kind === "framework")).toBe(true);

    await loginAs("auditor@northgate.example");
    const auditorResults = await search("an");
    expect(auditorResults.length).toBeGreaterThan(0);
    expect(auditorResults.some((item) => ["risk", "vendor", "person"].includes(item.kind))).toBe(
      false,
    );
    expect((await search("soc")).some((item) => item.kind === "framework")).toBe(true);
    expect((await search("iso")).some((item) => item.kind === "framework")).toBe(false);

    await loginAs("dev.patel@harborline.example");
    const employeeResults = await search("a");
    expect(employeeResults.every((item) => ["policy", "framework"].includes(item.kind))).toBe(true);
  });

  it("tracks unread notifications, read state and preferences", async () => {
    const feed = (await (await demoResponse("/api/v1/notifications/feed")).json()) as Feed;
    expect(feed.unread).toBeGreaterThan(0);
    const first = feed.items[0];
    const read = await demoResponse(`/api/v1/notifications/${first.id}/read`, { method: "POST" });
    expect(read.status).toBe(204);
    const count = (await (await demoResponse("/api/v1/notifications/unread-count")).json()) as {
      unread: number;
    };
    expect(count.unread).toBe(feed.unread - 1);

    expect((await demoResponse("/api/v1/notifications/read-all", { method: "POST" })).status).toBe(
      204,
    );
    const after = (await (await demoResponse("/api/v1/notifications/unread-count")).json()) as {
      unread: number;
    };
    expect(after.unread).toBe(0);

    const kind = first.kind;
    const put = await demoResponse("/api/v1/notifications/preferences", {
      method: "PUT",
      body: JSON.stringify({ preferences: [{ kind, inApp: false, email: true, slack: false }] }),
    });
    expect(put.status).toBe(200);
    const filtered = (await (await demoResponse("/api/v1/notifications/feed")).json()) as Feed;
    expect(filtered.items.some((item) => item.kind === kind)).toBe(false);
  });

  it("exports reports for admins, forbids auditors, and mocks scheduled delivery", async () => {
    const csv = await demoResponse("/api/v1/reports/risk?format=csv");
    expect(csv.status).toBe(200);
    expect(csv.headers.get("Content-Type")).toContain("text/csv");
    expect((await csv.text()).split("\r\n").length).toBeGreaterThan(2);

    const pdf = await demoResponse("/api/v1/reports/board?format=pdf");
    expect(pdf.status).toBe(200);

    const saved = await demoResponse("/api/v1/reports/schedules", {
      method: "PUT",
      body: JSON.stringify({
        reportKey: "board",
        frequency: "Weekly",
        format: "pdf",
        recipients: "board@harborline.example",
        enabled: true,
      }),
    });
    expect(saved.status).toBe(200);
    const sent = await demoResponse("/api/v1/reports/schedules/board/send", { method: "POST" });
    expect(sent.status).toBe(200);
    expect(((await sent.json()) as { deliveryId: string }).deliveryId).toMatch(/^email:demo/);

    const bad = await demoResponse("/api/v1/reports/schedules", {
      method: "PUT",
      body: JSON.stringify({
        reportKey: "board",
        frequency: "Hourly",
        format: "pdf",
        recipients: "x",
        enabled: true,
      }),
    });
    expect(bad.status).toBe(400);

    await loginAs("auditor@northgate.example");
    expect((await demoResponse("/api/v1/reports/risk?format=csv")).status).toBe(403);
    expect((await demoResponse("/api/v1/reports/schedules")).status).toBe(403);
  });

  it("confirms password reset links publicly", async () => {
    await demoResponse("/api/v1/auth/logout", { method: "POST" });
    const reset = (token: string, password: string) =>
      demoResponse("/api/v1/auth/reset", {
        method: "POST",
        body: JSON.stringify({ token, password }),
      });
    const expired = await reset("expired", "a-long-enough-password");
    expect(expired.status).toBe(400);
    expect(await expired.json()).toEqual({ code: "invalid_token" });
    const weak = await reset("token-1", "short");
    expect(await weak.json()).toEqual({ code: "weak_password" });
    const ok = await reset("token-1", "a-long-enough-password");
    expect(ok.status).toBe(200);
  });
});
