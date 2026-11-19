import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { frameworkTag, initials, Ring, safeHref } from "./ui";
import { Icon } from "./icons";

describe("shared ui", () => {
  it("announces a readiness ring", () => {
    render(<Ring value={69} tone="var(--accent)" />);
    expect(screen.getByRole("img", { name: "69%" })).toBeInTheDocument();
  });

  it("builds initials and framework tags", () => {
    expect(initials("Chandra Example")).toBe("CE");
    expect(frameworkTag("soc2")).toBe("SOC 2");
    expect(frameworkTag("iso27001")).toBe("ISO");
    expect(frameworkTag("pci-dss")).toBe("PCI 4.0");
    expect(frameworkTag("iso42001")).toBe("ISO 42001");
  });

  it("sanitizes unsafe link protocols", () => {
    expect(safeHref("https://checkr.example/report/1")).toBe("https://checkr.example/report/1");
    expect(safeHref("http://localhost:5173/test")).toBe("http://localhost:5173/test");
    expect(safeHref("javascript:alert(1)")).toBeNull();
    expect(safeHref("data:text/html,<h1>x</h1>")).toBeNull();
    expect(safeHref("")).toBeNull();
    expect(safeHref(null)).toBeNull();
  });

  it("renders icons with accessible aria-hidden", () => {
    const { container } = render(<Icon name="dashboard" />);
    const svg = container.querySelector("svg");
    expect(svg).toBeInTheDocument();
    expect(svg).toHaveAttribute("aria-hidden", "true");
    expect(svg).toHaveClass("ico");
  });
});
