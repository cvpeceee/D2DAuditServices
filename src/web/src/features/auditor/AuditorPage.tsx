import { useEffect, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import {
  ApiError,
  api,
  type AuditPortalPage,
  type Job,
  type Workspace,
} from "../../shared/api/client";
import { formatDate, formatWhen, Pill } from "../../shared/ui/ui";

type ShellContext = { workspace: Workspace | null; tenantId: string | null; jobs: Job[] };

function requestTone(status: string) {
  if (status === "Accepted") return "pass";
  if (status === "Submitted") return "info";
  if (status === "Open") return "warn";
  return undefined;
}

export function AuditorPage() {
  const { t } = useTranslation();
  const { workspace, tenantId, jobs } = useOutletContext<ShellContext>();
  const [page, setPage] = useState<AuditPortalPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [title, setTitle] = useState("");
  const [population, setPopulation] = useState("");
  const [sampleSize, setSampleSize] = useState("");
  const [source, setSource] = useState("");
  const [dueOn, setDueOn] = useState("");
  const [note, setNote] = useState("");
  const [evidenceId, setEvidenceId] = useState("");
  const [response, setResponse] = useState("");
  const canAct = workspace?.role === "Auditor" || workspace?.canWrite === true;

  const load = () => {
    if (!tenantId) return;
    void api<AuditPortalPage>("/api/v1/audits", { tenantId })
      .then(setPage)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  };

  useEffect(() => {
    load();
    // Loaded once per workspace. Export progress arrives over the jobs hub.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  const job = jobs.find((item) => item.kind === "export");
  const running =
    job?.status === "Running" ||
    job?.status === "Queued" ||
    job?.status === "running" ||
    job?.status === "queued";
  const ready = job?.status === "Succeeded" || job?.status === "succeeded";

  async function exportPackage() {
    if (!tenantId) return;
    setBusy(true);
    setError(null);
    try {
      await api<string>("/api/v1/audits/package", { method: "POST", tenantId, body: {} });
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  async function download() {
    if (!tenantId || !job) return;
    const result = await fetch(`/api/v1/audits/packages/${job.id}`, {
      credentials: "include",
      headers: { "X-D2D-Request": "1", "X-Tenant-Id": tenantId },
    });
    if (!result.ok) {
      setError("load");
      return;
    }
    const blob = await result.blob();
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = "audit-package.zip";
    link.click();
    URL.revokeObjectURL(url);
  }

  async function createRequest(event: FormEvent) {
    event.preventDefault();
    if (!tenantId) return;
    setBusy(true);
    setError(null);
    try {
      await api("/api/v1/audits/requests", {
        method: "POST",
        tenantId,
        body: {
          title,
          population,
          sampleSize: sampleSize ? Number(sampleSize) : null,
          source,
          dueOn,
        },
      });
      setTitle("");
      setPopulation("");
      setSampleSize("");
      setSource("");
      setDueOn("");
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  async function respond(code: string) {
    if (!tenantId) return;
    setBusy(true);
    try {
      await api(`/api/v1/audits/requests/${code}/respond`, {
        method: "POST",
        tenantId,
        body: { body: note, evidenceId: evidenceId || null },
      });
      setNote("");
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  async function accept(code: string) {
    if (!tenantId) return;
    setBusy(true);
    try {
      await api(`/api/v1/audits/requests/${code}/accept`, { method: "POST", tenantId, body: {} });
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  async function answer(code: string) {
    if (!tenantId) return;
    setBusy(true);
    try {
      await api(`/api/v1/audits/findings/${code}/response`, {
        method: "POST",
        tenantId,
        body: { response },
      });
      setResponse("");
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  const engagement = page?.engagement;
  const tested =
    engagement && engagement.controlsTotal > 0
      ? Math.round((engagement.controlsPassing / engagement.controlsTotal) * 100)
      : 0;

  return (
    <>
      <div className="head">
        <div>
          <h1>{engagement?.name ?? t("auditorPage.title")}</h1>
          <div className="sub">
            {engagement
              ? `${engagement.scope} · ${formatDate(engagement.periodStart)} – ${formatDate(engagement.periodEnd)} · ${engagement.firmName}`
              : t("auditorPage.empty")}
          </div>
        </div>
        {canAct && (
          <div className="act">
            <button
              className="btn primary"
              type="button"
              disabled={busy || running}
              onClick={() => void exportPackage()}
            >
              {t("auditorPage.export")}
            </button>
          </div>
        )}
      </div>
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}
      {job && (
        <div className="card" style={{ marginBottom: 14 }}>
          <b>{t("auditorPage.progress")}</b>
          <div className="m">{job.currentStep}</div>
          <div
            className="bar"
            style={{ marginTop: 8 }}
            role="progressbar"
            aria-valuenow={job.percent ?? 0}
            aria-valuemin={0}
            aria-valuemax={100}
            aria-label={job.currentStep}
          >
            <i style={{ width: `${job.percent ?? 0}%` }} />
          </div>
          {ready && (
            <button
              className="btn"
              type="button"
              style={{ marginTop: 8 }}
              onClick={() => void download()}
            >
              {t("auditorPage.download")}
            </button>
          )}
        </div>
      )}
      {engagement && (
        <div className="grid" style={{ gridTemplateColumns: "repeat(4, 1fr)", marginBottom: 14 }}>
          <div className="card">
            <div className="m">{t("auditorPage.tested")}</div>
            <div style={{ fontSize: 22, fontWeight: 800, margin: "4px 0 8px" }}>
              {engagement.controlsPassing} / {engagement.controlsTotal}
            </div>
            <div className="bar">
              <i style={{ width: `${tested}%` }} />
            </div>
          </div>
          <div className="card">
            <div className="m">{t("auditorPage.requests")}</div>
            <div style={{ fontSize: 22, fontWeight: 800, margin: "4px 0 8px" }}>
              {engagement.requestsTotal}{" "}
              <small className="m">
                · {engagement.requestsOpen} {t("auditorPage.open")}
              </small>
            </div>
          </div>
          <div className="card">
            <div className="m">{t("auditorPage.findings")}</div>
            <div style={{ fontSize: 22, fontWeight: 800, margin: "4px 0 8px" }}>
              {engagement.findingsTotal}{" "}
              <small className="m">
                · {engagement.exceptions} {t("auditorPage.exceptions")}
              </small>
            </div>
          </div>
          <div className="card">
            <div className="m">{t("auditorPage.responseTime")}</div>
            <div style={{ fontSize: 22, fontWeight: 800, margin: "4px 0 8px" }}>
              {engagement.averageResponseHours === null
                ? "—"
                : t("auditorPage.hours", { count: engagement.averageResponseHours })}
            </div>
          </div>
        </div>
      )}
      <div className="grid" style={{ gridTemplateColumns: "1.5fr 1fr" }}>
        <div className="card" style={{ paddingBottom: 6 }}>
          <h3>{t("auditorPage.sampling")}</h3>
          <table>
            <thead>
              <tr>
                <th>{t("auditorPage.request")}</th>
                <th>{t("auditorPage.population")}</th>
                <th>{t("auditorPage.due")}</th>
                <th>{t("auditorPage.status")}</th>
              </tr>
            </thead>
            <tbody>
              {page?.requests.map((item) => (
                <tr key={item.code}>
                  <td>
                    <div className="t">
                      {item.code} · {item.title}
                    </div>
                    <div className="m">
                      {item.sampleSize
                        ? t("auditorPage.sampleOf", {
                            sample: item.sampleSize,
                            population: item.population,
                          })
                        : item.population}
                    </div>
                    {item.response && <div className="m">{item.response.body}</div>}
                    {workspace?.canWrite && item.status === "Open" && (
                      <form
                        className="stack"
                        onSubmit={(event) => {
                          event.preventDefault();
                          void respond(item.code);
                        }}
                      >
                        <input
                          aria-label={t("auditorPage.note")}
                          value={note}
                          onChange={(event) => setNote(event.target.value)}
                        />
                        <select
                          aria-label={t("auditorPage.attachment")}
                          value={evidenceId}
                          onChange={(event) => setEvidenceId(event.target.value)}
                        >
                          <option value="">{t("auditorPage.noAttachment")}</option>
                          {page.evidence.map((file) => (
                            <option key={file.id} value={file.id}>
                              {file.controlCode} · {file.title}
                            </option>
                          ))}
                        </select>
                        <button className="btn" type="submit" disabled={busy || !note.trim()}>
                          {t("auditorPage.respond")}
                        </button>
                      </form>
                    )}
                    {workspace?.role === "Auditor" && item.status === "Submitted" && (
                      <button
                        className="btn"
                        type="button"
                        disabled={busy}
                        onClick={() => void accept(item.code)}
                      >
                        {t("auditorPage.accept")}
                      </button>
                    )}
                  </td>
                  <td className="m">{item.source}</td>
                  <td className="m">{formatDate(item.dueOn)}</td>
                  <td>
                    <Pill status={requestTone(item.status)}>
                      {t(`auditorPage.statuses.${item.status}`, { defaultValue: item.status })}
                    </Pill>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {canAct && (
            <form
              className="stack"
              onSubmit={(event) => void createRequest(event)}
              style={{ marginTop: 12 }}
            >
              <h3>{t("auditorPage.newRequest")}</h3>
              <input
                aria-label={t("auditorPage.requestTitle")}
                value={title}
                onChange={(event) => setTitle(event.target.value)}
                required
              />
              <input
                aria-label={t("auditorPage.population")}
                value={population}
                onChange={(event) => setPopulation(event.target.value)}
                required
              />
              <input
                aria-label={t("auditorPage.sampleSize")}
                value={sampleSize}
                onChange={(event) => setSampleSize(event.target.value)}
                inputMode="numeric"
              />
              <input
                aria-label={t("auditorPage.source")}
                value={source}
                onChange={(event) => setSource(event.target.value)}
                required
              />
              <input
                aria-label={t("auditorPage.due")}
                type="date"
                value={dueOn}
                onChange={(event) => setDueOn(event.target.value)}
                required
              />
              <button className="btn" type="submit" disabled={busy}>
                {t("auditorPage.addRequest")}
              </button>
            </form>
          )}
        </div>
        <div className="card">
          <h3>{t("auditorPage.findings")}</h3>
          {page?.findings.map((item) => (
            <div
              key={item.code}
              style={{
                border: "1px solid var(--border)",
                borderRadius: 12,
                padding: 12,
                marginBottom: 10,
                borderInlineStart: `3px solid ${item.severity === "Exception" ? "var(--bad)" : "var(--warn)"}`,
              }}
            >
              <div className="row">
                <b>
                  {item.code} · {item.title}
                </b>
                <span style={{ marginInlineStart: "auto" }}>
                  <Pill status={item.severity === "Exception" ? "fail" : "warn"}>
                    {t(`auditorPage.severities.${item.severity}`, { defaultValue: item.severity })}
                  </Pill>
                </span>
              </div>
              <div className="m" style={{ margin: "6px 0" }}>
                {item.detail} {item.requirementCode}
              </div>
              <div className="m">
                {item.managementResponse
                  ? `${item.managementResponse}${item.responseByName ? ` · ${item.responseByName}` : ""}`
                  : t("auditorPage.noResponse")}
              </div>
              {(item.comments ?? []).length > 0 && (
                <ul>
                  {(item.comments ?? []).map((comment) => (
                    <li key={`${comment.at}-${comment.authorName}`}>
                      <b>{comment.authorName}</b> · {formatWhen(comment.at)} · {comment.body}
                    </li>
                  ))}
                </ul>
              )}
              {workspace?.canWrite && !item.managementResponse && (
                <form
                  className="stack"
                  onSubmit={(event) => {
                    event.preventDefault();
                    void answer(item.code);
                  }}
                >
                  <input
                    aria-label={t("auditorPage.management")}
                    value={response}
                    onChange={(event) => setResponse(event.target.value)}
                  />
                  <button className="btn" type="submit" disabled={busy || !response.trim()}>
                    {t("auditorPage.saveResponse")}
                  </button>
                </form>
              )}
            </div>
          ))}
        </div>
      </div>
    </>
  );
}
