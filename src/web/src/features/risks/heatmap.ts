export type HeatmapView = "inherent" | "residual";

export type HeatmapPoint = {
  code: string;
  likelihood: number;
  impact: number;
  residualLikelihood: number;
  residualImpact: number;
};

export type HeatmapCell = {
  likelihood: number;
  impact: number;
  score: number;
  band: "critical" | "high" | "medium" | "low";
  codes: string[];
};

export function bandFor(score: number): HeatmapCell["band"] {
  if (score >= 15) return "critical";
  if (score >= 8) return "high";
  if (score >= 4) return "medium";
  return "low";
}

const clamp = (value: number) => Math.min(5, Math.max(1, Math.round(value) || 1));

export function coordinates(point: HeatmapPoint, view: HeatmapView) {
  return view === "inherent"
    ? { likelihood: clamp(point.likelihood), impact: clamp(point.impact) }
    : { likelihood: clamp(point.residualLikelihood), impact: clamp(point.residualImpact) };
}

/** Builds the 5x5 grid, impact 5 first, likelihood 1 to 5 within each row. */
export function buildHeatmap(points: HeatmapPoint[], view: HeatmapView): HeatmapCell[] {
  const cells: HeatmapCell[] = [];
  for (let impact = 5; impact >= 1; impact--) {
    for (let likelihood = 1; likelihood <= 5; likelihood++) {
      const codes = points
        .filter((point) => {
          const at = coordinates(point, view);
          return at.likelihood === likelihood && at.impact === impact;
        })
        .map((point) => point.code)
        .sort();
      const score = likelihood * impact;
      cells.push({ likelihood, impact, score, band: bandFor(score), codes });
    }
  }
  return cells;
}

export function criticalCount(points: HeatmapPoint[], view: HeatmapView) {
  return buildHeatmap(points, view)
    .filter((cell) => cell.band === "critical")
    .reduce((sum, cell) => sum + cell.codes.length, 0);
}

export function annualizedLoss(sle: number | null | undefined, aro: number | null | undefined) {
  if (sle == null || aro == null || sle < 0 || aro < 0) return null;
  return Math.round(sle * aro * 100) / 100;
}
