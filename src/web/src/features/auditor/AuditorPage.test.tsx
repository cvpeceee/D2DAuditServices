import { render, screen } from "@testing-library/react";
import { MemoryRouter, Outlet, Route, Routes } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { api } from "../../shared/api/client";
import "../../shared/i18n";
import { AuditorPage } from "./AuditorPage";

vi.mock("../../shared/api/client", async () => {
  const actual =
    await vi.importActual<typeof import("../../shared/api/client")>("../../shared/api/client");
  return { ...actual, api: vi.fn() };
});

const page = {
  engagement: {
    name: "SOC 2 Type II — Fieldwork",
    firmName: "Northgate Assurance",
    frameworkKey: "soc2",
    scope: "SOC 2 Type II (Security, Availability).",
    periodStart: "2026-04-01",
    periodEnd: "2026-09-30",
    fieldworkOn: "2026-10-28",
    controlsPassing: 4,
    controlsTotal: 8,
    requestsOpen: 2,
    requestsTotal: 5,
    findingsTotal: 2,
    exceptions: 1,
    averageResponseHours: 18.5,
  },
  requests: [
    {
      code: "REQ-14",
      title: "New hire access",
      population: "23 hires",
      sampleSize: 5,
      source: "HR system",
      dueOn: "2026-10-24",
      status: "Open",
      response: null,
    },
  ],
  findings: [
    {
      code: "F-02",
      title: "MFA gap",
      severity: "Exception",
      detail: "Two accounts lack MFA.",
      requirementCode: "CC6.1",
      managementResponse: null,
      responseByName: null,
    },
  ],
  evidence: [],
};

describe("AuditorPage", () => {
  it("shows the engagement and hides the package action from an employee", async () => {
    vi.mocked(api).mockResolvedValue(page);
    render(
      <MemoryRouter initialEntries={["/auditor"]}>
        <Routes>
          <Route
            element={
              <Outlet
                context={{
                  workspace: { canWrite: false, role: "Employee", email: "lena@acme.example" },
                  tenantId: "tenant-1",
                  jobs: [],
                }}
              />
            }
          >
            <Route path="/auditor" element={<AuditorPage />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(
      await screen.findByRole("heading", { name: "SOC 2 Type II — Fieldwork" }),
    ).toBeInTheDocument();
    expect(screen.getByText("REQ-14 · New hire access")).toBeInTheDocument();
    expect(screen.getByText("Exception")).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Download audit package" }),
    ).not.toBeInTheDocument();
  });

  it("shows export progress from the jobs hub", async () => {
    vi.mocked(api).mockResolvedValue(page);
    render(
      <MemoryRouter initialEntries={["/auditor"]}>
        <Routes>
          <Route
            element={
              <Outlet
                context={{
                  workspace: {
                    canWrite: false,
                    role: "Auditor",
                    email: "auditor@northgate.example",
                  },
                  tenantId: "tenant-1",
                  jobs: [
                    {
                      id: "job-1",
                      kind: "export",
                      status: "Running",
                      currentStep: "Checking the hash chain",
                      percent: 40,
                      step: 2,
                      totalSteps: 5,
                    },
                  ],
                }}
              />
            }
          >
            <Route path="/auditor" element={<AuditorPage />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(await screen.findByRole("progressbar")).toHaveAttribute("aria-valuenow", "40");
    expect(screen.getByText("Checking the hash chain")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Download audit package" })).toBeInTheDocument();
  });
});
