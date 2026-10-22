import { render, screen } from "@testing-library/react";
import { MemoryRouter, Outlet, Route, Routes } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { api } from "../../shared/api/client";
import "../../shared/i18n";
import { RisksPage } from "./RisksPage";

vi.mock("../../shared/api/client", async () => {
  const actual =
    await vi.importActual<typeof import("../../shared/api/client")>("../../shared/api/client");
  return { ...actual, api: vi.fn() };
});

describe("RisksPage", () => {
  it("shows the heat map and a scored risk", async () => {
    vi.mocked(api).mockResolvedValue([
      {
        code: "R-01",
        title: "Public storage",
        statement: "The marketing site bucket can be read by anyone.",
        likelihood: 4,
        impact: 5,
        score: 20,
        band: "critical",
        ownerName: "Priya N.",
        ownerEmail: "priya@acme.example",
        treatment: "Mitigate",
        treatmentPlan: "Turn on Block Public Access.",
        reviewOn: "2026-11-01",
        residualLikelihood: 2,
        residualImpact: 3,
        residualScore: 6,
        residualBand: "medium",
        controlCodes: ["CFG-01"],
      },
    ]);
    render(
      <MemoryRouter initialEntries={["/risks"]}>
        <Routes>
          <Route
            element={
              <Outlet
                context={{
                  workspace: {
                    canWrite: true,
                    role: "Admin",
                    email: "chandra.vell@harborline.example",
                  },
                  tenantId: "tenant-1",
                }}
              />
            }
          >
            <Route path="/risks" element={<RisksPage />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(await screen.findByRole("heading", { name: "Risk register" })).toBeInTheDocument();
    expect(screen.getByRole("grid", { name: "Heat map" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Public storage/ })).toBeInTheDocument();
    expect(screen.getByText("Critical 20")).toBeInTheDocument();
  });
});
