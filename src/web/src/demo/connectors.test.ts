import { describe, expect, it } from "vitest";
import type {
  ConnectorActionResponse,
  ConnectorCatalog,
  ConnectorDetail,
  ConnectorRun,
} from "../shared/api/client";
import { examplePassword } from "./accounts";
import { demoResponse, resetDemo } from "./backend";

async function login(email: string) {
  const response = await demoResponse("/api/v1/auth/login", {
    method: "POST",
    body: JSON.stringify({ email, password: examplePassword }),
  });
  expect(response.status).toBe(200);
}

async function getJson<T>(path: string) {
  const response = await demoResponse(path, { headers: { "X-Tenant-Id": "acme" } });
  expect(response.status).toBe(200);
  return (await response.json()) as T;
}

describe("demo connector catalog", () => {
  it("lists all M11 connectors with categories, search filtering, and Harborline defaults", async () => {
    await resetDemo();
    await login("chandra.vell@harborline.example");

    const catalog = await getJson<ConnectorCatalog>("/api/v1/integrations/catalog");
    expect(catalog.total).toBeGreaterThanOrEqual(60);
    expect(catalog.categories).toEqual(
      expect.arrayContaining([
        "identity",
        "hris",
        "endpoint",
        "cloud",
        "code",
        "vulnerability",
        "observability",
        "ticketing",
        "communication",
        "passwords",
        "training",
        "background",
        "data",
      ]),
    );
    const nonBuiltin = catalog.connectors.filter((item) => !item.builtin);
    expect(nonBuiltin.length).toBeGreaterThanOrEqual(45);

    const okta = catalog.connectors.find((item) => item.key === "okta");
    expect(okta?.status).toBe("connected");
    expect(okta?.mode).toBe("mock");
    expect(okta?.resourceCount).toBeGreaterThan(0);

    const snowflake = catalog.connectors.find((item) => item.key === "snowflake");
    expect(snowflake?.status).toBe("available");
    expect(snowflake?.resourceCount).toBe(0);

    const dataOnly = await getJson<ConnectorCatalog>("/api/v1/integrations/catalog?category=data");
    expect(dataOnly.connectors.every((item) => item.category === "data")).toBe(true);
    expect(dataOnly.connectors.map((item) => item.key)).toEqual(
      expect.arrayContaining(["snowflake", "mongodb-atlas", "supabase"]),
    );

    const searchRes = await getJson<ConnectorCatalog>(
      "/api/v1/integrations/catalog?search=crowdstrike",
    );
    expect(searchRes.connectors.map((item) => item.key)).toContain("crowdstrike");
  });

  it("evaluates Harborline automated checks and redacts stored secrets across connect/test/sync/disconnect", async () => {
    await resetDemo();
    await login("chandra.vell@harborline.example");

    const crowdstrike = await getJson<ConnectorDetail>("/api/v1/integrations/catalog/crowdstrike");
    const edrCheck = crowdstrike.checks.find((item) => item.key === "endpoint-edr");
    expect(edrCheck?.outcome).toBe("fail");
    expect(edrCheck?.detail).toContain("degraded");

    const connectRes = await demoResponse("/api/v1/integrations/catalog/snowflake/connect", {
      method: "POST",
      headers: { "X-Tenant-Id": "acme" },
      body: JSON.stringify({
        config: {
          account: "xy12345.us-east-1",
          token: "super-secret-snowflake-token",
          warehouse: "AUDIT_WH",
        },
      }),
    });
    expect(connectRes.status).toBe(200);
    const connected = (await connectRes.json()) as ConnectorActionResponse;
    expect(connected.detail.connector.status).toBe("connected");
    expect(connected.detail.connector.mode).toBe("mock");
    expect(connected.detail.connector.resourceCount).toBeGreaterThan(0);
    expect(connected.run?.kind).toBe("sync");
    expect(connected.run?.trigger).toBe("connect");

    const tokenConfig = connected.detail.config.find((item) => item.key === "token");
    expect(tokenConfig?.configured).toBe(true);
    expect(tokenConfig?.value).toBeNull();
    const accountConfig = connected.detail.config.find((item) => item.key === "account");
    expect(accountConfig?.configured).toBe(true);
    expect(accountConfig?.value).toBe("xy12345.us-east-1");

    const resourcesAfterConnect = await getJson<{ provider: string }[]>("/api/v1/resources");
    expect(resourcesAfterConnect.some((item) => item.provider === "snowflake")).toBe(true);

    const testRes = await demoResponse("/api/v1/integrations/catalog/snowflake/test", {
      method: "POST",
      headers: { "X-Tenant-Id": "acme" },
      body: JSON.stringify({ config: {} }),
    });
    expect(testRes.status).toBe(200);
    const tested = (await testRes.json()) as ConnectorActionResponse;
    expect(tested.run?.kind).toBe("test");
    expect(tested.run?.status).toBe("ok");

    const syncRes = await demoResponse("/api/v1/integrations/catalog/snowflake/sync", {
      method: "POST",
      headers: { "X-Tenant-Id": "acme" },
      body: "{}",
    });
    expect(syncRes.status).toBe(200);
    const synced = (await syncRes.json()) as ConnectorActionResponse;
    expect(synced.run?.kind).toBe("sync");
    expect(synced.run?.trigger).toBe("manual");

    const history = await getJson<ConnectorRun[]>("/api/v1/integrations/catalog/snowflake/history");
    expect(history.length).toBeGreaterThanOrEqual(3);
    expect(history[0]?.kind).toBe("sync");

    const disconnectRes = await demoResponse("/api/v1/integrations/catalog/snowflake/disconnect", {
      method: "POST",
      headers: { "X-Tenant-Id": "acme" },
      body: "{}",
    });
    expect(disconnectRes.status).toBe(200);
    const disconnected = (await disconnectRes.json()) as ConnectorDetail;
    expect(disconnected.connector.status).toBe("available");
    expect(disconnected.connector.resourceCount).toBe(0);

    const resourcesAfterDisconnect = await getJson<{ provider: string }[]>("/api/v1/resources");
    expect(resourcesAfterDisconnect.some((item) => item.provider === "snowflake")).toBe(false);
  });

  it("enforces read-only auditor RBAC and guards built-in connectors", async () => {
    await resetDemo();
    await login("chandra.vell@harborline.example");

    const builtinConnect = await demoResponse("/api/v1/integrations/catalog/aws/connect", {
      method: "POST",
      headers: { "X-Tenant-Id": "acme" },
      body: JSON.stringify({ config: {} }),
    });
    expect(builtinConnect.status).toBe(400);

    const missingConnector = await demoResponse("/api/v1/integrations/catalog/nonexistent");
    expect(missingConnector.status).toBe(404);

    await demoResponse("/api/v1/auth/logout", { method: "POST" });
    await login("auditor@northgate.example");

    const catalog = await getJson<ConnectorCatalog>("/api/v1/integrations/catalog");
    expect(catalog.total).toBeGreaterThanOrEqual(60);

    const forbiddenConnect = await demoResponse("/api/v1/integrations/catalog/snowflake/connect", {
      method: "POST",
      headers: { "X-Tenant-Id": "acme" },
      body: JSON.stringify({ config: {} }),
    });
    expect(forbiddenConnect.status).toBe(403);
  });
});
