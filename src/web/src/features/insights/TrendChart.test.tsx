import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import "../../shared/i18n";
import { TrendChart, frameworkSeries } from "./TrendChart";
import type { TrendPoint } from "./types";

describe("TrendChart", () => {
  it("renders an accessible SVG chart with a data table fallback", () => {
    render(
      <TrendChart
        title="Control health"
        description="Passing controls rose from 60% to 80%"
        labels={["2026-01-01", "2026-01-02", "2026-01-03"]}
        series={[{ label: "Passing", color: "var(--ok)", values: [60, 70, 80] }]}
        max={100}
        unit="%"
      />,
    );
    const chart = screen.getByRole("img", { name: /Control health/ });
    expect(chart.tagName.toLowerCase()).toBe("svg");
    expect(chart.querySelector("title")?.textContent).toBe("Control health");
    expect(chart.querySelector("desc")?.textContent).toContain("60% to 80%");
    expect(chart.querySelector("path[data-series='Passing']")?.getAttribute("d")).toMatch(
      /^M[\d.]+,[\d.]+ L[\d.]+,[\d.]+ L[\d.]+,[\d.]+$/,
    );
    const table = screen.getByRole("table", { hidden: true });
    expect(table.querySelectorAll("tbody tr")).toHaveLength(3);
    expect(table.querySelector("tbody tr:last-child td")?.textContent).toBe("80%");
  });

  it("builds one series per framework, filling gaps with zero", () => {
    const point = (day: string, frameworks: TrendPoint["frameworks"]) =>
      ({ day, frameworks }) as TrendPoint;
    const series = frameworkSeries([
      point("2026-01-01", [{ key: "soc2", name: "SOC 2", passing: 5, total: 10, percent: 50 }]),
      point("2026-01-02", [
        { key: "soc2", name: "SOC 2", passing: 6, total: 10, percent: 60 },
        { key: "iso27001", name: "ISO 27001", passing: 1, total: 4, percent: 25 },
      ]),
    ]);
    expect(series.map((item) => item.label)).toEqual(["SOC 2", "ISO 27001"]);
    expect(series[1].values).toEqual([0, 25]);
  });
});
