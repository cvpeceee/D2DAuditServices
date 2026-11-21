import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Outlet, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  api,
  type ConnectorActionResponse,
  type ConnectorCatalog,
  type ConnectorDetail,
} from "../../shared/api/client";
import "../../shared/i18n";
import { IntegrationsPage } from "./IntegrationsPage";

vi.mock("../../shared/api/client", async () => {
  const actual =
    await vi.importActual<typeof import("../../shared/api/client")>("../../shared/api/client");
  return { ...actual, api: vi.fn() };
});

function renderPage(canWrite = true) {
  return render(
    <MemoryRouter initialEntries={["/integrations"]}>
      <Routes>
        <Route
          element={
            <Outlet
              context={{
                workspace: { canWrite, exampleData: true, name: "Harborline" },
                tenantId: "tenant-1",
                jobs: [],
              }}
            />
          }
        >
          <Route path="/integrations" element={<IntegrationsPage />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

const sampleCatalog: ConnectorCatalog = {
  categories: ["identity", "data", "cloud"],
  total: 3,
  connected: 1,
  connectors: [
    {
      key: "okta",
      name: "Okta",
      category: "identity",
      authType: "apiKey",
      description: "Workforce directory users and MFA enrolment.",
      resources: ["user"],
      tests: ["mfa-all-users"],
      controls: ["AC-07", "HR-08"],
      docsUrl: "https://developer.okta.com/docs/reference/api/users/",
      builtin: false,
      managedAt: null,
      status: "connected",
      mode: "mock",
      resourceCount: 50,
      lastSyncedAt: "2026-11-20T12:00:00Z",
      lastError: null,
    },
    {
      key: "snowflake",
      name: "Snowflake",
      category: "data",
      authType: "apiKey",
      description: "Human users with MFA enrolment and network policies.",
      resources: ["user", "firewall"],
      tests: ["mfa-all-users", "network-exposure"],
      controls: ["AC-07", "CFG-01"],
      docsUrl: "https://docs.snowflake.com/en/developer-guide/sql-api/reference",
      builtin: false,
      managedAt: null,
      status: "available",
      mode: null,
      resourceCount: 0,
      lastSyncedAt: null,
      lastError: null,
    },
    {
      key: "aws",
      name: "Amazon Web Services",
      category: "cloud",
      authType: "role",
      description: "Amazon Web Services is collected by the existing integration flow.",
      resources: ["bucket"],
      tests: ["s3-public-access"],
      controls: ["CFG-01"],
      docsUrl: "https://docs.aws.amazon.com/IAM/latest/UserGuide/id_roles.html",
      builtin: true,
      managedAt: "/integrations",
      status: "connected",
      mode: "mock",
      resourceCount: 12,
      lastSyncedAt: "2026-11-20T12:00:00Z",
      lastError: null,
    },
  ],
};

const snowflakeDetail: ConnectorDetail = {
  connector: sampleCatalog.connectors[1],
  fields: [
    {
      key: "account",
      label: "Account identifier",
      secret: false,
      required: true,
      placeholder: "xy12345.us-east-1",
    },
    {
      key: "token",
      label: "Access token",
      secret: true,
      required: true,
      placeholder: "",
    },
  ],
  config: [
    { key: "account", value: null, configured: false },
    { key: "token", value: null, configured: false },
  ],
  envKeys: [],
  checks: [
    {
      key: "network-exposure",
      name: "Firewalls block unrestricted inbound",
      controlCode: "CFG-01",
      outcome: "notRun",
      detail: "No firewalls were collected.",
    },
  ],
  history: [],
};

describe("IntegrationsPage", () => {
  beforeEach(() => {
    vi.mocked(api).mockReset();
  });

  it("shows the four providers and does not invent a connection", async () => {
    vi.mocked(api).mockResolvedValue([]);
    renderPage();
    expect(await screen.findByRole("heading", { name: "Integrations" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Amazon Web Services" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Google Workspace" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Microsoft 365" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "GitHub" })).toBeInTheDocument();
    expect(screen.getAllByText("Not connected").length).toBeGreaterThan(0);
  });

  it("filters the marketplace by category and search, and connects a connector from the drawer", async () => {
    const user = userEvent.setup();
    vi.mocked(api).mockImplementation(async (path, options) => {
      if (path === "/api/v1/integrations") return [];
      if (path === "/api/v1/resources") return [];
      if (path === "/api/v1/integrations/catalog") return sampleCatalog;
      if (path === "/api/v1/integrations/catalog/snowflake") return snowflakeDetail;
      if (path === "/api/v1/integrations/catalog/snowflake/connect" && options?.method === "POST") {
        const connectedDetail: ConnectorDetail = {
          ...snowflakeDetail,
          connector: {
            ...snowflakeDetail.connector,
            status: "connected",
            mode: "mock",
            resourceCount: 9,
            lastSyncedAt: "2026-11-21T12:00:00Z",
          },
          config: [
            { key: "account", value: "xy12345.us-east-1", configured: true },
            { key: "token", value: null, configured: true },
          ],
          history: [
            {
              id: "run-1",
              kind: "sync",
              mode: "mock",
              status: "ok",
              resourceCount: 9,
              error: null,
              startedAt: "2026-11-21T12:00:00Z",
              finishedAt: "2026-11-21T12:00:00Z",
              trigger: "connect",
            },
          ],
        };
        const response: ConnectorActionResponse = {
          detail: connectedDetail,
          run: connectedDetail.history[0],
          jobId: null,
          message: "mock",
        };
        return response;
      }
      return [];
    });

    renderPage();
    expect(await screen.findByText("Integration marketplace")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Okta" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Snowflake" })).toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: /Data \(1\)/i }));
    expect(screen.getByRole("heading", { name: "Snowflake" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Okta" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: /All categories/i }));
    await user.type(screen.getByLabelText("Search connectors"), "snowflake");
    expect(screen.getByRole("heading", { name: "Snowflake" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Okta" })).not.toBeInTheDocument();

    const snowflakeCard = screen.getByRole("heading", { name: "Snowflake" }).closest(".card")!;
    const connectButton = snowflakeCard.querySelector("button")!;
    await user.click(connectButton);

    expect(await screen.findByLabelText("Account identifier")).toBeInTheDocument();
    await user.type(screen.getByLabelText("Account identifier"), "xy12345.us-east-1");
    await user.type(screen.getByLabelText("Access token"), "secret-tok");

    const drawer = screen.getByRole("complementary", { name: "Snowflake" });
    const submitBtn = drawer.querySelector('button[type="submit"]')!;
    await user.click(submitBtn);

    expect(await screen.findByRole("status")).toHaveTextContent(
      "Snowflake connected (Mock mode, 9 resources).",
    );
    expect(screen.getByRole("button", { name: "Disconnect" })).toBeInTheDocument();
  });
});
