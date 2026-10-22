import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import { ApiError, api, download, type Workspace } from "../../shared/api/client";
import { Pager } from "../../shared/ui/Pager";
import { formatDate, Pill } from "../../shared/ui/ui";
import type { AssetItem, AssetPage } from "./types";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

export function AssetsPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [result, setResult] = useState<AssetPage | null>(null);
  const [page, setPage] = useState(1);
  const [query, setQuery] = useState("");
  const [selected, setSelected] = useState<AssetItem | null>(null);
  const [csv, setCsv] = useState(
    "name,type,source,owner,ownerEmail,environment,classification,inScope\n",
  );
  const [error, setError] = useState<string | null>(null);

  function load() {
    if (!tenantId) return;
    const params = new URLSearchParams({ page: String(page), pageSize: "40" });
    if (query.trim()) params.set("q", query.trim());
    void api<AssetPage>(`/api/v1/assets?${params}`, { tenantId })
      .then(setResult)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId, page, query]);

  async function save(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId || !selected) return;
    await api("/api/v1/assets", { method: "POST", tenantId, body: selected });
    setSelected(null);
    load();
  }

  async function importCsv(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId) return;
    await api("/api/v1/assets/import", { method: "POST", tenantId, body: { csv } });
    load();
  }

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("assetsPage.title")}</h1>
          <div className="sub">{t("assetsPage.sub")}</div>
        </div>
        <div className="act">
          <button
            className="btn"
            type="button"
            onClick={() => tenantId && void download("/api/v1/assets/export", tenantId)}
          >
            {t("assetsPage.export")}
          </button>
        </div>
      </div>
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}
      <label className="field">
        <span>{t("assetsPage.searchLabel")}</span>
        <input
          value={query}
          placeholder={t("assetsPage.search")}
          onChange={(event) => {
            setQuery(event.target.value);
            setPage(1);
          }}
        />
      </label>
      <div className="card">
        {result && result.total === 0 ? (
          <div className="empty">{t("assetsPage.empty")}</div>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>{t("assetsPage.name")}</th>
                  <th>{t("assetsPage.type")}</th>
                  <th>{t("assetsPage.source")}</th>
                  <th>{t("assetsPage.owner")}</th>
                  <th>{t("assetsPage.environment")}</th>
                  <th>{t("assetsPage.classification")}</th>
                  <th>{t("assetsPage.scope")}</th>
                  <th>{t("assetsPage.seen")}</th>
                </tr>
              </thead>
              <tbody>
                {(result?.items ?? []).map((row) => (
                  <tr key={row.id}>
                    <td>
                      <button className="linkish" type="button" onClick={() => setSelected(row)}>
                        <b>{row.name}</b>
                      </button>
                    </td>
                    <td>{t(`assetsPage.types.${row.type}`)}</td>
                    <td>{row.source}</td>
                    <td>{row.ownerName}</td>
                    <td>{row.environment}</td>
                    <td>{t(`assetsPage.classes.${row.classification}`)}</td>
                    <td>
                      {row.inScope ? (
                        <Pill status="pass">{t("assetsPage.inScope")}</Pill>
                      ) : (
                        <Pill status="warn">{t("assetsPage.outOfScope")}</Pill>
                      )}
                    </td>
                    <td>{formatDate(row.lastSeen.slice(0, 10))}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {result && (
              <Pager
                page={result.page}
                pageSize={result.pageSize}
                total={result.total}
                onPage={setPage}
              />
            )}
          </div>
        )}
      </div>
      {selected && (
        <form className="card" style={{ marginTop: 14 }} onSubmit={(event) => void save(event)}>
          <h3>{selected.name}</h3>
          <div className="split-fields">
            <input
              value={selected.ownerName}
              disabled={!workspace?.canWrite}
              onChange={(event) => setSelected({ ...selected, ownerName: event.target.value })}
            />
            <input
              value={selected.ownerEmail}
              disabled={!workspace?.canWrite}
              onChange={(event) => setSelected({ ...selected, ownerEmail: event.target.value })}
            />
          </div>
          <label className="check-row">
            <input
              type="checkbox"
              checked={selected.inScope}
              disabled={!workspace?.canWrite}
              onChange={(event) => setSelected({ ...selected, inScope: event.target.checked })}
            />
            {t("assetsPage.inScope")}
          </label>
          {workspace?.canWrite && (
            <button className="btn primary" type="submit">
              {t("assetsPage.save")}
            </button>
          )}
        </form>
      )}
      {workspace?.canWrite && (
        <form
          className="card"
          style={{ marginTop: 14 }}
          onSubmit={(event) => void importCsv(event)}
        >
          <h3>{t("assetsPage.import")}</h3>
          <textarea value={csv} onChange={(event) => setCsv(event.target.value)} rows={4} />
          <button className="btn" type="submit">
            {t("assetsPage.import")}
          </button>
        </form>
      )}
    </>
  );
}
