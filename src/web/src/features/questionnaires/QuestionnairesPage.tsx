import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import { ApiError, api, download, type Workspace } from "../../shared/api/client";
import { formatWhen, Pill } from "../../shared/ui/ui";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

type Summary = {
  id: string;
  name: string;
  customerName: string;
  status: string;
  createdAt: string;
  questions: number;
  accepted: number;
};

type Answer = {
  id: string;
  position: number;
  question: string;
  suggestion: string;
  citation: string;
  answer: string;
  reviewerName: string;
  accepted: boolean;
};

type Detail = Summary & { answers: Answer[] };

export function QuestionnairesPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [rows, setRows] = useState<Summary[] | null>(null);
  const [current, setCurrent] = useState<Detail | null>(null);
  const [draft, setDraft] = useState({ name: "", customerName: "" });
  const [file, setFile] = useState<File | null>(null);
  const [edits, setEdits] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);

  function load() {
    if (!tenantId) return;
    void api<Summary[]>("/api/v1/questionnaires", { tenantId })
      .then(setRows)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  async function open(id: string) {
    if (!tenantId) return;
    const detail = await api<Detail>(`/api/v1/questionnaires/${id}`, { tenantId });
    setCurrent(detail);
    setEdits(Object.fromEntries(detail.answers.map((item) => [item.id, item.answer])));
  }

  async function upload(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId || !file) return;
    const contentBase64 = await readBase64(file);
    const detail = await api<Detail>("/api/v1/questionnaires", {
      method: "POST",
      tenantId,
      body: { ...draft, fileName: file.name, contentBase64 },
    });
    setDraft({ name: "", customerName: "" });
    setFile(null);
    setCurrent(detail);
    setEdits(Object.fromEntries(detail.answers.map((item) => [item.id, item.answer])));
    load();
  }

  async function review(answer: Answer, accepted: boolean) {
    if (!tenantId || !current) return;
    await api(`/api/v1/questionnaires/${current.id}/answers/${answer.id}`, {
      method: "POST",
      tenantId,
      body: { answer: edits[answer.id] ?? answer.answer, accepted },
    });
    await open(current.id);
    load();
  }

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("questionnairesPage.title")}</h1>
          <div className="sub">{t("questionnairesPage.sub")}</div>
        </div>
      </div>
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>{t("questionnairesPage.name")}</th>
              <th>{t("questionnairesPage.customer")}</th>
              <th>{t("questionnairesPage.status")}</th>
              <th>{t("questionnairesPage.progress")}</th>
            </tr>
          </thead>
          <tbody>
            {rows?.map((row) => (
              <tr key={row.id} onClick={() => void open(row.id)} style={{ cursor: "pointer" }}>
                <td>
                  <b>{row.name}</b>
                  <div>{formatWhen(row.createdAt)}</div>
                </td>
                <td>{row.customerName}</td>
                <td>
                  <Pill status={row.status === "Accepted" ? "pass" : "warn"}>
                    {t(`questionnairesPage.statuses.${row.status}`)}
                  </Pill>
                </td>
                <td>
                  {t("questionnairesPage.acceptedOf", {
                    accepted: row.accepted,
                    questions: row.questions,
                  })}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {current && (
        <section className="card">
          <div className="head">
            <h3>
              {current.name} · {current.customerName}
            </h3>
            <button
              className="btn"
              type="button"
              onClick={() =>
                tenantId && void download(`/api/v1/questionnaires/${current.id}/export`, tenantId)
              }
            >
              {t("questionnairesPage.export")}
            </button>
          </div>
          {current.answers.map((answer) => (
            <article key={answer.id} className="card">
              <b>
                {answer.position}. {answer.question}
              </b>
              <p>{answer.suggestion}</p>
              {answer.citation && (
                <div className="id">
                  {t("questionnairesPage.citation")}: {answer.citation}
                </div>
              )}
              {workspace?.canWrite ? (
                <>
                  <label className="field">
                    <span>{t("questionnairesPage.answer")}</span>
                    <textarea
                      value={edits[answer.id] ?? answer.answer}
                      onChange={(event) => setEdits({ ...edits, [answer.id]: event.target.value })}
                    />
                  </label>
                  <div className="act">
                    <button className="btn" type="button" onClick={() => void review(answer, true)}>
                      {t("questionnairesPage.accept")}
                    </button>
                    <button
                      className="btn"
                      type="button"
                      onClick={() => void review(answer, false)}
                    >
                      {t("questionnairesPage.reopen")}
                    </button>
                  </div>
                </>
              ) : (
                <p>{answer.answer}</p>
              )}
              {answer.accepted && (
                <div>{t("questionnairesPage.reviewedBy", { name: answer.reviewerName })}</div>
              )}
            </article>
          ))}
        </section>
      )}
      {workspace?.canWrite && (
        <form className="card" onSubmit={(event) => void upload(event)}>
          <h3>{t("questionnairesPage.upload")}</h3>
          <label className="field">
            <span>{t("questionnairesPage.name")}</span>
            <input
              aria-label={t("questionnairesPage.name")}
              value={draft.name}
              onChange={(event) => setDraft({ ...draft, name: event.target.value })}
            />
          </label>
          <label className="field">
            <span>{t("questionnairesPage.customer")}</span>
            <input
              aria-label={t("questionnairesPage.customer")}
              value={draft.customerName}
              onChange={(event) => setDraft({ ...draft, customerName: event.target.value })}
            />
          </label>
          <label className="field">
            <span>{t("questionnairesPage.file")}</span>
            <input
              aria-label={t("questionnairesPage.file")}
              type="file"
              accept=".csv,.xlsx,text/csv"
              onChange={(event) => setFile(event.target.files?.[0] ?? null)}
            />
          </label>
          <button className="btn primary" type="submit" disabled={!file}>
            {t("questionnairesPage.upload")}
          </button>
        </form>
      )}
    </>
  );
}

function readBase64(file: File) {
  return new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const value = String(reader.result ?? "");
      const comma = value.indexOf(",");
      resolve(comma >= 0 ? value.slice(comma + 1) : value);
    };
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });
}
