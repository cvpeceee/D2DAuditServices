import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import { ApiError, api, type Workspace } from "../../shared/api/client";
import { formatDate, formatWhen, Pill } from "../../shared/ui/ui";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

type Incident = {
  id: string;
  code: string;
  title: string;
  severity: string;
  status: string;
  description: string;
  reporterName: string;
  reportedAt: string;
  slaDue: string;
  resolvedAt: string | null;
  rootCause: string;
  controlCode: string;
  withinSla: boolean;
  timeline: { kind: string; detail: string; authorName: string; at: string }[];
};

const severities = ["Critical", "High", "Medium", "Low"] as const;
const statuses = ["Open", "Investigating", "Resolved"] as const;

function tone(severity: string) {
  if (severity === "Critical" || severity === "High") return "fail";
  if (severity === "Medium") return "warn";
  return "info";
}

export function IncidentsPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [rows, setRows] = useState<Incident[] | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const [draft, setDraft] = useState({ title: "", severity: "Medium", description: "" });
  const [note, setNote] = useState("");
  const [cause, setCause] = useState("");
  const [error, setError] = useState<string | null>(null);

  function load() {
    if (!tenantId) return;
    void api<Incident[]>("/api/v1/incidents", { tenantId })
      .then(setRows)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  const current = rows?.find((item) => item.id === selected) ?? null;

  async function report(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId) return;
    await api("/api/v1/incidents", { method: "POST", tenantId, body: draft });
    setDraft({ title: "", severity: "Medium", description: "" });
    load();
  }

  async function update(status: string) {
    if (!tenantId || !current) return;
    await api(`/api/v1/incidents/${current.id}`, {
      method: "POST",
      tenantId,
      body: { status, rootCause: cause, note },
    });
    setNote("");
    load();
  }

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("incidentsPage.title")}</h1>
          <div className="sub">{t("incidentsPage.sub")}</div>
        </div>
      </div>
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}
      <div className="card">
        {!rows ? null : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>{t("incidentsPage.code")}</th>
                  <th>{t("incidentsPage.severity")}</th>
                  <th>{t("incidentsPage.status")}</th>
                  <th>{t("incidentsPage.sla")}</th>
                  <th>{t("incidentsPage.reporter")}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.id}>
                    <td>
                      <button
                        className="linkish"
                        type="button"
                        onClick={() => {
                          setSelected(row.id);
                          setCause(row.rootCause);
                        }}
                      >
                        <b>{row.code}</b>
                      </button>
                      <div className="id">{row.title}</div>
                    </td>
                    <td>
                      <Pill status={tone(row.severity)}>
                        {t(`incidentsPage.severities.${row.severity}`)}
                      </Pill>
                    </td>
                    <td>{t(`incidentsPage.statuses.${row.status}`)}</td>
                    <td>
                      {formatDate(row.slaDue)}
                      <div className="id">
                        {row.withinSla ? t("incidentsPage.inside") : t("incidentsPage.late")}
                      </div>
                    </td>
                    <td>
                      {row.reporterName}
                      <div className="id">{formatWhen(row.reportedAt)}</div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
      {current && (
        <div className="card">
          <h3>
            {current.code} · {current.title}
          </h3>
          <p>{current.description}</p>
          {current.rootCause && (
            <p className="m">
              {t("incidentsPage.cause")}: {current.rootCause}
            </p>
          )}
          {current.controlCode && <div className="id">{current.controlCode}</div>}
          <h3>{t("incidentsPage.timeline")}</h3>
          <ul>
            {current.timeline.map((item) => (
              <li key={`${item.at}-${item.kind}`}>
                <b>{t(`incidentsPage.kinds.${item.kind}`, { defaultValue: item.kind })}</b> ·{" "}
                {item.authorName} · {formatWhen(item.at)}
                <div className="id">{item.detail}</div>
              </li>
            ))}
          </ul>
          {workspace?.canWrite && (
            <form
              onSubmit={(event) => {
                event.preventDefault();
                void update(current.status);
              }}
            >
              <label className="field">
                <span>{t("incidentsPage.cause")}</span>
                <input value={cause} onChange={(event) => setCause(event.target.value)} />
              </label>
              <label className="field">
                <span>{t("incidentsPage.note")}</span>
                <input value={note} onChange={(event) => setNote(event.target.value)} />
              </label>
              <div className="act">
                {statuses.map((status) => (
                  <button
                    key={status}
                    className="btn"
                    type="button"
                    onClick={() => void update(status)}
                  >
                    {t(`incidentsPage.statuses.${status}`)}
                  </button>
                ))}
              </div>
            </form>
          )}
        </div>
      )}
      <form className="card" onSubmit={(event) => void report(event)}>
        <h3>{t("incidentsPage.report")}</h3>
        <div className="split-fields">
          <input
            aria-label={t("incidentsPage.titleField")}
            placeholder={t("incidentsPage.titleField")}
            value={draft.title}
            onChange={(event) => setDraft({ ...draft, title: event.target.value })}
            required
          />
          <select
            aria-label={t("incidentsPage.severity")}
            value={draft.severity}
            onChange={(event) => setDraft({ ...draft, severity: event.target.value })}
          >
            {severities.map((item) => (
              <option key={item} value={item}>
                {t(`incidentsPage.severities.${item}`)}
              </option>
            ))}
          </select>
        </div>
        <label className="field">
          <span>{t("incidentsPage.description")}</span>
          <textarea
            value={draft.description}
            onChange={(event) => setDraft({ ...draft, description: event.target.value })}
            required
          />
        </label>
        <button className="btn primary" type="submit">
          {t("incidentsPage.report")}
        </button>
      </form>
    </>
  );
}
