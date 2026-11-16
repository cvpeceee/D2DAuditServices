import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import { ApiError, api, type Workspace } from "../../shared/api/client";
import { formatWhen } from "../../shared/ui/ui";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

type Description = { body: string; updatedAt: string; updatedBy: string };

export function SystemDescriptionPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [row, setRow] = useState<Description | null>(null);
  const [body, setBody] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    if (!tenantId) return;
    void api<Description>("/api/v1/system-description", { tenantId })
      .then((next) => {
        setRow(next);
        setBody(next.body);
      })
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  }, [tenantId]);

  async function save(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId) return;
    const next = await api<Description>("/api/v1/system-description", {
      method: "POST",
      tenantId,
      body: { body },
    });
    setRow(next);
    setBody(next.body);
    setNotice(t("descriptionPage.saved"));
  }

  async function redraft() {
    if (!tenantId) return;
    const next = await api<Description>("/api/v1/system-description/redraft", {
      method: "POST",
      tenantId,
    });
    setRow(next);
    setBody(next.body);
    setNotice(t("descriptionPage.redrafted"));
  }

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("descriptionPage.title")}</h1>
          <div className="sub">{t("descriptionPage.sub")}</div>
        </div>
      </div>
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}
      {notice && (
        <div className="notice" role="status">
          {notice}
        </div>
      )}
      {row && (
        <form className="card" onSubmit={(event) => void save(event)}>
          <p>
            {t("descriptionPage.updated", {
              name: row.updatedBy,
              when: formatWhen(row.updatedAt),
            })}
          </p>
          <label className="field">
            <span>{t("descriptionPage.body")}</span>
            <textarea
              aria-label={t("descriptionPage.body")}
              rows={16}
              value={body}
              readOnly={!workspace?.canWrite}
              onChange={(event) => setBody(event.target.value)}
            />
          </label>
          {workspace?.canWrite && (
            <div className="act">
              <button className="btn primary" type="submit">
                {t("descriptionPage.save")}
              </button>
              <button className="btn" type="button" onClick={() => void redraft()}>
                {t("descriptionPage.redraft")}
              </button>
            </div>
          )}
        </form>
      )}
    </>
  );
}
