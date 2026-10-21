import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { ApiError, api, type ActivityEvent } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { formatWhen, Skeleton } from "../components/ui";

export function ActivityPage() {
  const { t } = useTranslation();
  const { tenantId } = useAuth();
  const [rows, setRows] = useState<ActivityEvent[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!tenantId) return;
    let cancelled = false;
    void api<ActivityEvent[]>("/api/v1/activity", { tenantId })
      .then((value) => { if (!cancelled) setRows(value); })
      .catch((caught: unknown) => { if (!cancelled) setError(caught instanceof ApiError && caught.status === 403 ? "forbidden" : "load"); });
    return () => { cancelled = true; };
  }, [tenantId]);

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("activity.title")}</h1>
          <div className="sub">{t("activity.sub")}</div>
        </div>
      </div>
      {error && <div className="error" role="alert">{t(error === "forbidden" ? "errors.forbidden" : "errors.load")}</div>}
      {!rows && !error && <Skeleton className="lg" />}
      {rows && (
        <div className="card" style={{ padding: "4px 8px" }}>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>{t("activity.when")}</th>
                  <th>{t("activity.action")}</th>
                  <th>{t("activity.result")}</th>
                  <th>{t("activity.actor")}</th>
                  <th>{t("activity.detail")}</th>
                </tr>
              </thead>
              <tbody>
                {rows.length === 0 && <tr><td colSpan={5} className="empty">{t("activity.empty")}</td></tr>}
                {rows.map((row, index) => (
                  <tr key={`${row.at}-${index}`}>
                    <td className="m">{formatWhen(row.at)}</td>
                    <td className="t">{row.action}</td>
                    <td>{row.result}</td>
                    <td className="m">{row.actorEmail || "—"}</td>
                    <td className="m">{row.detail || row.ipAddress || "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </>
  );
}
