import { useEffect, useId, useState } from "react";
import { useTranslation } from "react-i18next";
import { ApiError, api } from "../../shared/api/client";
import { EmptyState, ErrorState, LoadingState } from "../../shared/ui/states";
import type { TrendPoint, TrendsResponse } from "./types";

export type ChartSeries = { label: string; color: string; values: number[] };

const width = 640;
const height = 180;
const pad = { top: 12, right: 12, bottom: 24, left: 36 };
const palette = ["var(--accent)", "var(--ok)", "var(--warn)", "var(--bad)", "var(--info, #3b82f6)"];

export function seriesColor(index: number) {
  return palette[index % palette.length];
}

function niceMax(values: number[]) {
  const top = Math.max(1, ...values);
  if (top <= 5) return 5;
  const step = 10 ** Math.floor(Math.log10(top));
  return Math.ceil(top / step) * step;
}

export function TrendChart({
  title,
  description,
  labels,
  series,
  max,
  unit = "",
}: {
  title: string;
  description: string;
  labels: string[];
  series: ChartSeries[];
  max?: number;
  unit?: string;
}) {
  const { t } = useTranslation();
  const id = useId();
  const top = max ?? niceMax(series.flatMap((item) => item.values));
  const innerW = width - pad.left - pad.right;
  const innerH = height - pad.top - pad.bottom;
  const x = (index: number) =>
    pad.left + (labels.length <= 1 ? innerW / 2 : (index * innerW) / (labels.length - 1));
  const y = (value: number) => pad.top + innerH - (Math.min(value, top) / top) * innerH;
  const ticks = [0, top / 2, top];
  const tickEvery = Math.max(1, Math.ceil(labels.length / 6));

  return (
    <figure className="trend-chart">
      <svg
        viewBox={`0 0 ${width} ${height}`}
        role="img"
        aria-labelledby={`${id}-t ${id}-d`}
        preserveAspectRatio="none"
        className="trend-svg"
      >
        <title id={`${id}-t`}>{title}</title>
        <desc id={`${id}-d`}>{description}</desc>
        {ticks.map((tick) => (
          <g key={tick}>
            <line
              x1={pad.left}
              x2={width - pad.right}
              y1={y(tick)}
              y2={y(tick)}
              stroke="var(--border)"
              strokeDasharray="3 3"
            />
            <text
              x={pad.left - 6}
              y={y(tick) + 4}
              textAnchor="end"
              fontSize="10"
              fill="var(--faint)"
            >
              {Math.round(tick)}
              {unit}
            </text>
          </g>
        ))}
        {labels.map((label, index) =>
          index % tickEvery === 0 || index === labels.length - 1 ? (
            <text
              key={label}
              x={x(index)}
              y={height - 6}
              textAnchor="middle"
              fontSize="10"
              fill="var(--faint)"
            >
              {label.slice(5)}
            </text>
          ) : null,
        )}
        {series.map((item) => {
          const path = item.values
            .map(
              (value, index) =>
                `${index === 0 ? "M" : "L"}${x(index).toFixed(1)},${y(value).toFixed(1)}`,
            )
            .join(" ");
          return (
            <path
              key={item.label}
              d={path}
              fill="none"
              stroke={item.color}
              strokeWidth={2}
              vectorEffect="non-scaling-stroke"
              data-series={item.label}
            />
          );
        })}
      </svg>
      <figcaption className="trend-legend">
        {series.map((item) => (
          <span key={item.label}>
            <i style={{ background: item.color }} aria-hidden="true" />
            {item.label}
            {item.values.length > 0 && (
              <b>
                {" "}
                {item.values[item.values.length - 1]}
                {unit}
              </b>
            )}
          </span>
        ))}
      </figcaption>
      <details className="trend-table">
        <summary>{t("insights.showTable")}</summary>
        <div className="table-wrap">
          <table>
            <caption className="sr-only">{title}</caption>
            <thead>
              <tr>
                <th scope="col">{t("insights.day")}</th>
                {series.map((item) => (
                  <th scope="col" key={item.label}>
                    {item.label}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {labels.map((label, index) => (
                <tr key={label}>
                  <th scope="row">{label}</th>
                  {series.map((item) => (
                    <td key={item.label}>
                      {item.values[index]}
                      {unit}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </figure>
  );
}

export function frameworkSeries(points: TrendPoint[]): ChartSeries[] {
  const names = new Map<string, string>();
  points.forEach((point) => point.frameworks.forEach((item) => names.set(item.key, item.name)));
  return [...names.entries()].map(([key, name], index) => ({
    label: name,
    color: seriesColor(index),
    values: points.map((point) => point.frameworks.find((item) => item.key === key)?.percent ?? 0),
  }));
}

export function TrendsPanel({ tenantId }: { tenantId: string | null }) {
  const { t } = useTranslation();
  const [days, setDays] = useState(90);
  const [data, setData] = useState<TrendsResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    if (!tenantId) return;
    let cancelled = false;
    setError(null);
    void api<TrendsResponse>(`/api/v1/insights/trends?days=${days}`, { tenantId })
      .then((value) => {
        if (!cancelled) setData(value);
      })
      .catch((caught: unknown) => {
        if (!cancelled) setError(caught instanceof ApiError ? caught.code : "load");
      });
    return () => {
      cancelled = true;
    };
  }, [tenantId, days, attempt]);

  const points = data?.points ?? [];
  const labels = points.map((point) => point.day);
  const first = points[0];
  const last = points[points.length - 1];

  return (
    <section className="card" style={{ marginTop: 14 }} aria-labelledby="trend-heading">
      <h3 id="trend-heading">
        {t("insights.title")}
        <span className="r tabs" role="group" aria-label={t("insights.range")}>
          {[30, 90, 180].map((value) => (
            <button
              key={value}
              type="button"
              className={days === value ? "on" : ""}
              aria-pressed={days === value}
              onClick={() => setDays(value)}
            >
              {t("insights.days", { count: value })}
            </button>
          ))}
        </span>
      </h3>
      {error ? (
        <ErrorState code={error} onRetry={() => setAttempt((value) => value + 1)} />
      ) : !data ? (
        <LoadingState rows={1} />
      ) : points.length === 0 ? (
        <EmptyState>{t("insights.empty")}</EmptyState>
      ) : (
        <div className="trend-grid">
          <TrendChart
            title={t("insights.controlHealth")}
            description={t("insights.controlHealthDesc", {
              from: first?.percent ?? 0,
              to: last?.percent ?? 0,
              count: points.length,
            })}
            labels={labels}
            max={100}
            unit="%"
            series={[
              {
                label: t("insights.passingPercent"),
                color: "var(--ok)",
                values: points.map((point) => point.percent),
              },
            ]}
          />
          <TrendChart
            title={t("insights.readiness")}
            description={t("insights.readinessDesc", { count: points.length })}
            labels={labels}
            max={100}
            unit="%"
            series={frameworkSeries(points)}
          />
          <TrendChart
            title={t("insights.failing")}
            description={t("insights.failingDesc", {
              from: first?.failingTests ?? 0,
              to: last?.failingTests ?? 0,
            })}
            labels={labels}
            series={[
              {
                label: t("insights.failingTests"),
                color: "var(--bad)",
                values: points.map((point) => point.failingTests),
              },
              {
                label: t("insights.overdue"),
                color: "var(--warn)",
                values: points.map((point) => point.overdueItems),
              },
              {
                label: t("insights.criticalVulns"),
                color: "var(--accent)",
                values: points.map((point) => point.vulnCritical + point.vulnHigh),
              },
            ]}
          />
        </div>
      )}
    </section>
  );
}
