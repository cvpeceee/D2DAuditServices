import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import { ApiError, api, type Workspace } from "../../shared/api/client";
import { formatDate, Pill } from "../../shared/ui/ui";
import type { WorkTask } from "./types";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

const kinds = ["Control", "Test", "Risk", "Vendor", "Policy"] as const;
const recurrences = ["None", "Weekly", "Monthly", "Quarterly", "Annual"] as const;

export function TasksPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [rows, setRows] = useState<WorkTask[] | null>(null);
  const [title, setTitle] = useState("");
  const [linkedKind, setLinkedKind] = useState<(typeof kinds)[number]>("Control");
  const [linkedCode, setLinkedCode] = useState("");
  const [ownerName, setOwnerName] = useState("");
  const [ownerEmail, setOwnerEmail] = useState("");
  const [dueOn, setDueOn] = useState(new Date().toISOString().slice(0, 10));
  const [recurrence, setRecurrence] = useState<(typeof recurrences)[number]>("None");
  const [error, setError] = useState<string | null>(null);

  function load() {
    if (!tenantId) return;
    void api<WorkTask[]>("/api/v1/tasks", { tenantId })
      .then(setRows)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  async function save(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId) return;
    await api("/api/v1/tasks", {
      method: "POST",
      tenantId,
      body: {
        title,
        linkedKind,
        linkedCode,
        ownerName,
        ownerEmail,
        dueOn,
        status: "Open",
        recurrence,
      },
    });
    setTitle("");
    load();
  }

  async function finish(id: string) {
    if (!tenantId) return;
    await api(`/api/v1/tasks/${id}/done`, { method: "POST", tenantId, body: {} });
    load();
  }

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("tasksPage.title")}</h1>
          <div className="sub">{t("tasksPage.sub")}</div>
        </div>
      </div>
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}
      {workspace?.canWrite && (
        <form className="card" onSubmit={(event) => void save(event)}>
          <div className="split-fields">
            <input
              value={title}
              placeholder={t("tasksPage.titleField")}
              onChange={(event) => setTitle(event.target.value)}
              required
            />
            <select
              value={linkedKind}
              onChange={(event) => setLinkedKind(event.target.value as (typeof kinds)[number])}
            >
              {kinds.map((kind) => (
                <option key={kind} value={kind}>
                  {t(`tasksPage.kinds.${kind}`)}
                </option>
              ))}
            </select>
            <input
              value={linkedCode}
              placeholder={t("tasksPage.linked")}
              onChange={(event) => setLinkedCode(event.target.value)}
            />
            <input
              value={ownerName}
              placeholder={t("tasksPage.owner")}
              onChange={(event) => setOwnerName(event.target.value)}
              required
            />
            <input
              value={ownerEmail}
              placeholder={t("tasksPage.email")}
              onChange={(event) => setOwnerEmail(event.target.value)}
              required
            />
            <input
              type="date"
              value={dueOn}
              onChange={(event) => setDueOn(event.target.value)}
              required
            />
            <select
              value={recurrence}
              onChange={(event) =>
                setRecurrence(event.target.value as (typeof recurrences)[number])
              }
            >
              {recurrences.map((item) => (
                <option key={item} value={item}>
                  {t(`tasksPage.recurrence.${item}`)}
                </option>
              ))}
            </select>
          </div>
          <button className="btn primary" type="submit">
            {t("tasksPage.assign")}
          </button>
        </form>
      )}
      <div className="card" style={{ marginTop: 14 }}>
        <table>
          <thead>
            <tr>
              <th>{t("tasksPage.titleField")}</th>
              <th>{t("tasksPage.linked")}</th>
              <th>{t("tasksPage.owner")}</th>
              <th>{t("tasksPage.due")}</th>
              <th>{t("tasksPage.status")}</th>
            </tr>
          </thead>
          <tbody>
            {(rows ?? []).map((row) => (
              <tr key={row.id} className={row.overdue ? "late" : ""}>
                <td>
                  <b>{row.title}</b>
                  <div className="id">{t(`tasksPage.recurrence.${row.recurrence}`)}</div>
                </td>
                <td>
                  {t(`tasksPage.kinds.${row.linkedKind}`)} · {row.linkedCode}
                </td>
                <td>{row.ownerName}</td>
                <td>
                  {formatDate(row.dueOn)}
                  {row.overdue && <Pill status="fail">{t("tasksPage.overdue")}</Pill>}
                </td>
                <td>
                  {row.status !== "Done" && (
                    <button className="btn" type="button" onClick={() => void finish(row.id)}>
                      {t("tasksPage.done")}
                    </button>
                  )}
                  {row.status === "Done" && (
                    <Pill status="pass">{t("tasksPage.statuses.Done")}</Pill>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}
