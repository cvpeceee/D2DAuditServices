import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import { ApiError, api, type Workspace } from "../../shared/api/client";
import { formatDate, formatWhen, Pill } from "../../shared/ui/ui";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };
type Tab = "asset" | "vulnerability" | "ignored" | "history";

type Finding = {
  id: string;
  cve: string;
  title: string;
  severity: string;
  assetName: string;
  source: string;
  firstSeen: string;
  lastSeen: string;
  slaDue: string;
  fixAvailable: boolean;
  ignored: boolean;
  ignoreReason: string;
  overdue: boolean;
};

type Board = {
  sla: { criticalDays: number; highDays: number; mediumDays: number; lowDays: number };
  findings: Finding[];
  assets: { assetName: string; open: number; critical: number; high: number; items: Finding[] }[];
  ignored: Finding[];
  history: {
    cve: string;
    assetName: string;
    eventKind: string;
    dueOn: string;
    detail: string;
    at: string;
  }[];
};

function tone(severity: string) {
  if (severity === "Critical" || severity === "High") return "fail";
  if (severity === "Medium") return "warn";
  return "info";
}

export function VulnerabilitiesPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [tab, setTab] = useState<Tab>("vulnerability");
  const [board, setBoard] = useState<Board | null>(null);
  const [sla, setSla] = useState<Board["sla"] | null>(null);
  const [reason, setReason] = useState("");
  const [selected, setSelected] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [reports, setReports] = useState<
    { id: string; title: string; vendor: string; testedOn: string; findingCount: number }[]
  >([]);
  const [notice, setNotice] = useState<string | null>(null);
  const [pentest, setPentest] = useState({ title: "", vendor: "", testedOn: "", csv: "" });

  function load() {
    if (!tenantId) return;
    void api<Board>("/api/v1/vulnerabilities", { tenantId })
      .then((value) => {
        setBoard(value);
        setSla(value.sla);
      })
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
    void api<typeof reports>("/api/v1/vulnerabilities/pentests", { tenantId })
      .then(setReports)
      .catch(() => setReports([]));
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  async function ignore(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId || !selected) return;
    await api(`/api/v1/vulnerabilities/${selected}/ignore`, {
      method: "POST",
      tenantId,
      body: { reason },
    });
    setReason("");
    setSelected("");
    load();
  }

  async function saveSla(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId || !sla) return;
    await api("/api/v1/vulnerabilities/sla", { method: "POST", tenantId, body: sla });
    load();
  }

  const tabs: { id: Tab; label: string }[] = [
    { id: "vulnerability", label: t("vulnPage.byVulnerability") },
    { id: "asset", label: t("vulnPage.byAsset") },
    { id: "ignored", label: t("vulnPage.ignored") },
    { id: "history", label: t("vulnPage.history") },
  ];

  function table(rows: Finding[]) {
    return (
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>{t("vulnPage.cve")}</th>
              <th>{t("vulnPage.severity")}</th>
              <th>{t("vulnPage.asset")}</th>
              <th>{t("vulnPage.source")}</th>
              <th>{t("vulnPage.seen")}</th>
              <th>{t("vulnPage.due")}</th>
              <th>{t("vulnPage.fix")}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.id}>
                <td>
                  <b>{row.cve}</b>
                  <div className="id">{row.title}</div>
                  {workspace?.canWrite && !row.ignored && (
                    <button className="linkish" type="button" onClick={() => setSelected(row.id)}>
                      {t("vulnPage.ignore")}
                    </button>
                  )}
                  {row.ignoreReason && <div className="id">{row.ignoreReason}</div>}
                </td>
                <td>
                  <Pill status={tone(row.severity)}>
                    {t(`vulnPage.severities.${row.severity}`)}
                  </Pill>
                </td>
                <td>{row.assetName}</td>
                <td>{row.source}</td>
                <td>
                  {formatWhen(row.firstSeen)}
                  <div className="id">{formatWhen(row.lastSeen)}</div>
                </td>
                <td>
                  {formatDate(row.slaDue)}
                  {row.overdue && <Pill status="fail">{t("vulnPage.overdue")}</Pill>}
                </td>
                <td>{row.fixAvailable ? t("vulnPage.fixYes") : t("vulnPage.fixNo")}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    );
  }

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("vulnPage.title")}</h1>
          <div className="sub">{t("vulnPage.sub")}</div>
        </div>
        {workspace?.canWrite && (
          <button
            className="btn"
            type="button"
            onClick={() => {
              if (!tenantId) return;
              void api<{ imported: number }>("/api/v1/vulnerabilities/sync", {
                method: "POST",
                tenantId,
              }).then((result) => {
                setNotice(t("vulnPage.synced", { count: result.imported }));
                load();
              });
            }}
          >
            {t("vulnPage.sync")}
          </button>
        )}
      </div>
      {notice && (
        <div className="notice" role="status">
          {notice}
        </div>
      )}
      <section className="card">
        <h3>{t("vulnPage.pentests")}</h3>
        <ul>
          {reports.map((report) => (
            <li key={report.id}>
              <b>{report.title}</b> · {report.vendor} · {report.testedOn} ·{" "}
              {t("vulnPage.findingCount", { count: report.findingCount })}
            </li>
          ))}
        </ul>
        {workspace?.canWrite && (
          <form
            onSubmit={(event) => {
              event.preventDefault();
              if (!tenantId) return;
              void api("/api/v1/vulnerabilities/pentests", {
                method: "POST",
                tenantId,
                body: pentest,
              }).then(() => {
                setPentest({ title: "", vendor: "", testedOn: "", csv: "" });
                setNotice(t("vulnPage.pentestSaved"));
                load();
              });
            }}
          >
            <label className="field">
              <span>{t("vulnPage.pentestTitle")}</span>
              <input
                aria-label={t("vulnPage.pentestTitle")}
                value={pentest.title}
                onChange={(event) => setPentest({ ...pentest, title: event.target.value })}
              />
            </label>
            <label className="field">
              <span>{t("vulnPage.vendor")}</span>
              <input
                aria-label={t("vulnPage.vendor")}
                value={pentest.vendor}
                onChange={(event) => setPentest({ ...pentest, vendor: event.target.value })}
              />
            </label>
            <label className="field">
              <span>{t("vulnPage.csv")}</span>
              <textarea
                aria-label={t("vulnPage.csv")}
                value={pentest.csv}
                onChange={(event) => setPentest({ ...pentest, csv: event.target.value })}
              />
            </label>
            <button className="btn primary" type="submit">
              {t("vulnPage.uploadPentest")}
            </button>
          </form>
        )}
      </section>
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}
      <div className="tabs" role="tablist">
        {tabs.map((item) => (
          <button
            key={item.id}
            type="button"
            role="tab"
            aria-selected={tab === item.id}
            className={tab === item.id ? "on" : ""}
            onClick={() => setTab(item.id)}
          >
            {item.label}
          </button>
        ))}
      </div>
      <div className="card">
        {!board ? null : tab === "asset" ? (
          board.assets.map((group) => (
            <section key={group.assetName}>
              <h3>
                {group.assetName} · {t("vulnPage.openCount", { count: group.open })}
              </h3>
              {table(group.items)}
            </section>
          ))
        ) : tab === "ignored" ? (
          table(board.ignored)
        ) : tab === "history" ? (
          <ul>
            {board.history.map((item) => (
              <li key={`${item.cve}-${item.at}-${item.eventKind}`}>
                <b>{item.cve}</b> · {item.assetName} · {t(`vulnPage.events.${item.eventKind}`)} ·{" "}
                {formatDate(item.dueOn)}
                <div className="id">{item.detail}</div>
              </li>
            ))}
          </ul>
        ) : (
          table(board.findings)
        )}
      </div>
      {workspace?.canWrite && selected && (
        <form className="card" onSubmit={(event) => void ignore(event)}>
          <h3>{t("vulnPage.ignore")}</h3>
          <label className="field">
            <span>{t("vulnPage.reason")}</span>
            <input value={reason} onChange={(event) => setReason(event.target.value)} required />
          </label>
          <button className="btn" type="submit">
            {t("vulnPage.ignore")}
          </button>
        </form>
      )}
      {workspace?.canWrite && sla && (
        <form className="card" onSubmit={(event) => void saveSla(event)}>
          <h3>{t("vulnPage.sla")}</h3>
          <div className="split-fields">
            {(["criticalDays", "highDays", "mediumDays", "lowDays"] as const).map((key) => (
              <label key={key} className="field">
                <span>{t(`vulnPage.${key}`)}</span>
                <input
                  type="number"
                  min={1}
                  max={365}
                  value={sla[key]}
                  onChange={(event) => setSla({ ...sla, [key]: Number(event.target.value) })}
                />
              </label>
            ))}
          </div>
          <button className="btn" type="submit">
            {t("vulnPage.saveSla")}
          </button>
        </form>
      )}
    </>
  );
}
