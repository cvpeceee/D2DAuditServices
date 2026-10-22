import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import { ApiError, api, download, type SoaRow, type Workspace } from "../../shared/api/client";
import { Pill } from "../../shared/ui/ui";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

const statuses = ["NotStarted", "InProgress", "Implemented", "NotApplicable"] as const;

function tone(status: string) {
  if (status === "Implemented") return "pass";
  if (status === "InProgress") return "info";
  if (status === "NotApplicable") return undefined;
  return "warn";
}

export function SoaPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [rows, setRows] = useState<SoaRow[] | null>(null);
  const [selected, setSelected] = useState<SoaRow | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!tenantId) return;
    void api<SoaRow[]>("/api/v1/soa", { tenantId })
      .then(setRows)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  }, [tenantId]);

  async function save(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId || !selected) return;
    setBusy(true);
    setError(null);
    try {
      const next = await api<SoaRow>(`/api/v1/soa/${selected.code}`, {
        method: "PUT",
        tenantId,
        body: {
          applicable: selected.applicable,
          justification: selected.justification,
          implementation: selected.applicable ? selected.implementation : "NotApplicable",
        },
      });
      setRows((current) => current?.map((row) => (row.code === next.code ? next : row)) ?? [next]);
      setSelected(next);
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("soaPage.title")}</h1>
          <div className="sub">{t("soaPage.sub")}</div>
        </div>
        <div className="act">
          <button
            className="btn"
            type="button"
            onClick={() => void download("/api/v1/soa/export?format=csv", tenantId)}
          >
            {t("soaPage.exportCsv")}
          </button>
          <button
            className="btn"
            type="button"
            onClick={() => void download("/api/v1/soa/export?format=pdf", tenantId)}
          >
            {t("soaPage.exportPdf")}
          </button>
        </div>
      </div>
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}
      <div className="card">
        {!rows ? null : rows.length === 0 ? (
          <div className="empty">{t("soaPage.empty")}</div>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>{t("soaPage.code")}</th>
                  <th>{t("soaPage.applicable")}</th>
                  <th>{t("soaPage.implementation")}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.code}>
                    <td>
                      <button className="linkish" type="button" onClick={() => setSelected(row)}>
                        <b>{row.code}</b> {row.title}
                      </button>
                    </td>
                    <td>{row.applicable ? t("soaPage.yes") : t("soaPage.no")}</td>
                    <td>
                      <Pill status={tone(row.implementation)}>
                        {t(`soaPage.statuses.${row.implementation}`)}
                      </Pill>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
      {selected && (
        <form className="card" style={{ marginTop: 14 }} onSubmit={(event) => void save(event)}>
          <h3>
            {selected.code} {selected.title}
          </h3>
          <label className="check-row">
            <input
              type="checkbox"
              checked={selected.applicable}
              disabled={!workspace?.canWrite}
              onChange={(event) => setSelected({ ...selected, applicable: event.target.checked })}
            />
            {t("soaPage.applicable")}
          </label>
          <div className="field">
            <label htmlFor="soa-why">{t("soaPage.justification")}</label>
            <textarea
              id="soa-why"
              value={selected.justification}
              disabled={!workspace?.canWrite}
              onChange={(event) => setSelected({ ...selected, justification: event.target.value })}
              required
            />
          </div>
          <div className="field">
            <label htmlFor="soa-status">{t("soaPage.implementation")}</label>
            <select
              id="soa-status"
              value={selected.implementation}
              disabled={!workspace?.canWrite || !selected.applicable}
              onChange={(event) => setSelected({ ...selected, implementation: event.target.value })}
            >
              {statuses.map((status) => (
                <option key={status} value={status}>
                  {t(`soaPage.statuses.${status}`)}
                </option>
              ))}
            </select>
          </div>
          {workspace?.canWrite && (
            <button className="btn primary" type="submit" disabled={busy}>
              {t("soaPage.save")}
            </button>
          )}
        </form>
      )}
    </>
  );
}
