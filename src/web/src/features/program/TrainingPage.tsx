import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import { ApiError, api, download, type Workspace } from "../../shared/api/client";
import { Pager } from "../../shared/ui/Pager";
import { formatDate, Pill, safeHref } from "../../shared/ui/ui";
import type { TrainingDetail, TrainingModule, TrainingPeople } from "./types";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

type AcademyModuleItem = {
  key: string;
  title: string;
  summary: string;
  minutes: number;
  required: boolean;
  frameworkSpecific: boolean;
  audiences: string[];
  sectionsTotal: number;
  sectionsCompleted: number;
  status: string;
  bestScore: number;
  questionCount: number;
  passedAt: string | null;
  expiresOn: string | null;
  certificateNumber: string | null;
};

type AcademyOverview = {
  modules: AcademyModuleItem[];
  required: number;
  current: number;
  passMark: number;
  renewalMonths: number;
};

type AcademyModuleDetail = {
  module: AcademyModuleItem;
  sections: { heading: string; body: string }[];
  questions: { prompt: string; choices: string[] }[];
};

type AcademyQuizResult = {
  correct: number;
  total: number;
  passMark: number;
  passed: boolean;
  wrong: number[];
  explanations: string[];
  expiresOn: string | null;
  certificateNumber: string | null;
  evidenceId: string | null;
};

type AcademyAssignmentItem = {
  moduleKey: string;
  title: string;
  available: boolean;
  audiences: string[];
  assigned: number;
  current: number;
};

function academyTone(status: string) {
  if (status === "Passed") return "pass";
  if (status === "Expired") return "fail";
  if (status === "InProgress") return "warn";
  return "info";
}

export function TrainingPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [tab, setTab] = useState<"academy" | "assignments" | "legacy">("academy");

  // Security Awareness Academy state
  const [overview, setOverview] = useState<AcademyOverview | null>(null);
  const [trackFilter, setTrackFilter] = useState<string>("all");
  const [activeCourse, setActiveCourse] = useState<AcademyModuleDetail | null>(null);
  const [academyAnswers, setAcademyAnswers] = useState<number[]>([]);
  const [quizFeedback, setQuizFeedback] = useState<AcademyQuizResult | null>(null);
  const [assignments, setAssignments] = useState<AcademyAssignmentItem[] | null>(null);
  const [draftAudiences, setDraftAudiences] = useState<Record<string, string>>({});

  // Legacy annual / external LMS state
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
    void api<AcademyOverview>("/api/v1/academy", { tenantId })
      .then(setOverview)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
    void api<TrainingModule[]>("/api/v1/training", { tenantId })
      .then(setModules)
      .catch(() => setModules([]));
    if (workspace?.canWrite) {
      void api<AcademyAssignmentItem[]>("/api/v1/academy/assignments", { tenantId })
        .then((items) => {
          setAssignments(items);
          setDraftAudiences(
            Object.fromEntries(items.map((item) => [item.moduleKey, item.audiences.join(", ")])),
          );
        })
        .catch(() => setAssignments([]));
    }
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId, workspace?.canWrite]);

  async function openCourse(key: string) {
    if (!tenantId) return;
    setError(null);
    setResult(null);
    setQuizFeedback(null);
    try {
      const course = await api<AcademyModuleDetail>(`/api/v1/academy/${key}`, { tenantId });
      setActiveCourse(course);
      setAcademyAnswers(course.questions.map(() => 0));
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  async function markSection(sectionsCompleted: number) {
    if (!tenantId || !activeCourse) return;
    setError(null);
    try {
      const updated = await api<AcademyModuleItem>(
        `/api/v1/academy/${activeCourse.module.key}/progress`,
        {
          method: "POST",
          tenantId,
          body: { sectionsCompleted },
        },
      );
      setActiveCourse({ ...activeCourse, module: updated });
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  async function submitAcademyQuiz(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId || !activeCourse) return;
    setError(null);
    try {
      const outcome = await api<AcademyQuizResult>(
        `/api/v1/academy/${activeCourse.module.key}/quiz`,
        {
          method: "POST",
          tenantId,
          body: { answers: academyAnswers },
        },
      );
      setQuizFeedback(outcome);
      setResult(
        outcome.passed
          ? t("trainingPage.academy.passedNotice", {
              correct: outcome.correct,
              total: outcome.total,
              cert: outcome.certificateNumber ?? "",
            })
          : t("trainingPage.academy.failedNotice", {
              correct: outcome.correct,
              total: outcome.total,
              passMark: outcome.passMark,
            }),
      );
      const refreshed = await api<AcademyModuleDetail>(
        `/api/v1/academy/${activeCourse.module.key}`,
        { tenantId },
      );
      setActiveCourse(refreshed);
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  async function saveAssignment(item: AcademyAssignmentItem) {
    if (!tenantId) return;
    setError(null);
    const list = (draftAudiences[item.moduleKey] ?? "")
      .split(",")
      .map((entry) => entry.trim())
      .filter(Boolean);
    try {
      await api<AcademyAssignmentItem>(`/api/v1/academy/assignments/${item.moduleKey}`, {
        method: "PUT",
        tenantId,
        body: { audiences: list },
      });
      setResult(t("trainingPage.academy.savedAudiences", { module: item.title }));
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    }
  }

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

  const filteredCourses = (overview?.modules ?? []).filter((course) => {
    if (trackFilter === "all") return true;
    if (trackFilter === "required") return course.required;
    return course.status === trackFilter;
  });

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

      <div className="tabs" role="tablist" aria-label={t("trainingPage.title")}>
        <button
          type="button"
          role="tab"
          aria-selected={tab === "academy"}
          className={tab === "academy" ? "on" : ""}
          onClick={() => setTab("academy")}
        >
          {t("trainingPage.tabs.academy")} ({overview?.modules.length ?? 0})
        </button>
        {workspace?.canWrite && (
          <button
            type="button"
            role="tab"
            aria-selected={tab === "assignments"}
            className={tab === "assignments" ? "on" : ""}
            onClick={() => setTab("assignments")}
          >
            {t("trainingPage.tabs.assignments")}
          </button>
        )}
        <button
          type="button"
          role="tab"
          aria-selected={tab === "legacy"}
          className={tab === "legacy" ? "on" : ""}
          onClick={() => setTab("legacy")}
        >
          {t("trainingPage.tabs.legacy")} ({modules?.length ?? 0})
        </button>
      </div>

      {tab === "academy" && overview && (
        <div className="grid">
          <div className="kv">
            <div>
              <small>{t("trainingPage.academy.requiredCompleted")}</small>
              <b>
                {overview.current} / {overview.required}
              </b>
            </div>
            <div>
              <small>{t("trainingPage.academy.passMark")}</small>
              <b>{overview.passMark} / 5</b>
            </div>
            <div>
              <small>{t("trainingPage.academy.renewal")}</small>
              <b>{t("trainingPage.academy.months", { count: overview.renewalMonths })}</b>
            </div>
          </div>

          {!activeCourse ? (
            <>
              <div className="chips">
                {(["all", "required", "Passed", "InProgress", "NotStarted"] as const).map((key) => (
                  <button
                    key={key}
                    type="button"
                    className={trackFilter === key ? "btn primary" : "chip"}
                    onClick={() => setTrackFilter(key)}
                  >
                    {t(`trainingPage.academy.filters.${key}`)}
                  </button>
                ))}
              </div>

              <div className="cardgrid">
                {filteredCourses.map((course) => {
                  const pct =
                    course.sectionsTotal > 0
                      ? Math.round((course.sectionsCompleted / course.sectionsTotal) * 100)
                      : 0;
                  return (
                    <article key={course.key} className="card stack">
                      <h3>
                        <span>{course.title}</span>
                        <span className="r">
                          <Pill status={academyTone(course.status)}>
                            {t(`trainingPage.academy.statuses.${course.status}`, {
                              defaultValue: course.status,
                            })}
                          </Pill>
                        </span>
                      </h3>
                      <p className="m" style={{ margin: 0 }}>
                        {course.summary}
                      </p>
                      <div className="chips">
                        <span className="tag">
                          {t("trainingPage.academy.minutes", { count: course.minutes })}
                        </span>
                        <span className="tag">
                          {course.required
                            ? t("trainingPage.academy.required")
                            : t("trainingPage.academy.optional")}
                        </span>
                        {course.frameworkSpecific && (
                          <span className="tag">{t("trainingPage.academy.framework")}</span>
                        )}
                        {course.audiences.map((aud) => (
                          <span key={aud} className="tag">
                            {aud}
                          </span>
                        ))}
                      </div>
                      <div>
                        <div className="m" style={{ marginBottom: 4 }}>
                          {t("trainingPage.academy.sectionsProgress", {
                            done: course.sectionsCompleted,
                            total: course.sectionsTotal,
                          })}
                        </div>
                        <div className="bar">
                          <i style={{ width: `${pct}%` }} />
                        </div>
                      </div>
                      {course.certificateNumber && (
                        <div className="id">
                          {t("trainingPage.academy.certNumber")}: {course.certificateNumber}
                          {course.expiresOn ? ` · ${formatDate(course.expiresOn)}` : ""}
                        </div>
                      )}
                      <div className="row" style={{ marginTop: "auto" }}>
                        <button
                          className="btn primary"
                          type="button"
                          onClick={() => void openCourse(course.key)}
                        >
                          {course.status === "Passed"
                            ? t("trainingPage.academy.review")
                            : course.status === "InProgress"
                              ? t("trainingPage.academy.continue")
                              : t("trainingPage.academy.start")}
                        </button>
                        {course.status === "Passed" && (
                          <button
                            className="btn"
                            type="button"
                            onClick={() =>
                              void download(`/api/v1/academy/${course.key}/certificate`, tenantId)
                            }
                          >
                            {t("trainingPage.academy.downloadCert")}
                          </button>
                        )}
                      </div>
                    </article>
                  );
                })}
              </div>
            </>
          ) : (
            <div className="card stack">
              <div className="row" style={{ justifyContent: "space-between", flexWrap: "wrap" }}>
                <div>
                  <h3 style={{ marginBottom: 4 }}>{activeCourse.module.title}</h3>
                  <div className="m">{activeCourse.module.summary}</div>
                </div>
                <div className="row">
                  <Pill status={academyTone(activeCourse.module.status)}>
                    {t(`trainingPage.academy.statuses.${activeCourse.module.status}`, {
                      defaultValue: activeCourse.module.status,
                    })}
                  </Pill>
                  {activeCourse.module.status === "Passed" && (
                    <button
                      className="btn"
                      type="button"
                      onClick={() =>
                        void download(
                          `/api/v1/academy/${activeCourse.module.key}/certificate`,
                          tenantId,
                        )
                      }
                    >
                      {t("trainingPage.academy.downloadCert")}
                    </button>
                  )}
                  <button
                    className="btn"
                    type="button"
                    onClick={() => {
                      setActiveCourse(null);
                      setQuizFeedback(null);
                    }}
                  >
                    {t("trainingPage.academy.close")}
                  </button>
                </div>
              </div>

              {activeCourse.module.certificateNumber && (
                <div className="local-banner">
                  <b>{t("trainingPage.academy.certNumber")}:</b>{" "}
                  <code>{activeCourse.module.certificateNumber}</code>
                  {activeCourse.module.expiresOn && (
                    <span>
                      {" "}
                      · {t("trainingPage.academy.expires")}{" "}
                      {formatDate(activeCourse.module.expiresOn)}
                    </span>
                  )}
                </div>
              )}

              {activeCourse.sections.map((section, idx) => {
                const completed = activeCourse.module.sectionsCompleted > idx;
                return (
                  <section
                    key={section.heading}
                    className="card"
                    style={{ background: "var(--surface2)" }}
                  >
                    <h3>
                      <span>
                        {idx + 1}. {section.heading}
                      </span>
                      <span className="r">
                        {completed ? (
                          <Pill status="pass">{t("trainingPage.academy.sectionDone")}</Pill>
                        ) : (
                          <button
                            className="btn"
                            type="button"
                            onClick={() => void markSection(idx + 1)}
                          >
                            {t("trainingPage.academy.markSection")}
                          </button>
                        )}
                      </span>
                    </h3>
                    <div style={{ whiteSpace: "pre-line" }}>{section.body}</div>
                  </section>
                );
              })}

              <form
                className="stack"
                style={{ borderTop: "1px solid var(--border)", paddingTop: 14 }}
                onSubmit={(event) => void submitAcademyQuiz(event)}
              >
                <h3>
                  {t("trainingPage.academy.quizHeading", {
                    count: activeCourse.questions.length,
                    passMark: overview.passMark,
                  })}
                </h3>
                {activeCourse.questions.map((question, qIdx) => (
                  <fieldset
                    key={question.prompt}
                    style={{
                      border: "1px solid var(--border)",
                      borderRadius: 10,
                      padding: 12,
                    }}
                  >
                    <legend style={{ fontWeight: 600 }}>
                      {qIdx + 1}. {question.prompt}
                    </legend>
                    {question.choices.map((choice, cIdx) => (
                      <label key={choice} className="check-row">
                        <input
                          type="radio"
                          name={`academy-q-${qIdx}`}
                          checked={academyAnswers[qIdx] === cIdx}
                          onChange={() =>
                            setAcademyAnswers((current) =>
                              current.map((val, idx) => (idx === qIdx ? cIdx : val)),
                            )
                          }
                        />
                        {choice}
                      </label>
                    ))}
                  </fieldset>
                ))}

                {quizFeedback && quizFeedback.explanations.length > 0 && (
                  <div className="local-banner">
                    <b>
                      {quizFeedback.correct} / {quizFeedback.total}
                    </b>
                    <ul style={{ margin: "6px 0 0", paddingInlineStart: 18 }}>
                      {quizFeedback.explanations.map((exp, idx) => (
                        <li key={`${exp}-${idx}`}>{exp}</li>
                      ))}
                    </ul>
                  </div>
                )}

                <div>
                  <button className="btn primary" type="submit">
                    {t("trainingPage.academy.submitQuiz")}
                  </button>
                </div>
              </form>
            </div>
          )}
        </div>
      )}

      {tab === "assignments" && workspace?.canWrite && (
        <div className="card">
          <h3>{t("trainingPage.academy.assignmentsTitle")}</h3>
          <p className="m">{t("trainingPage.academy.assignmentsHelp")}</p>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>{t("trainingPage.module")}</th>
                  <th>{t("trainingPage.academy.available")}</th>
                  <th>{t("trainingPage.academy.completion")}</th>
                  <th>{t("trainingPage.academy.audiences")}</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {(assignments ?? []).map((item) => (
                  <tr key={item.moduleKey}>
                    <td>
                      <b>{item.title}</b>
                      <div className="id">{item.moduleKey}</div>
                    </td>
                    <td>
                      <Pill status={item.available ? "pass" : "neutral"}>
                        {item.available
                          ? t("trainingPage.academy.active")
                          : t("trainingPage.academy.gated")}
                      </Pill>
                    </td>
                    <td>
                      {item.current} / {item.assigned}
                    </td>
                    <td>
                      <input
                        className="search-input"
                        aria-label={`${item.title} ${t("trainingPage.academy.audiences")}`}
                        value={draftAudiences[item.moduleKey] ?? ""}
                        onChange={(event) =>
                          setDraftAudiences((prev) => ({
                            ...prev,
                            [item.moduleKey]: event.target.value,
                          }))
                        }
                      />
                    </td>
                    <td>
                      <button
                        className="btn"
                        type="button"
                        onClick={() => void saveAssignment(item)}
                      >
                        {t("trainingPage.academy.saveAudiences")}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {tab === "legacy" && (
        <>
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
                                current.map((value, item) =>
                                  item === index ? choiceIndex : value,
                                ),
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
                  {safeHref(detail.externalUrl) && (
                    <p>
                      <a
                        href={safeHref(detail.externalUrl) ?? undefined}
                        target="_blank"
                        rel="noopener noreferrer"
                      >
                        {detail.externalUrl}
                      </a>
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
      )}
    </>
  );
}
