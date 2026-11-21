import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useOutletContext, useSearchParams } from "react-router-dom";
import { ApiError, api, type Workspace } from "../../shared/api/client";
import { formatDate, formatWhen, Pill } from "../../shared/ui/ui";
import { VendorReviewEditor, type RichVendor } from "./VendorReviewEditor";
import {
  riskTone,
  type VendorAssessment,
  type VendorDiscovery,
  type VendorDiscoveryScanResult,
  type VendorReminderSyncResult,
  type VendorReviewDocument,
  type VendorRiskRow,
} from "./vendorRiskTypes";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

const tabKeys = ["inventory", "discovery", "questionnaires", "documents"] as const;
type TabKey = (typeof tabKeys)[number];

export function VendorsPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [params, setParams] = useSearchParams();
  const tab: TabKey = tabKeys.includes(params.get("tab") as TabKey)
    ? (params.get("tab") as TabKey)
    : "inventory";
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const canWrite = Boolean(workspace?.canWrite);

  async function syncReminders() {
    if (!tenantId) return;
    setError(null);
    try {
      const result = await api<VendorReminderSyncResult>("/api/v1/vendor-risk/reminders/sync", {
        method: "POST",
        tenantId,
      });
      setNotice(t("vendorRisk.remindersSynced", { count: result.created }));
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("vendorsPage.title")}</h1>
          <div className="sub">{t("vendorsPage.sub")}</div>
        </div>
        {canWrite && (
          <button className="btn" type="button" onClick={() => void syncReminders()}>
            {t("vendorRisk.syncReminders")}
          </button>
        )}
      </div>
      {error && (
        <div className="error" role="alert">
          {error === "forbidden" ? t("errors.forbidden") : `${t("errors.load")} (${error})`}
        </div>
      )}
      {notice && (
        <div className="notice" role="status">
          {notice}
        </div>
      )}
      <div className="tabs" role="tablist" aria-label={t("vendorsPage.title")}>
        {tabKeys.map((key) => (
          <button
            key={key}
            type="button"
            role="tab"
            aria-selected={tab === key}
            className={`tab ${tab === key ? "on" : ""}`}
            onClick={() => setParams(key === "inventory" ? {} : { tab: key })}
          >
            {t(`vendorRisk.tabs.${key}`)}
          </button>
        ))}
      </div>
      {tenantId && tab === "inventory" && (
        <InventoryTab tenantId={tenantId} canWrite={canWrite} onError={setError} />
      )}
      {tenantId && tab === "discovery" && (
        <DiscoveryTab
          tenantId={tenantId}
          canWrite={canWrite}
          onError={setError}
          onNotice={setNotice}
        />
      )}
      {tenantId && tab === "questionnaires" && (
        <QuestionnairesTab tenantId={tenantId} canWrite={canWrite} onError={setError} />
      )}
      {tenantId && tab === "documents" && (
        <DocumentsTab tenantId={tenantId} canWrite={canWrite} onError={setError} />
      )}
    </>
  );
}

type TabProps = { tenantId: string; canWrite: boolean; onError: (code: string | null) => void };

function errorCode(caught: unknown) {
  return caught instanceof ApiError ? caught.code : "load";
}

function InventoryTab({ tenantId, canWrite, onError }: TabProps) {
  const { t } = useTranslation();
  const [rows, setRows] = useState<VendorRiskRow[] | null>(null);
  const [legacy, setLegacy] = useState<RichVendor[]>([]);
  const [editing, setEditing] = useState<RichVendor | null>(null);

  const load = () => {
    void api<VendorRiskRow[]>("/api/v1/vendor-risk/inventory", { tenantId })
      .then(setRows)
      .catch((caught: unknown) => onError(errorCode(caught)));
    void api<RichVendor[]>("/api/v1/vendors", { tenantId })
      .then(setLegacy)
      .catch(() => setLegacy([]));
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  return (
    <>
      <div className="card">
        {!rows ? null : rows.length === 0 ? (
          <div className="empty">{t("vendorsPage.empty")}</div>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>{t("vendorsPage.name")}</th>
                  <th>{t("vendorRisk.tier")}</th>
                  <th>{t("vendorRisk.owner")}</th>
                  <th>{t("vendorRisk.renewal")}</th>
                  <th>{t("vendorRisk.nextReview")}</th>
                  <th>{t("vendorRisk.questionnaire")}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => {
                  const legacyRow = legacy.find((item) => item.id === row.id);
                  return (
                    <tr key={row.id}>
                      <td>
                        <Link to={`/vendors/${row.id}`}>
                          <b>{row.name}</b>
                        </Link>
                        <div className="id">{row.service}</div>
                        <div className="id">
                          {row.dataCategories.join(", ")}
                          {row.subprocessor ? ` · ${t("vendorRisk.subprocessor")}` : ""}
                        </div>
                        {legacyRow && (
                          <button
                            className="linkish"
                            type="button"
                            onClick={() => setEditing(legacyRow)}
                          >
                            {t("vendorRisk.quickReview")}
                          </button>
                        )}
                      </td>
                      <td>
                        <Pill status={riskTone(row.tier)}>
                          {t(`vendorRisk.tiers.${row.tier}`, { defaultValue: row.tier })}
                        </Pill>
                        <div className="id">
                          {t("vendorRisk.inherent")} {row.inherentScore}
                        </div>
                      </td>
                      <td>
                        {row.ownerName || "—"}
                        {row.ownerEmail && <div className="id">{row.ownerEmail}</div>}
                      </td>
                      <td>
                        {row.contractRenewalOn ? formatDate(row.contractRenewalOn) : "—"}
                        <div>
                          <Pill status={riskTone(row.renewalState)}>
                            {t(`vendorRisk.renewalStates.${row.renewalState}`, {
                              defaultValue: row.renewalState,
                            })}
                          </Pill>
                        </div>
                      </td>
                      <td>
                        {formatDate(row.nextReviewOn)}
                        <div>
                          <Pill status={riskTone(row.reviewStatus)}>
                            {t(`vendorRisk.reviewStates.${row.reviewStatus}`, {
                              defaultValue: row.reviewStatus,
                            })}
                          </Pill>
                        </div>
                        <div className="id">
                          {t("vendorRisk.cadence", { count: row.reviewCadenceMonths })}
                        </div>
                      </td>
                      <td>
                        {row.latestAssessmentStatus ? (
                          <Pill status={riskTone(row.latestAssessmentStatus)}>
                            {t(`vendorRisk.assessmentStates.${row.latestAssessmentStatus}`, {
                              defaultValue: row.latestAssessmentStatus,
                            })}
                          </Pill>
                        ) : (
                          "—"
                        )}
                        {row.latestAssessmentScore != null && (
                          <div className="id">
                            {t("vendorRisk.score")} {row.latestAssessmentScore}
                          </div>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
      {editing && (
        <div className="card" style={{ marginTop: 14 }}>
          <VendorReviewEditor
            key={editing.id}
            vendor={editing}
            tenantId={tenantId}
            canWrite={canWrite}
            onSaved={() => {
              setEditing(null);
              load();
            }}
          />
        </div>
      )}
    </>
  );
}

function DiscoveryTab({
  tenantId,
  canWrite,
  onError,
  onNotice,
}: TabProps & { onNotice: (value: string | null) => void }) {
  const { t } = useTranslation();
  const [rows, setRows] = useState<VendorDiscovery[] | null>(null);
  const [busy, setBusy] = useState(false);

  const load = () => {
    void api<VendorDiscovery[]>("/api/v1/vendor-risk/discovery", { tenantId })
      .then(setRows)
      .catch((caught: unknown) => onError(errorCode(caught)));
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  async function scan() {
    setBusy(true);
    onError(null);
    try {
      const result = await api<VendorDiscoveryScanResult>("/api/v1/vendor-risk/discovery/scan", {
        method: "POST",
        tenantId,
      });
      onNotice(t("vendorRisk.scanned", { found: result.found, added: result.added }));
      setRows(result.items);
    } catch (caught: unknown) {
      onError(errorCode(caught));
    } finally {
      setBusy(false);
    }
  }

  async function decide(id: string, action: "accept" | "ignore") {
    onError(null);
    try {
      await api(`/api/v1/vendor-risk/discovery/${id}/${action}`, { method: "POST", tenantId });
      load();
    } catch (caught: unknown) {
      onError(errorCode(caught));
    }
  }

  return (
    <div className="card">
      <h3>
        {t("vendorRisk.discoveryTitle")}
        {canWrite && (
          <span className="r">
            <button className="btn" type="button" disabled={busy} onClick={() => void scan()}>
              {t("vendorRisk.scan")}
            </button>
          </span>
        )}
      </h3>
      <p className="m">{t("vendorRisk.discoverySub")}</p>
      {!rows ? null : rows.length === 0 ? (
        <div className="empty">{t("vendorRisk.discoveryEmpty")}</div>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{t("vendorsPage.name")}</th>
                <th>{t("vendorRisk.source")}</th>
                <th>{t("vendorsPage.criticality")}</th>
                <th>{t("vendorRisk.status")}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.id}>
                  <td>
                    <b>{row.name}</b>
                    <div className="id">{row.detail}</div>
                    <div className="id">{row.dataCategories.join(", ")}</div>
                  </td>
                  <td>{t(`vendorRisk.sources.${row.source}`, { defaultValue: row.source })}</td>
                  <td>
                    {t(`vendorsPage.criticalities.${row.suggestedCriticality}`, {
                      defaultValue: row.suggestedCriticality,
                    })}
                  </td>
                  <td>
                    <Pill status={riskTone(row.status)}>
                      {t(`vendorRisk.discoveryStates.${row.status}`, { defaultValue: row.status })}
                    </Pill>
                    {row.decidedBy && <div className="id">{row.decidedBy}</div>}
                  </td>
                  <td>
                    {row.status === "New" && canWrite ? (
                      <>
                        <button
                          className="btn primary"
                          type="button"
                          onClick={() => void decide(row.id, "accept")}
                        >
                          {t("vendorRisk.accept")}
                        </button>{" "}
                        <button
                          className="btn"
                          type="button"
                          onClick={() => void decide(row.id, "ignore")}
                        >
                          {t("vendorRisk.ignore")}
                        </button>
                      </>
                    ) : row.vendorId ? (
                      <Link to={`/vendors/${row.vendorId}`}>{t("vendorRisk.openVendor")}</Link>
                    ) : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function QuestionnairesTab({ tenantId, canWrite, onError }: TabProps) {
  const { t } = useTranslation();
  const [rows, setRows] = useState<VendorAssessment[] | null>(null);

  const load = () => {
    void api<VendorAssessment[]>("/api/v1/vendor-risk/assessments", { tenantId })
      .then(setRows)
      .catch((caught: unknown) => onError(errorCode(caught)));
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  async function act(id: string, path: string, body?: unknown) {
    onError(null);
    try {
      await api(`/api/v1/vendor-risk/assessments/${id}/${path}`, {
        method: "POST",
        tenantId,
        body,
      });
      load();
    } catch (caught: unknown) {
      onError(errorCode(caught));
    }
  }

  return (
    <div className="card">
      <p className="m">{t("vendorRisk.questionnairesSub")}</p>
      {!rows ? null : rows.length === 0 ? (
        <div className="empty">{t("vendorRisk.questionnairesEmpty")}</div>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{t("vendorsPage.name")}</th>
                <th>{t("vendorRisk.status")}</th>
                <th>{t("vendorRisk.progress")}</th>
                <th>{t("vendorRisk.score")}</th>
                <th>{t("vendorRisk.expires")}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.id}>
                  <td>
                    <Link to={`/vendors/${row.vendorId}`}>
                      <b>{row.vendorName}</b>
                    </Link>
                    <div className="id">{row.templateName}</div>
                    <div className="id">
                      {row.contactName} · {row.contactEmail}
                    </div>
                  </td>
                  <td>
                    <Pill status={riskTone(row.status)}>
                      {t(`vendorRisk.assessmentStates.${row.status}`, { defaultValue: row.status })}
                    </Pill>
                  </td>
                  <td className="m">
                    {row.answered}/{row.total}
                  </td>
                  <td>
                    {row.score ?? "—"}
                    {row.rating && (
                      <div>
                        <Pill status={riskTone(row.rating)}>
                          {t(`vendorRisk.ratings.${row.rating}`, { defaultValue: row.rating })}
                        </Pill>
                      </div>
                    )}
                    {row.flagged.length > 0 && (
                      <div className="id">
                        {t("vendorRisk.flagged")}: {row.flagged.join(", ")}
                      </div>
                    )}
                  </td>
                  <td className="m">{formatWhen(row.expiresAt)}</td>
                  <td>
                    {canWrite && row.status === "Submitted" && (
                      <>
                        <button
                          className="btn primary"
                          type="button"
                          onClick={() => void act(row.id, "review", { decision: "Approve" })}
                        >
                          {t("vendorRisk.approve")}
                        </button>{" "}
                        <button
                          className="btn"
                          type="button"
                          onClick={() => void act(row.id, "review", { decision: "Reject" })}
                        >
                          {t("vendorRisk.reject")}
                        </button>{" "}
                        <button
                          className="btn"
                          type="button"
                          onClick={() => void act(row.id, "review", { decision: "Reopen" })}
                        >
                          {t("vendorRisk.reopen")}
                        </button>
                      </>
                    )}
                    {canWrite && (row.status === "Sent" || row.status === "InProgress") && (
                      <button
                        className="btn"
                        type="button"
                        onClick={() => void act(row.id, "revoke")}
                      >
                        {t("vendorRisk.revokeLink")}
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function DocumentsTab({ tenantId, canWrite, onError }: TabProps) {
  const { t } = useTranslation();
  const [rows, setRows] = useState<VendorReviewDocument[] | null>(null);

  const load = () => {
    void api<VendorReviewDocument[]>("/api/v1/vendor-risk/documents", { tenantId })
      .then(setRows)
      .catch((caught: unknown) => onError(errorCode(caught)));
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  return (
    <div className="card">
      <p className="m">{t("vendorRisk.documentsSub")}</p>
      {!rows ? null : rows.length === 0 ? (
        <div className="empty">{t("vendorRisk.documentsEmpty")}</div>
      ) : (
        rows.map((row) => (
          <VendorDocumentCard
            key={row.id}
            document={row}
            tenantId={tenantId}
            canWrite={canWrite}
            onChanged={load}
            onError={onError}
            showVendor
          />
        ))
      )}
    </div>
  );
}

export function VendorDocumentCard({
  document,
  tenantId,
  canWrite,
  onChanged,
  onError,
  showVendor = false,
}: {
  document: VendorReviewDocument;
  tenantId: string;
  canWrite: boolean;
  onChanged: () => void;
  onError: (code: string | null) => void;
  showVendor?: boolean;
}) {
  const { t } = useTranslation();
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);

  async function run(path: string, body?: unknown) {
    setBusy(true);
    onError(null);
    try {
      await api(`/api/v1/vendor-risk/documents/${document.id}/${path}`, {
        method: "POST",
        tenantId,
        body,
      });
      onChanged();
    } catch (caught: unknown) {
      onError(errorCode(caught));
    } finally {
      setBusy(false);
    }
  }

  return (
    <article className="card" style={{ marginTop: 10 }}>
      <h3>
        {document.fileName}
        <span className="r">
          <Pill status={document.signedOffAt ? "pass" : document.summarizedAt ? "warn" : "info"}>
            {document.signedOffAt
              ? t("vendorRisk.signedOff")
              : document.summarizedAt
                ? t("vendorRisk.awaitingSignOff")
                : t("vendorRisk.notSummarized")}
          </Pill>
        </span>
      </h3>
      <div className="m">
        {showVendor && (
          <>
            <Link to={`/vendors/${document.vendorId}`}>{document.vendorName}</Link>
            {" · "}
          </>
        )}
        {t(`vendorRisk.kinds.${document.kind}`, { defaultValue: document.kind })} ·{" "}
        {Math.max(1, Math.round(document.sizeBytes / 1024))} KB · {document.uploadedBy} ·{" "}
        {formatWhen(document.uploadedAt)}
      </div>
      <div className="id">{document.sha256.slice(0, 16)}</div>
      {document.summarizedAt && (
        <div className="kv">
          <div>
            <small>{t("vendorRisk.reportPeriod")}</small>
            {document.reportPeriod || "—"}
          </div>
          <div>
            <small>{t("vendorRisk.opinion")}</small>
            {document.opinion || "—"}
          </div>
          <div>
            <small>{t("vendorRisk.summaryMode")}</small>
            {t(`vendorRisk.summaryModes.${document.summaryMode}`, {
              defaultValue: document.summaryMode,
            })}
          </div>
        </div>
      )}
      {document.summaryText && <p>{document.summaryText}</p>}
      {document.exceptions.length > 0 && (
        <>
          <b>{t("vendorRisk.exceptions")}</b>
          <ul>
            {document.exceptions.map((item) => (
              <li key={item}>{item}</li>
            ))}
          </ul>
        </>
      )}
      {document.cuecs.length > 0 && (
        <>
          <b>{t("vendorRisk.cuecs")}</b>
          <ul>
            {document.cuecs.map((item) => (
              <li key={item}>{item}</li>
            ))}
          </ul>
        </>
      )}
      {document.signedOffAt && (
        <div className="m">
          {t("vendorRisk.signedOffBy", {
            name: document.signedOffBy,
            when: formatWhen(document.signedOffAt),
          })}
          {document.signOffNote ? ` · ${document.signOffNote}` : ""}
        </div>
      )}
      {canWrite && !document.signedOffAt && (
        <div className="stack">
          <button
            className="btn"
            type="button"
            disabled={busy}
            onClick={() => void run("summarize")}
          >
            {document.summarizedAt ? t("vendorRisk.resummarize") : t("vendorRisk.summarize")}
          </button>
          {document.summarizedAt && (
            <>
              <input
                aria-label={t("vendorRisk.signOffNote")}
                placeholder={t("vendorRisk.signOffNote")}
                value={note}
                onChange={(event) => setNote(event.target.value)}
              />
              <button
                className="btn primary"
                type="button"
                disabled={busy}
                onClick={() => void run("sign-off", { note })}
              >
                {t("vendorRisk.signOff")}
              </button>
            </>
          )}
        </div>
      )}
    </article>
  );
}
