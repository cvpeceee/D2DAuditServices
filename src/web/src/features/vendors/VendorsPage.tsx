import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import { ApiError, api, type VendorListItem, type Workspace } from "../../shared/api/client";
import { formatDate, Pill } from "../../shared/ui/ui";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

const reviews = ["NotStarted", "InReview", "Approved", "Expired"] as const;

function tone(state: string) {
  if (state === "Approved") return "pass";
  if (state === "Expired") return "fail";
  if (state === "InReview") return "info";
  return "warn";
}

export function VendorsPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [rows, setRows] = useState<VendorListItem[] | null>(null);
  const [selected, setSelected] = useState<VendorListItem | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = () => {
    if (!tenantId) return;
    void api<VendorListItem[]>("/api/v1/vendors", { tenantId })
      .then(setRows)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  async function save(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId || !selected) return;
    setBusy(true);
    setError(null);
    try {
      await api("/api/v1/vendors", { method: "POST", tenantId, body: selected });
      setSelected(null);
      load();
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
          <h1>{t("vendorsPage.title")}</h1>
          <div className="sub">{t("vendorsPage.sub")}</div>
        </div>
      </div>
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}
      <div className="card">
        {!rows ? null : rows.length === 0 ? (
          <div className="empty">{t("vendorsPage.empty")}</div>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>{t("vendorsPage.name")}</th>
                  <th>{t("vendorsPage.criticality")}</th>
                  <th>{t("vendorsPage.review")}</th>
                  <th>{t("vendorsPage.due")}</th>
                  <th>{t("vendorsPage.rating")}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.id}>
                    <td>
                      <button className="linkish" type="button" onClick={() => setSelected(row)}>
                        <b>{row.name}</b>
                      </button>
                      <div className="id">{row.service}</div>
                    </td>
                    <td>{t(`vendorsPage.criticalities.${row.criticality}`)}</td>
                    <td>
                      <Pill status={tone(row.reviewState)}>
                        {t(`vendorsPage.reviews.${row.reviewState}`)}
                      </Pill>
                    </td>
                    <td className="m">{formatDate(row.reviewDueOn)}</td>
                    <td>{row.riskRating}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
      {selected && (
        <form className="card" style={{ marginTop: 14 }} onSubmit={(event) => void save(event)}>
          <h3>{selected.name}</h3>
          <p className="sub">{selected.dataAccess}</p>
          <h3>{t("vendorsPage.documents")}</h3>
          {selected.documents.length === 0 ? (
            <div className="empty">{t("vendorsPage.documents")}</div>
          ) : (
            <ul>
              {selected.documents.map((document) => (
                <li key={document.title}>
                  {document.title}
                  {document.issuedOn ? ` · ${formatDate(document.issuedOn)}` : ""}
                </li>
              ))}
            </ul>
          )}
          <div className="split-fields">
            <div className="field">
              <label htmlFor="vendor-review">{t("vendorsPage.review")}</label>
              <select
                id="vendor-review"
                value={selected.reviewState}
                disabled={!workspace?.canWrite}
                onChange={(event) => setSelected({ ...selected, reviewState: event.target.value })}
              >
                {reviews.map((review) => (
                  <option key={review} value={review}>
                    {t(`vendorsPage.reviews.${review}`)}
                  </option>
                ))}
              </select>
            </div>
            <div className="field">
              <label htmlFor="vendor-due">{t("vendorsPage.due")}</label>
              <input
                id="vendor-due"
                type="date"
                value={selected.reviewDueOn}
                disabled={!workspace?.canWrite}
                onChange={(event) => setSelected({ ...selected, reviewDueOn: event.target.value })}
              />
            </div>
          </div>
          {workspace?.canWrite && (
            <button className="btn primary" type="submit" disabled={busy}>
              {t("vendorsPage.save")}
            </button>
          )}
        </form>
      )}
    </>
  );
}
