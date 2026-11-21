import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import { ApiError, api, type Workspace } from "../../shared/api/client";
import { formatWhen, Pill } from "../../shared/ui/ui";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

type Change = {
  id: string;
  code: string;
  title: string;
  source: string;
  externalKey: string;
  repository: string;
  authorName: string;
  status: string;
  approverName: string;
  approvedAt: string | null;
  deployRef: string;
  deployedAt: string | null;
  openedAt: string;
  reviewed: boolean;
};

const sources = ["github", "jira", "manual"] as const;

function tone(status: string) {
  if (status === "Deployed" || status === "Approved") return "pass";
  if (status === "Rejected") return "fail";
  return "warn";
}

export function ChangesPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [rows, setRows] = useState<Change[] | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const [draft, setDraft] = useState({
    title: "",
    source: "manual",
    externalKey: "",
    repository: "",
  });
  const [deployRef, setDeployRef] = useState("");
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  function load() {
    if (!tenantId) return;
    void api<Change[]>("/api/v1/changes", { tenantId })
      .then(setRows)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  const current = rows?.find((item) => item.id === selected) ?? null;

  async function record(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId) return;
    try {
      await api("/api/v1/changes", { method: "POST", tenantId, body: draft });
      setDraft({ title: "", source: "manual", externalKey: "", repository: "" });
      setNotice(t("changesPage.saved"));
      setError(null);
      load();
    } catch {
      setError("save");
    }
  }

  async function act(action: string) {
    if (!tenantId || !current) return;
    try {
      await api(`/api/v1/changes/${current.id}`, {
        method: "POST",
        tenantId,
        body: { action, deployRef },
      });
      setDeployRef("");
      setError(null);
      load();
    } catch {
      setError("save");
    }
  }

  async function sync() {
    if (!tenantId) return;
    try {
      const result = await api<{ imported: number }>("/api/v1/changes/sync", {
        method: "POST",
        tenantId,
      });
      setNotice(t("changesPage.synced", { count: result.imported }));
      setError(null);
      load();
    } catch {
      setError("save");
    }
  }

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("changesPage.title")}</h1>
          <div className="sub">{t("changesPage.sub")}</div>
        </div>
        {workspace?.canWrite && (
          <button className="btn" type="button" onClick={() => void sync()}>
            {t("changesPage.sync")}
          </button>
        )}
      </div>
      {error && (
        <div className="error" role="alert">
          {t(error === "save" ? "errors.save" : "errors.load")}
        </div>
      )}
      {notice && (
        <div className="notice" role="status">
          {notice}
        </div>
      )}
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>{t("changesPage.code")}</th>
              <th>{t("changesPage.source")}</th>
              <th>{t("changesPage.status")}</th>
              <th>{t("changesPage.review")}</th>
              <th>{t("changesPage.deploy")}</th>
            </tr>
          </thead>
          <tbody>
            {rows?.map((row) => (
              <tr key={row.id} onClick={() => setSelected(row.id)} style={{ cursor: "pointer" }}>
                <td>
                  <b>{row.code}</b>
                  <div>{row.title}</div>
                </td>
                <td>
                  {t(`changesPage.sources.${row.source}`)}
                  <div>{row.externalKey}</div>
                </td>
                <td>
                  <Pill status={tone(row.status)}>{t(`changesPage.statuses.${row.status}`)}</Pill>
                </td>
                <td>
                  {row.reviewed ? row.approverName : t("changesPage.unreviewed")}
                  {row.approvedAt && <div>{formatWhen(row.approvedAt)}</div>}
                </td>
                <td>
                  {row.deployRef || "—"}
                  {row.deployedAt && <div>{formatWhen(row.deployedAt)}</div>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {current && (
        <section className="card">
          <h3>
            {current.code} · {current.title}
          </h3>
          <p>
            {t("changesPage.repository")}: {current.repository || "—"} · {t("changesPage.author")}:{" "}
            {current.authorName}
          </p>
          {workspace?.canWrite && (
            <>
              <label className="field">
                <span>{t("changesPage.deployRef")}</span>
                <input value={deployRef} onChange={(event) => setDeployRef(event.target.value)} />
              </label>
              <div className="act">
                <button className="btn" type="button" onClick={() => void act("approve")}>
                  {t("changesPage.approve")}
                </button>
                <button className="btn" type="button" onClick={() => void act("deploy")}>
                  {t("changesPage.linkDeploy")}
                </button>
                <button className="btn" type="button" onClick={() => void act("reject")}>
                  {t("changesPage.reject")}
                </button>
              </div>
            </>
          )}
        </section>
      )}
      {workspace?.canWrite && (
        <form className="card" onSubmit={(event) => void record(event)}>
          <h3>{t("changesPage.record")}</h3>
          <label className="field">
            <span>{t("changesPage.titleField")}</span>
            <input
              value={draft.title}
              aria-label={t("changesPage.titleField")}
              onChange={(event) => setDraft({ ...draft, title: event.target.value })}
            />
          </label>
          <label className="field">
            <span>{t("changesPage.source")}</span>
            <select
              value={draft.source}
              aria-label={t("changesPage.source")}
              onChange={(event) => setDraft({ ...draft, source: event.target.value })}
            >
              {sources.map((item) => (
                <option key={item} value={item}>
                  {t(`changesPage.sources.${item}`)}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            <span>{t("changesPage.key")}</span>
            <input
              value={draft.externalKey}
              aria-label={t("changesPage.key")}
              onChange={(event) => setDraft({ ...draft, externalKey: event.target.value })}
            />
          </label>
          <label className="field">
            <span>{t("changesPage.repository")}</span>
            <input
              value={draft.repository}
              aria-label={t("changesPage.repository")}
              onChange={(event) => setDraft({ ...draft, repository: event.target.value })}
            />
          </label>
          <button className="btn primary" type="submit">
            {t("changesPage.record")}
          </button>
        </form>
      )}
    </>
  );
}
