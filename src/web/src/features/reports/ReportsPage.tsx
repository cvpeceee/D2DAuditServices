import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { ApiError, api, download, type FrameworkListItem } from "../../shared/api/client";
import { ErrorState, LoadingState } from "../../shared/ui/states";
import { formatDate, formatWhen } from "../../shared/ui/ui";
import { useAuth } from "../auth/AuthContext";
import { reportKeys, type ReportSchedule } from "../insights/types";

const frequencies = ["Daily", "Weekly", "Monthly"] as const;

export function ReportsPage() {
  const { t } = useTranslation();
  const { tenantId, tenants } = useAuth();
  const role = tenants.find((item) => item.tenantId === tenantId)?.role;
  const canExport = role !== "Auditor" && role !== "Employee";
  const isAdmin = role === "Admin";
  const [frameworks, setFrameworks] = useState<FrameworkListItem[]>([]);
  const [framework, setFramework] = useState("");
  const [schedules, setSchedules] = useState<ReportSchedule[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const loadSchedules = useCallback(() => {
    if (!tenantId || !isAdmin) return;
    setError(null);
    void api<ReportSchedule[]>("/api/v1/reports/schedules", { tenantId })
      .then(setSchedules)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  }, [tenantId, isAdmin]);

  useEffect(() => {
    if (!tenantId || !canExport) return;
    void api<FrameworkListItem[]>("/api/v1/frameworks", { tenantId })
      .then(setFrameworks)
      .catch(() => setFrameworks([]));
    loadSchedules();
  }, [tenantId, canExport, loadSchedules]);

  if (!canExport) {
    return (
      <>
        <div className="head">
          <h1>{t("reports.title")}</h1>
        </div>
        <ErrorState code="forbidden" />
      </>
    );
  }

  async function exportReport(key: string, format: "csv" | "pdf") {
    setBusy(`${key}-${format}`);
    setNotice(null);
    const params = new URLSearchParams({ format });
    if (key === "framework" && framework) params.set("framework", framework);
    try {
      await download(`/api/v1/reports/${key}?${params.toString()}`, tenantId, {
        fallbackFileName: `${key}-report.${format}`,
      });
    } catch {
      setNotice(t("errors.load"));
    } finally {
      setBusy(null);
    }
  }

  function edit(key: string, patch: Partial<ReportSchedule>) {
    setSchedules((current) =>
      (current ?? []).map((item) => (item.reportKey === key ? { ...item, ...patch } : item)),
    );
  }

  async function save(item: ReportSchedule) {
    setBusy(`save-${item.reportKey}`);
    setNotice(null);
    try {
      const saved = await api<ReportSchedule>("/api/v1/reports/schedules", {
        method: "PUT",
        tenantId,
        body: {
          reportKey: item.reportKey,
          frequency: item.frequency,
          format: item.format,
          recipients: item.recipients,
          enabled: item.enabled,
        },
      });
      edit(item.reportKey, saved);
      setNotice(t("reports.scheduleSaved"));
    } catch (caught) {
      setNotice(
        caught instanceof ApiError && caught.status === 400
          ? t("reports.scheduleInvalid")
          : t("errors.load"),
      );
    } finally {
      setBusy(null);
    }
  }

  async function sendNow(key: string) {
    setBusy(`send-${key}`);
    setNotice(null);
    try {
      const result = await api<{ reportKey: string; deliveryId: string; sentAt: string }>(
        `/api/v1/reports/schedules/${key}/send`,
        { method: "POST", tenantId },
      );
      setNotice(t("reports.sent", { id: result.deliveryId }));
      loadSchedules();
    } catch (caught) {
      setNotice(
        caught instanceof ApiError && caught.status === 400
          ? t("reports.scheduleInvalid")
          : t("errors.load"),
      );
    } finally {
      setBusy(null);
    }
  }

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("reports.title")}</h1>
          <div className="sub">{t("reports.subtitle")}</div>
        </div>
      </div>
      {notice && (
        <div className="local-banner" role="status">
          {notice}
        </div>
      )}
      <div className="report-grid">
        {reportKeys.map((key) => (
          <section className="card" key={key} aria-labelledby={`report-${key}`}>
            <h3 id={`report-${key}`}>{t(`reports.keys.${key}`)}</h3>
            <p className="m">{t(`reports.descriptions.${key}`)}</p>
            {key === "framework" && (
              <label className="field">
                <span>{t("reports.framework")}</span>
                <select value={framework} onChange={(event) => setFramework(event.target.value)}>
                  <option value="">{t("reports.allFrameworks")}</option>
                  {frameworks.map((item) => (
                    <option key={item.key} value={item.key}>
                      {item.name}
                    </option>
                  ))}
                </select>
              </label>
            )}
            <div className="chips" style={{ marginTop: 10 }}>
              {(["csv", "pdf"] as const).map((format) => (
                <button
                  key={format}
                  className="btn"
                  type="button"
                  disabled={busy !== null}
                  aria-label={`${t(`reports.keys.${key}`)} ${format.toUpperCase()}`}
                  onClick={() => void exportReport(key, format)}
                >
                  {t(`reports.export.${format}`)}
                </button>
              ))}
            </div>
          </section>
        ))}
      </div>
      {isAdmin && (
        <div className="card" style={{ marginTop: 14 }}>
          <h3>{t("reports.schedules")}</h3>
          <p className="m">{t("reports.schedulesHint")}</p>
          {error ? (
            <ErrorState code={error} onRetry={loadSchedules} />
          ) : !schedules ? (
            <LoadingState rows={1} />
          ) : (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th scope="col">{t("reports.report")}</th>
                    <th scope="col">{t("reports.enabled")}</th>
                    <th scope="col">{t("reports.frequency")}</th>
                    <th scope="col">{t("reports.format")}</th>
                    <th scope="col">{t("reports.recipients")}</th>
                    <th scope="col">{t("reports.nextRun")}</th>
                    <th scope="col">
                      <span className="sr-only">{t("reports.actions")}</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {schedules.map((item) => {
                    const name = t(`reports.keys.${item.reportKey}`, {
                      defaultValue: item.reportKey,
                    });
                    return (
                      <tr key={item.reportKey}>
                        <th scope="row">{name}</th>
                        <td>
                          <input
                            type="checkbox"
                            aria-label={`${name}: ${t("reports.enabled")}`}
                            checked={item.enabled}
                            onChange={() => edit(item.reportKey, { enabled: !item.enabled })}
                          />
                        </td>
                        <td>
                          <select
                            aria-label={`${name}: ${t("reports.frequency")}`}
                            value={item.frequency}
                            onChange={(event) =>
                              edit(item.reportKey, { frequency: event.target.value })
                            }
                          >
                            {frequencies.map((value) => (
                              <option key={value} value={value}>
                                {t(`reports.frequencies.${value}`)}
                              </option>
                            ))}
                          </select>
                        </td>
                        <td>
                          <select
                            aria-label={`${name}: ${t("reports.format")}`}
                            value={item.format}
                            onChange={(event) =>
                              edit(item.reportKey, { format: event.target.value })
                            }
                          >
                            <option value="pdf">PDF</option>
                            <option value="csv">CSV</option>
                          </select>
                        </td>
                        <td>
                          <input
                            aria-label={`${name}: ${t("reports.recipients")}`}
                            placeholder="ciso@example.com, board@example.com"
                            value={item.recipients}
                            onChange={(event) =>
                              edit(item.reportKey, { recipients: event.target.value })
                            }
                          />
                        </td>
                        <td className="m">
                          {item.enabled && item.nextRunOn ? formatDate(item.nextRunOn) : "—"}
                          {item.lastSentAt && (
                            <div>
                              <small>
                                {t("reports.lastSent", { when: formatWhen(item.lastSentAt) })}
                              </small>
                            </div>
                          )}
                        </td>
                        <td>
                          <div className="chips">
                            <button
                              className="btn primary"
                              type="button"
                              disabled={busy !== null}
                              onClick={() => void save(item)}
                            >
                              {t("reports.save")}
                            </button>
                            <button
                              className="btn"
                              type="button"
                              disabled={busy !== null || !item.recipients.trim()}
                              onClick={() => void sendNow(item.reportKey)}
                            >
                              {t("reports.sendNow")}
                            </button>
                          </div>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
    </>
  );
}
