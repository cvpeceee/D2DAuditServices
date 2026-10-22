import { useState } from "react";
import { useTranslation } from "react-i18next";
import { ApiError, api } from "../../shared/api/client";

export function AssignTask({
  tenantId,
  linkedKind,
  linkedCode,
  canWrite,
}: {
  tenantId: string | null;
  linkedKind: string;
  linkedCode: string;
  canWrite: boolean;
}) {
  const { t } = useTranslation();
  const [title, setTitle] = useState("");
  const [ownerName, setOwnerName] = useState("");
  const [ownerEmail, setOwnerEmail] = useState("");
  const [dueOn, setDueOn] = useState(new Date().toISOString().slice(0, 10));
  const [notice, setNotice] = useState<string | null>(null);

  if (!canWrite) return null;

  async function save(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId) return;
    setNotice(null);
    try {
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
          recurrence: "None",
        },
      });
      setTitle("");
      setNotice(t("tasksPage.assigned"));
    } catch (caught: unknown) {
      setNotice(caught instanceof ApiError ? caught.code : "load");
    }
  }

  return (
    <form className="stack" onSubmit={(event) => void save(event)}>
      <div className="sec" style={{ paddingInlineStart: 0 }}>
        {t("tasksPage.assign")}
      </div>
      <input
        value={title}
        placeholder={t("tasksPage.titleField")}
        onChange={(event) => setTitle(event.target.value)}
        required
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
      <button className="btn" type="submit">
        {t("tasksPage.assign")}
      </button>
      {notice && <div className="notice">{notice === "load" ? t("errors.load") : notice}</div>}
    </form>
  );
}
