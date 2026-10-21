import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Outlet, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { api } from "../../shared/api/client";
import "../../shared/i18n";
import { ControlsPage } from "./ControlsPage";

vi.mock("../auth/AuthContext", () => ({
  useAuth: () => ({
    tenantId: "tenant-1",
    user: { id: "user-1", email: "dev@acme.example", displayName: "Dev" },
  }),
}));

vi.mock("../../shared/api/client", async () => {
  const actual =
    await vi.importActual<typeof import("../../shared/api/client")>("../../shared/api/client");
  return { ...actual, api: vi.fn() };
});

const list = [
  {
    code: "PP-01",
    title: "Privacy policy",
    status: "passing",
    ownerName: null,
    ownerEmail: null,
    frequency: "annual",
    testsPassing: 1,
    testsTotal: 1,
    frameworks: ["soc2"],
  },
];

const detail = {
  code: "PP-01",
  title: "Privacy policy",
  description: "Example control.",
  status: "passing",
  ownerName: null,
  ownerEmail: null,
  frequency: "annual",
  mappings: [],
  tests: [],
  evidence: [],
};

beforeEach(() => {
  vi.mocked(api).mockImplementation((path: string) => {
    if (path.includes("/PP-01")) return Promise.resolve(detail);
    return Promise.resolve(list);
  });
});

function renderPage() {
  return render(
    <MemoryRouter initialEntries={["/controls"]}>
      <Routes>
        <Route element={<Outlet context={{ workspace: null }} />}>
          <Route path="/controls" element={<ControlsPage />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe("ControlsPage", () => {
  it("opens a control from the keyboard", async () => {
    const user = userEvent.setup();
    renderPage();
    const open = await screen.findByRole("button", { name: "Privacy policy" });
    open.focus();
    await user.keyboard("{Enter}");
    expect(
      await screen.findByRole("complementary", { name: "Privacy policy" }),
    ).toBeInTheDocument();
  });
});
