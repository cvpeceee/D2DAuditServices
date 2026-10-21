import { render, screen } from "@testing-library/react";
import { MemoryRouter, Outlet, Route, Routes } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { api } from "../../shared/api/client";
import "../../shared/i18n";
import { PoliciesPage } from "./PoliciesPage";

vi.mock("../../shared/api/client", async () => {
  const actual =
    await vi.importActual<typeof import("../../shared/api/client")>("../../shared/api/client");
  return { ...actual, api: vi.fn() };
});

describe("PoliciesPage", () => {
  it("lists a policy and its review state", async () => {
    vi.mocked(api).mockResolvedValue([
      {
        code: "POL-ACC",
        title: "Access control",
        ownerName: "Priya N.",
        version: 2,
        state: "InReview",
        controlCodes: ["AC-07"],
        acknowledged: 0,
        outstanding: 1,
      },
    ]);
    render(
      <MemoryRouter initialEntries={["/policies"]}>
        <Routes>
          <Route
            element={
              <Outlet
                context={{
                  workspace: { canWrite: true, role: "Admin", email: "chandra@acme.example" },
                  tenantId: "tenant-1",
                  jobs: [],
                }}
              />
            }
          >
            <Route path="/policies" element={<PoliciesPage />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(await screen.findByRole("heading", { name: "Policies" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /POL-ACC/ })).toBeInTheDocument();
    expect(screen.getByText("In review")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Send reminders" })).toBeInTheDocument();
  });
});
