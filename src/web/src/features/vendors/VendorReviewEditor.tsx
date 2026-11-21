import { useState } from "react";
import { useTranslation } from "react-i18next";
import { ApiError, api, type VendorListItem } from "../../shared/api/client";
import { formatDate } from "../../shared/ui/ui";
import { AssignTask } from "../program/AssignTask";

type VendorQuestion = { prompt: string; answer: string };

const reviews = ["NotStarted", "InReview", "Approved", "Expired"] as const;
const decisions = ["Pending", "Approved", "Renew"] as const;

export type RichVendor = VendorListItem & {
  decision?: string;
  residualLikelihood?: number;
  residualImpact?: number;
  residualScore?: number;
  residualBand?: string;
  questions?: VendorQuestion[] | null;
};

export function vendorReviewTone(state: string) {
  if (state === "Approved") return "pass";
  if (state === "Expired") return "fail";
  if (state === "InReview") return "info";
  return "warn";
}

export function VendorReviewEditor({
  vendor,
  tenantId,
  canWrite,
  onSaved,
}: {
  vendor: RichVendor;
  tenantId: string;
  canWrite: boolean;
  onSaved: () => void;
}) {
  const { t } = useTranslation();
  const [selected, setSelected] = useState<RichVendor>(vendor);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function save(event: React.FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api("/api/v1/vendors", { method: "POST", tenantId, body: selected });
      onSaved();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}
      <div className="card" style={{ marginTop: 14 }}>
        <form onSubmit={(event) => void save(event)}>
          <h3>{selected.name}</h3>
          <p className="sub">{selected.dataAccess}</p>
          <h3>{t("vendorsPage.questionnaire")}</h3>
          {(selected.questions ?? []).length === 0 ? (
            <div className="empty">{t("vendorsPage.questionnaire")}</div>
          ) : (
            (selected.questions ?? []).map((question, index) => (
              <label className="field" key={question.prompt}>
                <span>{question.prompt}</span>
                <textarea
                  value={question.answer}
                  disabled={!canWrite}
                  aria-label={t("vendorsPage.answer")}
                  onChange={(event) => {
                    const questions = [...(selected.questions ?? [])];
                    questions[index] = { ...question, answer: event.target.value };
                    setSelected({ ...selected, questions });
                  }}
                />
              </label>
            ))
          )}
          <div className="split-fields">
            <div className="field">
              <label htmlFor="vendor-decision">{t("vendorsPage.decision")}</label>
              <select
                id="vendor-decision"
                value={selected.decision ?? "Pending"}
                disabled={!canWrite}
                onChange={(event) => setSelected({ ...selected, decision: event.target.value })}
              >
                {decisions.map((decision) => (
                  <option key={decision} value={decision}>
                    {t(`vendorsPage.decisions.${decision}`)}
                  </option>
                ))}
              </select>
            </div>
            <div className="field">
              <label htmlFor="vendor-rl">{t("risksPage.likelihood")}</label>
              <input
                id="vendor-rl"
                type="number"
                min={1}
                max={5}
                value={selected.residualLikelihood ?? 2}
                disabled={!canWrite}
                onChange={(event) =>
                  setSelected({ ...selected, residualLikelihood: Number(event.target.value) })
                }
              />
            </div>
            <div className="field">
              <label htmlFor="vendor-ri">{t("risksPage.impact")}</label>
              <input
                id="vendor-ri"
                type="number"
                min={1}
                max={5}
                value={selected.residualImpact ?? 2}
                disabled={!canWrite}
                onChange={(event) =>
                  setSelected({ ...selected, residualImpact: Number(event.target.value) })
                }
              />
            </div>
          </div>
          <p className="m">
            {t("vendorsPage.residual")}: {selected.residualScore ?? "—"}
            {selected.residualBand ? ` · ${t(`risksPage.bands.${selected.residualBand}`)}` : ""}
          </p>
          <h3>{t("vendorsPage.documents")}</h3>
          {selected.documents.length === 0 ? (
            <div className="empty">{t("vendorsPage.documents")}</div>
          ) : (
            <ul>
              {selected.documents.map((document) => (
                <li key={document.title}>
                  {document.title}
                  {document.issuedOn ? ` · ${formatDate(document.issuedOn)}` : ""}
                </li>
              ))}
            </ul>
          )}
          <div className="split-fields">
            <div className="field">
              <label htmlFor="vendor-review">{t("vendorsPage.review")}</label>
              <select
                id="vendor-review"
                value={selected.reviewState}
                disabled={!canWrite}
                onChange={(event) => setSelected({ ...selected, reviewState: event.target.value })}
              >
                {reviews.map((review) => (
                  <option key={review} value={review}>
                    {t(`vendorsPage.reviews.${review}`)}
                  </option>
                ))}
              </select>
            </div>
            <div className="field">
              <label htmlFor="vendor-due">{t("vendorsPage.due")}</label>
              <input
                id="vendor-due"
                type="date"
                value={selected.reviewDueOn}
                disabled={!canWrite}
                onChange={(event) => setSelected({ ...selected, reviewDueOn: event.target.value })}
              />
            </div>
          </div>
          {canWrite && (
            <button className="btn primary" type="submit" disabled={busy}>
              {t("vendorsPage.save")}
            </button>
          )}
        </form>
        <div style={{ marginTop: 12 }}>
          <AssignTask
            tenantId={tenantId}
            linkedKind="Vendor"
            linkedCode={selected.name}
            canWrite={canWrite}
          />
        </div>
      </div>
    </>
  );
}
