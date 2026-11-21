import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useParams } from "react-router-dom";
import { ApiError, api } from "../../shared/api/client";
import { formatWhen, Pill } from "../../shared/ui/ui";
import { UploadDocument } from "../vendors/VendorDetailPage";
import { answerValues, type VendorAnswer, type VendorPortalView } from "../vendors/vendorRiskTypes";

export function VendorPortalPage() {
  const { t } = useTranslation();
  const { token = "" } = useParams();
  const base = `/api/v1/public/vendor-portal/${encodeURIComponent(token)}`;
  const [view, setView] = useState<VendorPortalView | null>(null);
  const [answers, setAnswers] = useState<Record<string, VendorAnswer>>({});
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  function apply(next: VendorPortalView) {
    setView(next);
    setAnswers(Object.fromEntries(next.answers.map((answer) => [answer.questionCode, answer])));
  }

  const load = () => {
    void api<VendorPortalView>(base, { skipRefresh: true })
      .then(apply)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token]);

  const editable = view?.status === "Sent" || view?.status === "InProgress";
  const missing = useMemo(
    () =>
      view ? view.template.questions.filter((question) => !answers[question.code]?.answer) : [],
    [view, answers],
  );

  function setAnswer(code: string, patch: Partial<VendorAnswer>) {
    setAnswers((current) => {
      const existing: VendorAnswer = current[code] ?? {
        questionCode: code,
        answer: "",
        comment: "",
      };
      return { ...current, [code]: { ...existing, ...patch } };
    });
  }

  async function save(submit: boolean) {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const next = await api<VendorPortalView>(`${base}/answers`, {
        method: "POST",
        skipRefresh: true,
        body: {
          answers: Object.values(answers).filter((answer) => answer.answer),
          submit,
        },
      });
      apply(next);
      setNotice(submit ? t("vendorPortal.submitted") : t("vendorPortal.saved"));
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  const fatal = error === "expired" || error === "not_found";

  return (
    <main className="content" id="content" style={{ maxWidth: 920, margin: "0 auto" }}>
      {fatal ? (
        <div className="card">
          <h1>{t("vendorPortal.title")}</h1>
          <div className="error" role="alert">
            {error === "expired" ? t("vendorPortal.expired") : t("vendorPortal.notFound")}
          </div>
        </div>
      ) : (
        <>
          <div className="head">
            <div>
              <h1>{t("vendorPortal.title")}</h1>
              {view && (
                <div className="sub">
                  {t("vendorPortal.sub", { company: view.companyName, vendor: view.vendorName })}
                </div>
              )}
            </div>
            {view && (
              <Pill status={editable ? "warn" : "pass"}>
                {t(`vendorRisk.assessmentStates.${view.status}`, { defaultValue: view.status })}
              </Pill>
            )}
          </div>
          {error && (
            <div className="error" role="alert">
              {t(`vendorPortal.errors.${error}`, {
                defaultValue: `${t("errors.load")} (${error})`,
              })}
            </div>
          )}
          {notice && (
            <div className="notice" role="status">
              {notice}
            </div>
          )}
          {view && (
            <>
              <div className="card">
                <p>
                  {t("vendorPortal.intro", { name: view.contactName })}{" "}
                  {t("vendorPortal.expires", { when: formatWhen(view.expiresAt) })}
                </p>
                <p className="m">{view.template.description}</p>
                <div className="m">
                  {t("vendorPortal.progress", {
                    done: view.template.questions.length - missing.length,
                    total: view.template.questions.length,
                  })}
                </div>
              </div>
              <form
                className="card"
                style={{ marginTop: 14 }}
                onSubmit={(event) => {
                  event.preventDefault();
                  void save(true);
                }}
              >
                <h3>{view.template.name}</h3>
                <fieldset disabled={!editable || busy} style={{ border: 0, padding: 0, margin: 0 }}>
                  {view.template.questions.map((question) => (
                    <div className="field" key={question.code}>
                      <span>
                        <b className="id">{question.code}</b> {question.text}
                        {question.critical && (
                          <>
                            {" "}
                            <Pill status="warn">{t("vendorPortal.critical")}</Pill>
                          </>
                        )}
                      </span>
                      <div role="radiogroup" aria-label={question.code}>
                        {answerValues.map((value) => (
                          <label key={value} style={{ marginRight: 12 }}>
                            <input
                              type="radio"
                              name={`answer-${question.code}`}
                              value={value}
                              checked={answers[question.code]?.answer === value}
                              onChange={() => setAnswer(question.code, { answer: value })}
                            />{" "}
                            {t(`vendorRisk.answers.${value}`)}
                          </label>
                        ))}
                      </div>
                      <input
                        aria-label={t("vendorRisk.comment")}
                        placeholder={t("vendorRisk.comment")}
                        maxLength={1000}
                        value={answers[question.code]?.comment ?? ""}
                        onChange={(event) =>
                          setAnswer(question.code, { comment: event.target.value })
                        }
                      />
                    </div>
                  ))}
                  {editable && (
                    <div className="stack">
                      <button className="btn" type="button" onClick={() => void save(false)}>
                        {t("vendorPortal.save")}
                      </button>
                      <button
                        className="btn primary"
                        type="submit"
                        disabled={missing.length > 0}
                        title={missing.length > 0 ? t("vendorPortal.incomplete") : undefined}
                      >
                        {t("vendorPortal.submit")}
                      </button>
                    </div>
                  )}
                </fieldset>
              </form>
              <div className="card" style={{ marginTop: 14 }}>
                <h3>{t("vendorPortal.documents")}</h3>
                <p className="m">{t("vendorPortal.documentsSub")}</p>
                {view.documents.length > 0 && (
                  <ul>
                    {view.documents.map((document) => (
                      <li key={`${document.fileName}-${document.uploadedAt}`}>
                        {t(`vendorRisk.kinds.${document.kind}`, { defaultValue: document.kind })} ·{" "}
                        {document.fileName} · {formatWhen(document.uploadedAt)}
                      </li>
                    ))}
                  </ul>
                )}
                {editable && (
                  <UploadDocument
                    path={`${base}/documents`}
                    onUploaded={() => {
                      setNotice(t("vendorPortal.uploaded"));
                      load();
                    }}
                    onError={setError}
                  />
                )}
              </div>
            </>
          )}
        </>
      )}
    </main>
  );
}
