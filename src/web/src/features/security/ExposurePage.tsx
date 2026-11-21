import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useOutletContext } from "react-router-dom";
import { ApiError, api, type Workspace } from "../../shared/api/client";
import { formatWhen, Pill } from "../../shared/ui/ui";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

type ExposureItem = {
  id: string;
  provider: string;
  resourceName: string;
  resourceKind: string;
  region: string;
  cidr: string;
  port: number;
  protocol: string;
  service: string;
  severity: string;
  riskScore: number;
  controlCode: string;
  summary: string;
  remediationTerraform: string;
  remediationCli: string;
  status: string;
  resolutionNote: string;
  detectedAt: string;
  resolvedAt: string | null;
};

type ExposureBoard = {
  openCount: number;
  criticalCount: number;
  highCount: number;
  publicBuckets: number;
  openPortsCount: number;
  findings: ExposureItem[];
};

function tone(severity: string) {
  if (severity === "Critical" || severity === "High") return "fail";
  if (severity === "Medium") return "warn";
  return "info";
}

export function ExposurePage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [board, setBoard] = useState<ExposureBoard | null>(null);
  const [provider, setProvider] = useState("all");
  const [statusFilter, setStatusFilter] = useState("Open");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [notesById, setNotesById] = useState<Record<string, string>>({});
  const [copiedKey, setCopiedKey] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  function load() {
    if (!tenantId) return;
    void api<ExposureBoard>("/api/v1/security/exposure", { tenantId })
      .then((value) => {
        setBoard(value);
        setError(null);
      })
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  async function scanNow() {
    if (!tenantId) return;
    try {
      const updated = await api<ExposureBoard>("/api/v1/security/exposure/scan", {
        method: "POST",
        tenantId,
      });
      setBoard(updated);
      setNotice(t("exposurePage.scanned", { count: updated.openCount }));
      setError(null);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.code : "save");
    }
  }

  async function resolveFinding(event: React.FormEvent, id: string) {
    event.preventDefault();
    if (!tenantId) return;
    const note = (notesById[id] ?? "").trim();
    if (!note) return;
    try {
      await api(`/api/v1/security/exposure/${id}/resolve`, {
        method: "POST",
        tenantId,
        body: { note },
      });
      setNotesById((current) => ({ ...current, [id]: "" }));
      setNotice(t("exposurePage.resolvedNotice"));
      load();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.code : "save");
    }
  }

  function copySnippet(key: string, text: string) {
    if (!navigator.clipboard) return;
    navigator.clipboard.writeText(text).then(
      () => {
        setCopiedKey(key);
        window.setTimeout(() => setCopiedKey(null), 1800);
      },
      () => setCopiedKey(null),
    );
  }

  const visible = (board?.findings ?? []).filter(
    (item) =>
      (provider === "all" || item.provider === provider) &&
      (statusFilter === "All" || item.status === statusFilter),
  );
  const selected = visible.find((item) => item.id === selectedId) ?? visible[0] ?? null;

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("exposurePage.title")}</h1>
          <div className="sub">{t("exposurePage.sub")}</div>
        </div>
        {workspace?.canWrite && (
          <button className="btn primary" type="button" onClick={() => void scanNow()}>
            {t("exposurePage.scanNow")}
          </button>
        )}
      </div>

      {notice && (
        <div className="notice" role="status">
          {notice}
        </div>
      )}
      {error && (
        <div className="error" role="alert">
          {t(error === "save" ? "errors.save" : "errors.load")}
        </div>
      )}

      {board && (
        <div className="kpis">
          <div className="kpi">
            <span>{t("exposurePage.openExposures")}</span>
            <b>{board.openCount}</b>
          </div>
          <div className="kpi">
            <span>{t("exposurePage.criticalCount")}</span>
            <b>{board.criticalCount}</b>
          </div>
          <div className="kpi">
            <span>{t("exposurePage.openPorts")}</span>
            <b>{board.openPortsCount}</b>
          </div>
          <div className="kpi">
            <span>{t("exposurePage.publicBuckets")}</span>
            <b>{board.publicBuckets}</b>
          </div>
        </div>
      )}

      <div className="tabs" role="tablist">
        {(["Open", "Resolved", "All"] as const).map((status) => (
          <button
            key={status}
            type="button"
            role="tab"
            aria-selected={statusFilter === status}
            className={statusFilter === status ? "on" : ""}
            onClick={() => setStatusFilter(status)}
          >
            {status}
          </button>
        ))}
        {(["all", "aws", "azure", "gcp"] as const).map((cloud) => (
          <button
            key={cloud}
            type="button"
            className={provider === cloud ? "on" : ""}
            onClick={() => setProvider(cloud)}
          >
            {cloud === "all" ? t("exposurePage.allClouds") : cloud.toUpperCase()}
          </button>
        ))}
      </div>

      <div className="card">
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{t("exposurePage.resource")}</th>
                <th>{t("exposurePage.cloud")}</th>
                <th>{t("exposurePage.portCidr")}</th>
                <th>{t("exposurePage.risk")}</th>
                <th>{t("exposurePage.control")}</th>
                <th>{t("exposurePage.status")}</th>
              </tr>
            </thead>
            <tbody>
              {visible.map((row) => (
                <tr
                  key={row.id}
                  onClick={() => setSelectedId(row.id)}
                  style={{ cursor: "pointer" }}
                >
                  <td>
                    <b>{row.resourceName}</b>
                    <div className="id">{row.summary}</div>
                  </td>
                  <td>
                    {row.provider.toUpperCase()}
                    <div className="id">{row.region}</div>
                  </td>
                  <td>
                    <code>
                      {row.service} :{row.port}/{row.protocol}
                    </code>
                    <div className="id">{row.cidr}</div>
                  </td>
                  <td>
                    <Pill status={tone(row.severity)}>{`${row.severity} (${row.riskScore})`}</Pill>
                  </td>
                  <td>
                    <Link to={`/controls/${encodeURIComponent(row.controlCode)}`}>
                      {row.controlCode}
                    </Link>
                  </td>
                  <td>
                    <Pill status={row.status === "Resolved" ? "pass" : "fail"}>{row.status}</Pill>
                    <div className="id">{formatWhen(row.detectedAt)}</div>
                  </td>
                </tr>
              ))}
              {visible.length === 0 && (
                <tr>
                  <td colSpan={6} className="m">
                    {t("exposurePage.empty")}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {selected && (
        <section className="card">
          <h3>
            {t("exposurePage.iacTitle")}: {selected.resourceName} ({selected.service}:
            {selected.port})
          </h3>
          <p className="m">{selected.summary}</p>
          <div style={{ display: "grid", gap: 12, marginTop: 12 }}>
            <div>
              <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 6 }}>
                <b>{t("exposurePage.terraformPatch")}</b>
                <button
                  type="button"
                  className="btn"
                  onClick={() => copySnippet(`tf-${selected.id}`, selected.remediationTerraform)}
                >
                  {copiedKey === `tf-${selected.id}`
                    ? t("exposurePage.copied")
                    : t("exposurePage.copy")}
                </button>
              </div>
              <pre
                style={{
                  padding: 12,
                  borderRadius: 8,
                  background: "var(--surface-2, var(--bg))",
                  overflowX: "auto",
                  fontSize: 12,
                }}
              >
                <code>{selected.remediationTerraform}</code>
              </pre>
            </div>
            <div>
              <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 6 }}>
                <b>{t("exposurePage.cliCommand")}</b>
                <button
                  type="button"
                  className="btn"
                  onClick={() => copySnippet(`cli-${selected.id}`, selected.remediationCli)}
                >
                  {copiedKey === `cli-${selected.id}`
                    ? t("exposurePage.copied")
                    : t("exposurePage.copy")}
                </button>
              </div>
              <pre
                style={{
                  padding: 12,
                  borderRadius: 8,
                  background: "var(--surface-2, var(--bg))",
                  overflowX: "auto",
                  fontSize: 12,
                }}
              >
                <code>{selected.remediationCli}</code>
              </pre>
            </div>
          </div>

          {selected.status === "Resolved" && selected.resolutionNote && (
            <div className="notice" style={{ marginTop: 12 }}>
              <b>{t("exposurePage.resolutionNote")}:</b> {selected.resolutionNote}
            </div>
          )}

          {workspace?.canWrite && selected.status === "Open" && (
            <form
              style={{ marginTop: 16 }}
              onSubmit={(event) => void resolveFinding(event, selected.id)}
            >
              <label className="field">
                <span>{t("exposurePage.resolvePrompt")}</span>
                <input
                  value={notesById[selected.id] ?? ""}
                  onChange={(event) =>
                    setNotesById((current) => ({ ...current, [selected.id]: event.target.value }))
                  }
                  placeholder={t("exposurePage.resolvePlaceholder")}
                  required
                />
              </label>
              <button className="btn primary" type="submit">
                {t("exposurePage.markResolved")}
              </button>
            </form>
          )}
        </section>
      )}
    </>
  );
}
