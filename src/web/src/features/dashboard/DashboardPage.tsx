import { useEffect, useState } from "react";
import { Trans, useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { ApiError, api, type Dashboard, type Integration } from "../../shared/api/client";
import { useAuth } from "../auth/AuthContext";
import { daysUntil, formatDate, Pill, Ring, Skeleton } from "../../shared/ui/ui";

function IntegrationHealth({ tenantId }: { tenantId: string | null }) {
  const { t } = useTranslation();
  const [rows, setRows] = useState<Integration[] | null>(null);
  useEffect(() => {
    if (!tenantId) return;
    let cancelled = false;
    void api<Integration[]>("/api/v1/integrations", { tenantId })
      .then((value) => {
        if (!cancelled) setRows(value);
      })
      .catch(() => {
        if (!cancelled) setRows([]);
      });
    return () => {
      cancelled = true;
    };
  }, [tenantId]);
  if (!rows || rows.length === 0)
    return <div className="empty">{t("dashboard.integrationsEmpty")}</div>;
  return (
    <div>
      {rows.map((row) => (
        <div className="row" key={row.id} style={{ padding: "6px 0" }}>
          <span>{row.displayName}</span>
          <span className="m" style={{ marginInlineStart: "auto" }}>
            {t(`integrations.status.${row.status}`, { defaultValue: row.status })}
            {row.example ? ` · ${t("integrations.example")}` : ""}
          </span>
        </div>
      ))}
    </div>
  );
}

function ringTone(percent: number) {
  if (percent >= 85) return "var(--ok)";
  if (percent >= 70) return "var(--accent)";
  return "var(--warn)";
}

export function DashboardPage() {
  const { t } = useTranslation();
  const { tenantId } = useAuth();
  const [data, setData] = useState<Dashboard | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!tenantId) return;
    let cancelled = false;
    setData(null);
    setError(null);
    void api<Dashboard>("/api/v1/dashboard", { tenantId })
      .then((value) => {
        if (!cancelled) setData(value);
      })
      .catch((caught: unknown) => {
        if (!cancelled) setError(caught instanceof ApiError ? caught.code : "load");
      });
    return () => {
      cancelled = true;
    };
  }, [tenantId]);

  if (error)
    return (
      <div className="error" role="alert">
        {t(error === "tenant_forbidden" ? "errors.forbidden" : "errors.load")}
      </div>
    );
  if (!data) {
    return (
      <div className="stack">
        <Skeleton className="lg" />
        <Skeleton />
        <Skeleton />
      </div>
    );
  }

  const hour = new Date().getHours();
  const greeting =
    hour < 12
      ? "dashboard.goodMorning"
      : hour < 17
        ? "dashboard.goodAfternoon"
        : "dashboard.goodEvening";
  const first = data.displayName.split(" ")[0] || data.displayName;
  const health = [
    ["passing", data.health.passing, "ok"],
    ["failing", data.health.failing, "bad"],
    ["dueSoon", data.health.dueSoon, "warn"],
    ["needsEvidence", data.health.needsEvidence, "warn"],
    ["manual", data.health.manual, "info"],
    ["notStarted", data.health.notStarted, "neutral"],
  ] as const;
  const max = Math.max(1, ...health.map((item) => item[1]));

  return (
    <>
      <div className="head">
        <div>
          <h1>{t(greeting, { name: first })}</h1>
          <div className="sub">
            <Trans
              i18nKey="dashboard.ready"
              values={{
                tenant: data.tenantName,
                percent: data.readinessPercent,
                count: data.frameworkCount,
              }}
              components={{ 1: <b className="ready" /> }}
            />
          </div>
        </div>
        <div className="act">
          <Link className="btn" to="/auditor">
            {t("dashboard.export")}
          </Link>
          <Link className="btn primary" to="/frameworks">
            {t("dashboard.addFramework")}
          </Link>
        </div>
      </div>
      <div className="dash-split">
        <div className="card">
          <h3>
            {t("dashboard.readiness")}
            <Link className="r" to="/frameworks">
              {t("dashboard.viewAll")}
            </Link>
          </h3>
          <div className="fw-row">
            {data.frameworks.map((framework) => (
              <Link
                key={framework.key}
                className="fw"
                to={`/frameworks/${framework.key}`}
                style={{ textDecoration: "none", color: "inherit" }}
              >
                <Ring value={framework.percent} tone={ringTone(framework.percent)} />
                <b>{framework.name}</b>
                <small>
                  {t("dashboard.controlsProgress", {
                    passing: framework.passing,
                    total: framework.total,
                  })}
                </small>
              </Link>
            ))}
          </div>
        </div>
        <div className="card audit-card">
          <div className="audit-kicker">{t("dashboard.upcoming")}</div>
          {data.audit ? (
            <>
              <div style={{ fontSize: 18, fontWeight: 700, margin: "6px 0 2px" }}>
                {data.audit.name}
              </div>
              <div style={{ opacity: 0.85 }}>
                {data.audit.firm} · {formatDate(data.audit.fieldworkOn)}
              </div>
              <div className="audit-stats">
                <div className="stat">
                  <b>{daysUntil(data.audit.fieldworkOn)}</b>
                  <small>{t("dashboard.days")}</small>
                </div>
                <div className="stat">
                  <b>{data.audit.openItems}</b>
                  <small>{t("dashboard.openItems")}</small>
                </div>
                <div className="stat">
                  <b>{data.audit.samplesDue}</b>
                  <small>{t("dashboard.samplesDue")}</small>
                </div>
              </div>
            </>
          ) : (
            <div className="empty">{t("dashboard.noAudit")}</div>
          )}
        </div>
      </div>
      <div className="dash-split even">
        <div className="card">
          <h3>{t("dashboard.health")}</h3>
          {data.history.length > 0 && (
            <div className="trend" aria-label={t("dashboard.history")}>
              {data.history.map((point) => {
                const passing = Number(point.passing);
                const failing = Number(point.failing);
                const total = Math.max(1, passing + failing);
                return (
                  <div className="trend-col" key={point.weekOf} title={point.weekOf}>
                    <i className="fail" style={{ height: `${(failing / total) * 48}px` }} />
                    <i className="pass" style={{ height: `${(passing / total) * 48}px` }} />
                  </div>
                );
              })}
            </div>
          )}
          <div className="health-bars" style={{ marginTop: 12 }}>
            {health.map(([key, count]) => (
              <div className="line" key={key}>
                <span>{t(`dashboard.${key}`)}</span>
                <div
                  className="bar"
                  role="progressbar"
                  aria-label={t(`dashboard.${key}`)}
                  aria-valuenow={count}
                  aria-valuemin={0}
                  aria-valuemax={max}
                >
                  <i style={{ width: `${(count / max) * 100}%` }} />
                </div>
                <b>{count}</b>
              </div>
            ))}
          </div>
        </div>
        <div className="card">
          <h3>
            {t("dashboard.integrations")}
            <Link className="r" to="/integrations">
              {t("nav.integrations")}
            </Link>
          </h3>
          <IntegrationHealth tenantId={tenantId} />
          <div className="notice">
            {t("shell.deviceAgent")}: {t("shell.devices", { count: Number(data.devicesReporting) })}
          </div>
        </div>
      </div>
      <div className="card" style={{ marginTop: 14 }}>
        <h3>{t("dashboard.dueTitle")}</h3>
        {data.dueItems.length === 0 ? (
          <div className="empty">{t("dashboard.dueEmpty")}</div>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>{t("dashboard.test")}</th>
                  <th>{t("dashboard.due")}</th>
                  <th>{t("controls.status")}</th>
                </tr>
              </thead>
              <tbody>
                {data.dueItems.slice(0, 8).map((item) => (
                  <tr key={`${item.kind}-${item.title}-${item.dueOn}`}>
                    <td>
                      <Link to={item.href}>{item.title}</Link>
                      <div className="id">{t(`dashboard.dueKinds.${item.kind}`)}</div>
                    </td>
                    <td className="m">{formatDate(item.dueOn)}</td>
                    <td>
                      {item.overdue ? (
                        <Pill status="fail">{t("dashboard.overdue")}</Pill>
                      ) : (
                        <Pill status="warn">{t("dashboard.dueSoon")}</Pill>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            {data.dueItems.length > 8 && (
              <p className="m">{t("dashboard.moreDue", { count: data.dueItems.length - 8 })}</p>
            )}
          </div>
        )}
      </div>
      <div className="card" style={{ marginTop: 14 }}>
        <h3>
          {t("dashboard.failingTests")}
          <Link className="r" to="/controls">
            {t("dashboard.viewAll")}
          </Link>
        </h3>
        {data.failingTests.length === 0 ? (
          <div className="empty">{t("dashboard.noFailures")}</div>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>{t("dashboard.test")}</th>
                  <th>{t("dashboard.source")}</th>
                  <th>{t("dashboard.frameworks")}</th>
                  <th>{t("controls.status")}</th>
                </tr>
              </thead>
              <tbody>
                {data.failingTests.map((test) => (
                  <tr key={test.testId}>
                    <td>
                      <div className="t">{test.testName}</div>
                      <div className="id">{test.controlCode}</div>
                    </td>
                    <td className="m">{test.detail || "—"}</td>
                    <td>
                      {test.requirementCodes.map((code) => (
                        <span className="tag" key={code}>
                          {code}
                        </span>
                      ))}
                    </td>
                    <td>
                      <Pill status="fail">{t("status.fail")}</Pill>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </>
  );
}
