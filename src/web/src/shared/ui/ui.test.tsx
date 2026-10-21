import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { frameworkTag, initials, Ring } from "./ui";

describe("shared ui", () => {
  it("announces a readiness ring", () => {
    render(<Ring value={69} tone="var(--accent)" />);
    expect(screen.getByRole("img", { name: "69%" })).toBeInTheDocument();
  });

  it("builds initials and framework tags", () => {
    expect(initials("Chandra Example")).toBe("CE");
    expect(frameworkTag("soc2")).toBe("SOC 2");
    expect(frameworkTag("iso27001")).toBe("ISO");
  });
});
