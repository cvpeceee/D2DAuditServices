import { useEffect, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import {
  ApiError,
  api,
  type EvidenceItem,
  type EvidencePage as EvidencePayload,
  type Job,
  type VerifyReport,
  type Workspace,
} from "../../shared/api/client";
import { Pill } from "../../shared/ui/ui";

type ShellContext = { workspace: Workspace | null; tenantId: string | null; jobs: Job[] };

export function EvidencePage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [page, setPage] = useState<EvidencePayload | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [report, setReport] = useState<VerifyReport | null>(null);
  const [busy, setBusy] = useState(false);
  const [title, setTitle] = useState("");
  const [controlCode, setControlCode] = useState("");
  const [file, setFile] = useState<File | null>(null);

  const load = () => {
    if (!tenantId) return;
    void api<EvidencePayload>("/api/v1/evidence", { tenantId })
      .then(setPage)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  };

  useEffect(() => {
    load();
    // Reload when the workspace changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  async function verify() {
    if (!tenantId) return;
    setBusy(true);
    setError(null);
    try {
      setReport(await api<VerifyReport>("/api/v1/evidence/verify", { method: "POST", tenantId }));
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  async function upload(event: FormEvent) {
    event.preventDefault();
    if (!tenantId || !file || !title.trim() || !controlCode.trim()) return;
    setBusy(true);
    setError(null);
    try {
      const contentBase64 = await toBase64(file);
      await api<EvidenceItem>("/api/v1/evidence", {
        method: "POST",
        tenantId,
        body: { controlCode: controlCode.trim(), title: title.trim(), contentBase64 },
      });
      setTitle("");
      setControlCode("");
      setFile(null);
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  if (error && !page)
    return (
      <div className="error" role="alert">
        {t("errors.load")}
      </div>
    );
  if (!page) return <div className="skeleton lg" />;

  const verifiedPercent =
    page.summary.total === 0 ? 0 : Math.round((page.summary.verified / page.summary.total) * 100);

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("evidencePage.title")}</h1>
          <div className="sub">{t("evidencePage.sub")}</div>
        </div>
        <div className="act">
          <button className="btn" type="button" disabled={busy} onClick={() => void verify()}>
            {t("evidencePage.verify")}
          </button>
        </div>
      </div>
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}
      {report && (
        <div className="notice" role="status">
          {report.intact ? t("evidencePage.intact") : t("evidencePage.broken")} ·{" "}
          {t("evidencePage.checked", { count: report.checked })}
          {report.reason
            ? ` · ${t(`evidencePage.reason.${report.reason}`, { defaultValue: report.reason })}`
            : ""}
        </div>
      )}
      <div
        className="grid"
        style={{ gridTemplateColumns: "repeat(auto-fit, minmax(160px, 1fr))", marginBottom: 14 }}
      >
        <div className="card">
          <div className="m">{t("evidencePage.integrity")}</div>
          <div style={{ fontSize: 24, fontWeight: 800 }}>{verifiedPercent}%</div>
          <Pill status={page.summary.chainIntact ? "pass" : "fail"}>
            {page.summary.chainIntact ? t("evidencePage.intact") : t("evidencePage.broken")}
          </Pill>
        </div>
        <div className="card">
          <div className="m">{t("evidencePage.automated")}</div>
          <div style={{ fontSize: 24, fontWeight: 800 }}>{page.summary.automated}</div>
        </div>
        <div className="card">
          <div className="m">{t("evidencePage.manual")}</div>
          <div style={{ fontSize: 24, fontWeight: 800 }}>{page.summary.manual}</div>
        </div>
        <div className="card">
          <div className="m">{t("evidencePage.retention")}</div>
          <div style={{ fontSize: 24, fontWeight: 800 }}>
            {t("evidencePage.years", { count: page.summary.retentionYears })}
          </div>
        </div>
      </div>
      {page.items.length === 0 ? (
        <div className="empty">{t("evidencePage.empty")}</div>
      ) : (
        <div className="card" style={{ padding: "4px 8px" }}>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>{t("evidencePage.evidence")}</th>
                  <th>{t("evidencePage.control")}</th>
                  <th>{t("evidencePage.source")}</th>
                  <th>{t("evidencePage.collected")}</th>
                  <th>{t("evidencePage.integrity")}</th>
                  <th>{t("evidencePage.lock")}</th>
                </tr>
              </thead>
              <tbody>
                {page.items.map((item) => (
                  <tr key={item.id}>
                    <td>
                      <div className="t">{item.title}</div>
                      <div className="m">
                        {item.sha256
                          ? `sha256 ${item.sha256.slice(0, 8)}…${item.sha256.slice(-4)}`
                          : "sha256 —"}
                      </div>
                    </td>
                    <td className="id">{item.controlCode}</td>
                    <td className="m">{item.source}</td>
                    <td className="m">
                      {item.collectedAt ? new Date(item.collectedAt).toLocaleString() : "—"}
                    </td>
                    <td>
                      <Pill
                        status={
                          item.integrity === "verified"
                            ? "pass"
                            : item.integrity === "broken"
                              ? "fail"
                              : "info"
                        }
                      >
                        {item.integrity === "verified"
                          ? t("evidencePage.verified")
                          : t("evidencePage.notStored")}
                      </Pill>
                    </td>
                    <td className="m">
                      {item.lockedUntil
                        ? t("evidencePage.locked", {
                            date: new Date(item.lockedUntil).toLocaleDateString(),
                          })
                        : t("evidencePage.pending")}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
      {workspace?.canWrite && (
        <form
          className="card stack"
          style={{ marginTop: 14 }}
          onSubmit={(event) => void upload(event)}
        >
          <h3>{t("evidencePage.upload")}</h3>
          <div className="notice">{t("evidencePage.phi")}</div>
          <label className="field">
            <span>{t("evidencePage.controlCode")}</span>
            <input value={controlCode} onChange={(event) => setControlCode(event.target.value)} />
          </label>
          <label className="field">
            <span>{t("evidencePage.titleLabel")}</span>
            <input value={title} onChange={(event) => setTitle(event.target.value)} />
          </label>
          <label className="field">
            <span>{t("evidencePage.file")}</span>
            <input type="file" onChange={(event) => setFile(event.target.files?.[0] ?? null)} />
          </label>
          <button className="btn primary" type="submit" disabled={busy || !file}>
            {t("evidencePage.upload")}
          </button>
        </form>
      )}
    </>
  );
}

async function toBase64(file: File) {
  const bytes = new Uint8Array(await file.arrayBuffer());
  let binary = "";
  bytes.forEach((value) => {
    binary += String.fromCharCode(value);
  });
  return btoa(binary);
}
