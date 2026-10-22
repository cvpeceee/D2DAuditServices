import { render, screen } from "@testing-library/react";
import { MemoryRouter, Outlet, Route, Routes } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { api } from "../../shared/api/client";
import "../../shared/i18n";
import { IntegrationsPage } from "./IntegrationsPage";

vi.mock("../../shared/api/client", async () => {
  const actual =
    await vi.importActual<typeof import("../../shared/api/client")>("../../shared/api/client");
  return { ...actual, api: vi.fn() };
});

describe("IntegrationsPage", () => {
  it("shows the four providers and does not invent a connection", async () => {
    vi.mocked(api).mockResolvedValue([]);
    render(
      <MemoryRouter initialEntries={["/integrations"]}>
        <Routes>
          <Route
            element={
              <Outlet
                context={{
                  workspace: { canWrite: true, exampleData: true, name: "Acme Robotics" },
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
    expect(await screen.findByRole("heading", { name: "Integrations" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Amazon Web Services" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Google Workspace" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Microsoft 365" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "GitHub" })).toBeInTheDocument();
    expect(screen.getAllByText("Not connected").length).toBeGreaterThan(0);
  });
});
