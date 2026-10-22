import { useEffect, useState } from "react";
import { Trans, useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { ApiError, api, type Dashboard } from "../../shared/api/client";
import { useAuth } from "../auth/AuthContext";
import { daysUntil, formatDate, Pill, Ring, Skeleton } from "../../shared/ui/ui";

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
  const [exportNote, setExportNote] = useState(false);

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
          <button className="btn" type="button" onClick={() => setExportNote(true)}>
            {t("dashboard.export")}
          </button>
          <Link className="btn primary" to="/frameworks">
            {t("dashboard.addFramework")}
          </Link>
        </div>
      </div>
      {exportNote && (
        <div className="notice" role="status">
          {t("dashboard.exportLater")}
        </div>
      )}
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
          <p className="m">{t("dashboard.history")}</p>
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
          <div className="empty">{t("dashboard.integrationsEmpty")}</div>
          <div className="notice">
            {t("shell.deviceAgent")}: {t("shell.noDevices")}
          </div>
        </div>
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
