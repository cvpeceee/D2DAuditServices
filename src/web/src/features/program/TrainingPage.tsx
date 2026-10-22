import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import { ApiError, api, type Workspace } from "../../shared/api/client";
import { Pager } from "../../shared/ui/Pager";
import { formatDate, Pill } from "../../shared/ui/ui";
import type { TrainingDetail, TrainingModule, TrainingPeople } from "./types";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

export function TrainingPage() {
  const { t } = useTranslation();
  const { tenantId } = useOutletContext<ShellContext>();
  const [modules, setModules] = useState<TrainingModule[] | null>(null);
  const [detail, setDetail] = useState<TrainingDetail | null>(null);
  const [people, setPeople] = useState<TrainingPeople | null>(null);
  const [page, setPage] = useState(1);
  const [answers, setAnswers] = useState<number[]>([]);
  const [certificate, setCertificate] = useState("");
  const [result, setResult] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  function load() {
    if (!tenantId) return;
    void api<TrainingModule[]>("/api/v1/training", { tenantId })
      .then(setModules)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  async function open(id: string) {
    if (!tenantId) return;
    setResult(null);
    setPage(1);
    const next = await api<TrainingDetail>(`/api/v1/training/${id}`, { tenantId });
    setDetail(next);
    setAnswers(next.questions.map(() => 0));
    setPeople(await api<TrainingPeople>(`/api/v1/training/${id}/people?page=1`, { tenantId }));
  }

  useEffect(() => {
    if (!tenantId || !detail) return;
    void api<TrainingPeople>(`/api/v1/training/${detail.id}/people?page=${page}`, {
      tenantId,
    }).then(setPeople);
  }, [detail, page, tenantId]);

  async function submitQuiz(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId || !detail) return;
    const quiz = await api<{ passed: boolean; score: number; evidenceSha256: string | null }>(
      `/api/v1/training/${detail.id}/quiz`,
      {
        method: "POST",
        tenantId,
        body: { answers },
      },
    );
    setResult(
      quiz.passed
        ? t("trainingPage.passed", { score: quiz.score })
        : t("trainingPage.failed", { score: quiz.score }),
    );
    await open(detail.id);
    load();
  }

  async function upload(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId || !detail) return;
    const quiz = await api<{ passed: boolean; evidenceSha256: string | null }>(
      `/api/v1/training/${detail.id}/certificate`,
      {
        method: "POST",
        tenantId,
        body: { fileName: "certificate.txt", body: certificate },
      },
    );
    setResult(quiz.passed ? t("trainingPage.uploaded") : t("errors.load"));
    setCertificate("");
    await open(detail.id);
    load();
  }

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("trainingPage.title")}</h1>
          <div className="sub">{t("trainingPage.sub")}</div>
        </div>
      </div>
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}
      {result && (
        <div className="notice" role="status">
          {result}
        </div>
      )}
      <div className="card">
        <table>
          <thead>
            <tr>
              <th>{t("trainingPage.module")}</th>
              <th>{t("trainingPage.type")}</th>
              <th>{t("trainingPage.group")}</th>
              <th>{t("trainingPage.due")}</th>
              <th>{t("trainingPage.people")}</th>
            </tr>
          </thead>
          <tbody>
            {(modules ?? []).map((row) => (
              <tr key={row.id}>
                <td>
                  <button className="linkish" type="button" onClick={() => void open(row.id)}>
                    <b>{row.title}</b>
                  </button>
                  <div className="id">{row.code}</div>
                </td>
                <td>{t(`trainingPage.types.${row.type}`)}</td>
                <td>{row.assignedGroup}</td>
                <td>{formatDate(row.dueOn)}</td>
                <td>
                  {row.completed} / {row.assigned}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {detail && (
        <div className="card" style={{ marginTop: 14 }}>
          <h3>{detail.title}</h3>
          <div className="m">
            {detail.completed ? (
              <Pill status="pass">{t("trainingPage.complete")}</Pill>
            ) : (
              <Pill status="warn">{t("trainingPage.due")}</Pill>
            )}
            {detail.evidenceSha256 && (
              <span className="id"> {detail.evidenceSha256.slice(0, 16)}</span>
            )}
          </div>
          {detail.slides.map((slide) => (
            <section key={slide.title}>
              <h3>{slide.title}</h3>
              <p>{slide.body}</p>
            </section>
          ))}
          {detail.type === "BuiltIn" && !detail.completed && (
            <form onSubmit={(event) => void submitQuiz(event)}>
              {detail.questions.map((question, index) => (
                <fieldset key={question.prompt}>
                  <legend>{question.prompt}</legend>
                  {question.choices.map((choice, choiceIndex) => (
                    <label key={choice} className="check-row">
                      <input
                        type="radio"
                        name={`q-${index}`}
                        checked={answers[index] === choiceIndex}
                        onChange={() =>
                          setAnswers((current) =>
                            current.map((value, item) => (item === index ? choiceIndex : value)),
                          )
                        }
                      />
                      {choice}
                    </label>
                  ))}
                </fieldset>
              ))}
              <button className="btn primary" type="submit">
                {t("trainingPage.submit")}
              </button>
            </form>
          )}
          {detail.type === "External" && (
            <form onSubmit={(event) => void upload(event)}>
              {detail.externalUrl && (
                <p>
                  <a href={detail.externalUrl}>{detail.externalUrl}</a>
                </p>
              )}
              {!detail.completed && (
                <>
                  <textarea
                    value={certificate}
                    onChange={(event) => setCertificate(event.target.value)}
                    placeholder={t("trainingPage.certificate")}
                    required
                  />
                  <button className="btn primary" type="submit">
                    {t("trainingPage.upload")}
                  </button>
                </>
              )}
            </form>
          )}
          {people && (
            <>
              <h3>{t("trainingPage.people")}</h3>
              <table>
                <thead>
                  <tr>
                    <th>{t("peoplePage.title")}</th>
                    <th>{t("trainingPage.status")}</th>
                    <th>{t("trainingPage.due")}</th>
                  </tr>
                </thead>
                <tbody>
                  {people.items.map((person) => (
                    <tr key={person.email}>
                      <td>
                        <b>{person.name}</b>
                        <div className="id">{person.department}</div>
                      </td>
                      <td>{person.status}</td>
                      <td>{person.dueOn ? formatDate(person.dueOn) : "—"}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <Pager
                page={people.page}
                pageSize={people.pageSize}
                total={people.total}
                onPage={setPage}
              />
            </>
          )}
        </div>
      )}
    </>
  );
}
