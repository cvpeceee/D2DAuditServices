import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";
import "../../shared/i18n";
import { PlaceholderPage } from "./PlaceholderPage";

describe("PlaceholderPage", () => {
  it("names the section and does not invent data", () => {
    render(
      <MemoryRouter initialEntries={["/risks"]}>
        <PlaceholderPage />
      </MemoryRouter>,
    );
    expect(screen.getByRole("heading", { name: "Not in this milestone" })).toBeInTheDocument();
    expect(screen.getByText(/Risks is on the roadmap/)).toBeInTheDocument();
  });
});
