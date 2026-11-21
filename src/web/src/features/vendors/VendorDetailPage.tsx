import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useOutletContext, useParams } from "react-router-dom";
import { ApiError, api, type Workspace } from "../../shared/api/client";
import { formatDate, formatWhen, Pill } from "../../shared/ui/ui";
import { VendorDocumentCard } from "./VendorsPage";
import {
  documentKinds,
  maxDocumentBytes,
  readFileAsBase64,
  riskTone,
  type QuestionnaireTemplate,
  type SentVendorQuestionnaire,
  type VendorRiskDetail,
  type VendorRiskRow,
} from "./vendorRiskTypes";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

export function VendorDetailPage() {
  const { t } = useTranslation();
  const { id = "" } = useParams();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [detail, setDetail] = useState<VendorRiskDetail | null>(null);
  const [templates, setTemplates] = useState<QuestionnaireTemplate[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [portalLink, setPortalLink] = useState<string | null>(null);
  const canWrite = Boolean(workspace?.canWrite);

  const load = () => {
    if (!tenantId) return;
    void api<VendorRiskDetail>(`/api/v1/vendor-risk/vendors/${id}`, { tenantId })
      .then(setDetail)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  };

  useEffect(() => {
    load();
    if (tenantId) {
      void api<QuestionnaireTemplate[]>("/api/v1/vendor-risk/templates", { tenantId })
        .then(setTemplates)
        .catch(() => setTemplates([]));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId, id]);

  if (error === "not_found") {
    return (
      <div className="card">
        <div className="empty">{t("vendorRisk.notFound")}</div>
        <Link to="/vendors">{t("vendorRisk.back")}</Link>
      </div>
    );
  }

  const vendor = detail?.vendor;

  return (
    <>
      <div className="head">
        <div>
          <Link to="/vendors" className="m">
            ← {t("vendorRisk.back")}
          </Link>
          <h1>{vendor?.name ?? "…"}</h1>
          {vendor && (
            <div className="sub">
              {vendor.service} · {vendor.dataAccess}
            </div>
          )}
        </div>
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
      {portalLink && (
        <div className="notice" role="status">
          {t("vendorRisk.portalLinkOnce")} <code>{`${window.location.origin}${portalLink}`}</code>
        </div>
      )}
      {detail && vendor && tenantId && (
        <>
          <div className="kpis">
            <div className="card">
              <small>{t("vendorRisk.tier")}</small>
              <div>
                <Pill status={riskTone(vendor.tier)}>
                  {t(`vendorRisk.tiers.${vendor.tier}`, { defaultValue: vendor.tier })}
                </Pill>
              </div>
              <div className="id">
                {t("vendorRisk.inherent")} {vendor.inherentScore}
              </div>
            </div>
            <div className="card">
              <small>{t("vendorRisk.nextReview")}</small>
              <div>{formatDate(vendor.nextReviewOn)}</div>
              <Pill status={riskTone(vendor.reviewStatus)}>
                {t(`vendorRisk.reviewStates.${vendor.reviewStatus}`, {
                  defaultValue: vendor.reviewStatus,
                })}
              </Pill>
            </div>
            <div className="card">
              <small>{t("vendorRisk.renewal")}</small>
              <div>{vendor.contractRenewalOn ? formatDate(vendor.contractRenewalOn) : "—"}</div>
              <Pill status={riskTone(vendor.renewalState)}>
                {t(`vendorRisk.renewalStates.${vendor.renewalState}`, {
                  defaultValue: vendor.renewalState,
                })}
              </Pill>
            </div>
          </div>
          <ProfileForm
            key={`${vendor.id}-${vendor.ownerEmail}-${vendor.nextReviewOn}`}
            vendor={vendor}
            tenantId={tenantId}
            canWrite={canWrite}
            onSaved={() => {
              setNotice(t("vendorRisk.profileSaved"));
              load();
            }}
            onError={setError}
          />
          {canWrite && (
            <SendQuestionnaire
              vendor={vendor}
              templates={templates}
              tenantId={tenantId}
              onSent={(sent) => {
                setPortalLink(sent.portalPath);
                setNotice(
                  t("vendorRisk.sent", {
                    email: sent.assessment.contactEmail,
                    receipt: sent.assessment.deliveryReceipt,
                  }),
                );
                load();
              }}
              onError={setError}
            />
          )}
          <div className="card" style={{ marginTop: 14 }}>
            <h3>{t("vendorRisk.assessments")}</h3>
            {detail.assessments.length === 0 ? (
              <div className="empty">{t("vendorRisk.questionnairesEmpty")}</div>
            ) : (
              detail.assessments.map((assessment) => (
                <details key={assessment.id} open={assessment.status === "Submitted"}>
                  <summary>
                    {assessment.templateName} ·{" "}
                    <Pill status={riskTone(assessment.status)}>
                      {t(`vendorRisk.assessmentStates.${assessment.status}`, {
                        defaultValue: assessment.status,
                      })}
                    </Pill>{" "}
                    {assessment.answered}/{assessment.total}
                    {assessment.score != null && ` · ${t("vendorRisk.score")} ${assessment.score}`}
                    {assessment.rating &&
                      ` · ${t(`vendorRisk.ratings.${assessment.rating}`, { defaultValue: assessment.rating })}`}
                  </summary>
                  <div className="m">
                    {t("vendorRisk.sentBy", {
                      name: assessment.sentBy,
                      when: formatWhen(assessment.sentAt),
                    })}{" "}
                    · {t("vendorRisk.expires")} {formatWhen(assessment.expiresAt)}
                  </div>
                  {assessment.reviewedBy && (
                    <div className="m">
                      {t("vendorRisk.reviewedBy", { name: assessment.reviewedBy })}
                      {assessment.reviewNote ? ` · ${assessment.reviewNote}` : ""}
                    </div>
                  )}
                  {assessment.answers.length > 0 && (
                    <div className="table-wrap">
                      <table>
                        <thead>
                          <tr>
                            <th>{t("vendorRisk.question")}</th>
                            <th>{t("vendorRisk.answer")}</th>
                            <th>{t("vendorRisk.comment")}</th>
                          </tr>
                        </thead>
                        <tbody>
                          {assessment.answers.map((answer) => (
                            <tr key={answer.questionCode}>
                              <td className="id">
                                {answer.questionCode}
                                {assessment.flagged.includes(answer.questionCode) && (
                                  <>
                                    {" "}
                                    <Pill status="fail">{t("vendorRisk.flagged")}</Pill>
                                  </>
                                )}
                              </td>
                              <td>
                                {t(`vendorRisk.answers.${answer.answer}`, {
                                  defaultValue: answer.answer,
                                })}
                              </td>
                              <td>{answer.comment}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </details>
              ))
            )}
          </div>
          <div className="card" style={{ marginTop: 14 }}>
            <h3>{t("vendorRisk.documents")}</h3>
            {canWrite && (
              <UploadDocument
                tenantId={tenantId}
                vendorId={vendor.id}
                onUploaded={load}
                onError={setError}
              />
            )}
            {detail.documents.length === 0 ? (
              <div className="empty">{t("vendorRisk.documentsEmpty")}</div>
            ) : (
              detail.documents.map((document) => (
                <VendorDocumentCard
                  key={document.id}
                  document={document}
                  tenantId={tenantId}
                  canWrite={canWrite}
                  onChanged={load}
                  onError={setError}
                />
              ))
            )}
          </div>
          <div className="card" style={{ marginTop: 14 }}>
            <h3>{t("vendorRisk.reminders")}</h3>
            {detail.reminders.length === 0 ? (
              <div className="empty">{t("vendorRisk.remindersEmpty")}</div>
            ) : (
              <ul>
                {detail.reminders.map((reminder) => (
                  <li key={reminder.id}>
                    {reminder.title} · {formatDate(reminder.dueOn)} ·{" "}
                    {t(`mePage.status.${reminder.status}`, { defaultValue: reminder.status })}
                  </li>
                ))}
              </ul>
            )}
          </div>
        </>
      )}
    </>
  );
}

function ProfileForm({
  vendor,
  tenantId,
  canWrite,
  onSaved,
  onError,
}: {
  vendor: VendorRiskRow;
  tenantId: string;
  canWrite: boolean;
  onSaved: () => void;
  onError: (code: string | null) => void;
}) {
  const { t } = useTranslation();
  const [ownerName, setOwnerName] = useState(vendor.ownerName);
  const [ownerEmail, setOwnerEmail] = useState(vendor.ownerEmail);
  const [renewal, setRenewal] = useState(vendor.contractRenewalOn ?? "");
  const [categories, setCategories] = useState(vendor.dataCategories.join(", "));
  const [subprocessor, setSubprocessor] = useState(vendor.subprocessor);
  const [cadence, setCadence] = useState(vendor.reviewCadenceMonths);
  const [lastReviewed, setLastReviewed] = useState(vendor.lastReviewedOn ?? "");
  const [contactName, setContactName] = useState(vendor.contactName);
  const [contactEmail, setContactEmail] = useState(vendor.contactEmail);
  const [busy, setBusy] = useState(false);

  async function save(event: React.FormEvent) {
    event.preventDefault();
    setBusy(true);
    onError(null);
    try {
      await api(`/api/v1/vendor-risk/vendors/${vendor.id}/profile`, {
        method: "POST",
        tenantId,
        body: {
          ownerName,
          ownerEmail,
          contractRenewalOn: renewal || null,
          dataCategories: categories
            .split(",")
            .map((item) => item.trim())
            .filter(Boolean),
          subprocessor,
          reviewCadenceMonths: cadence,
          lastReviewedOn: lastReviewed || null,
          contactName,
          contactEmail,
        },
      });
      onSaved();
    } catch (caught: unknown) {
      onError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="card" style={{ marginTop: 14 }} onSubmit={(event) => void save(event)}>
      <h3>{t("vendorRisk.profile")}</h3>
      <fieldset disabled={!canWrite} style={{ border: 0, padding: 0, margin: 0 }}>
        <div className="split-fields">
          <label className="field">
            <span>{t("vendorRisk.ownerName")}</span>
            <input value={ownerName} onChange={(event) => setOwnerName(event.target.value)} />
          </label>
          <label className="field">
            <span>{t("vendorRisk.ownerEmail")}</span>
            <input
              type="email"
              value={ownerEmail}
              onChange={(event) => setOwnerEmail(event.target.value)}
            />
          </label>
        </div>
        <div className="split-fields">
          <label className="field">
            <span>{t("vendorRisk.renewal")}</span>
            <input
              type="date"
              value={renewal}
              onChange={(event) => setRenewal(event.target.value)}
            />
          </label>
          <label className="field">
            <span>{t("vendorRisk.cadenceLabel")}</span>
            <select value={cadence} onChange={(event) => setCadence(Number(event.target.value))}>
              {[3, 6, 12, 24].map((months) => (
                <option key={months} value={months}>
                  {t("vendorRisk.cadence", { count: months })}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            <span>{t("vendorRisk.lastReviewed")}</span>
            <input
              type="date"
              value={lastReviewed}
              onChange={(event) => setLastReviewed(event.target.value)}
            />
          </label>
        </div>
        <label className="field">
          <span>{t("vendorRisk.dataCategories")}</span>
          <input value={categories} onChange={(event) => setCategories(event.target.value)} />
        </label>
        <label className="field">
          <span>
            <input
              type="checkbox"
              checked={subprocessor}
              onChange={(event) => setSubprocessor(event.target.checked)}
            />{" "}
            {t("vendorRisk.subprocessorLabel")}
          </span>
        </label>
        <div className="split-fields">
          <label className="field">
            <span>{t("vendorRisk.contactName")}</span>
            <input value={contactName} onChange={(event) => setContactName(event.target.value)} />
          </label>
          <label className="field">
            <span>{t("vendorRisk.contactEmail")}</span>
            <input
              type="email"
              value={contactEmail}
              onChange={(event) => setContactEmail(event.target.value)}
            />
          </label>
        </div>
        {canWrite && (
          <button className="btn primary" type="submit" disabled={busy}>
            {t("vendorsPage.save")}
          </button>
        )}
      </fieldset>
    </form>
  );
}

function SendQuestionnaire({
  vendor,
  templates,
  tenantId,
  onSent,
  onError,
}: {
  vendor: VendorRiskRow;
  templates: QuestionnaireTemplate[];
  tenantId: string;
  onSent: (sent: SentVendorQuestionnaire) => void;
  onError: (code: string | null) => void;
}) {
  const { t } = useTranslation();
  const [templateCode, setTemplateCode] = useState("core-security");
  const [contactName, setContactName] = useState(vendor.contactName);
  const [contactEmail, setContactEmail] = useState(vendor.contactEmail);
  const [days, setDays] = useState(14);
  const [busy, setBusy] = useState(false);

  async function send(event: React.FormEvent) {
    event.preventDefault();
    setBusy(true);
    onError(null);
    try {
      const sent = await api<SentVendorQuestionnaire>(
        `/api/v1/vendor-risk/vendors/${vendor.id}/questionnaires`,
        {
          method: "POST",
          tenantId,
          body: { templateCode, contactName, contactEmail, expiresInDays: days },
        },
      );
      onSent(sent);
    } catch (caught: unknown) {
      onError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="card" style={{ marginTop: 14 }} onSubmit={(event) => void send(event)}>
      <h3>{t("vendorRisk.sendQuestionnaire")}</h3>
      <div className="split-fields">
        <label className="field">
          <span>{t("vendorRisk.template")}</span>
          <select value={templateCode} onChange={(event) => setTemplateCode(event.target.value)}>
            {templates.map((template) => (
              <option key={template.code} value={template.code}>
                {template.name} ({template.questions.length})
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          <span>{t("vendorRisk.contactName")}</span>
          <input
            required
            value={contactName}
            onChange={(event) => setContactName(event.target.value)}
          />
        </label>
        <label className="field">
          <span>{t("vendorRisk.contactEmail")}</span>
          <input
            required
            type="email"
            value={contactEmail}
            onChange={(event) => setContactEmail(event.target.value)}
          />
        </label>
        <label className="field">
          <span>{t("vendorRisk.expiresInDays")}</span>
          <input
            type="number"
            min={1}
            max={60}
            value={days}
            onChange={(event) => setDays(Number(event.target.value))}
          />
        </label>
      </div>
      <button className="btn primary" type="submit" disabled={busy}>
        {t("vendorRisk.send")}
      </button>
    </form>
  );
}

export function UploadDocument({
  tenantId,
  vendorId,
  onUploaded,
  onError,
  path,
}: {
  tenantId?: string;
  vendorId?: string;
  onUploaded: () => void;
  onError: (code: string | null) => void;
  path?: string;
}) {
  const { t } = useTranslation();
  const [kind, setKind] = useState<string>("Soc2");
  const [file, setFile] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);

  async function upload(event: React.FormEvent) {
    event.preventDefault();
    if (!file) return;
    if (file.size > maxDocumentBytes) {
      onError("too_large");
      return;
    }
    setBusy(true);
    onError(null);
    try {
      const contentBase64 = await readFileAsBase64(file);
      await api(path ?? `/api/v1/vendor-risk/vendors/${vendorId}/documents`, {
        method: "POST",
        tenantId,
        body: { kind, fileName: file.name, contentBase64 },
      });
      setFile(null);
      onUploaded();
    } catch (caught: unknown) {
      onError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="split-fields" onSubmit={(event) => void upload(event)}>
      <label className="field">
        <span>{t("vendorRisk.kind")}</span>
        <select value={kind} onChange={(event) => setKind(event.target.value)}>
          {documentKinds.map((item) => (
            <option key={item} value={item}>
              {t(`vendorRisk.kinds.${item}`)}
            </option>
          ))}
        </select>
      </label>
      <label className="field">
        <span>{t("vendorRisk.file")}</span>
        <input
          type="file"
          accept=".pdf,.txt,.md,.json,.docx"
          onChange={(event) => setFile(event.target.files?.[0] ?? null)}
        />
      </label>
      <div className="field">
        <span>&nbsp;</span>
        <button className="btn primary" type="submit" disabled={busy || !file}>
          {t("vendorRisk.upload")}
        </button>
      </div>
    </form>
  );
}
