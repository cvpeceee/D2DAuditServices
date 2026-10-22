import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import {
  ApiError,
  api,
  type AccessCampaignDetail,
  type AccessCampaignListItem,
  type Workspace,
} from "../../shared/api/client";
import { Pager } from "../../shared/ui/Pager";
import { formatDate, Pill } from "../../shared/ui/ui";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

export function AccessReviewsPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [rows, setRows] = useState<AccessCampaignListItem[] | null>(null);
  const [detail, setDetail] = useState<AccessCampaignDetail | null>(null);
  const [name, setName] = useState("");
  const [dueOn, setDueOn] = useState(new Date().toISOString().slice(0, 10));
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [itemQuery, setItemQuery] = useState("");
  const [itemPage, setItemPage] = useState(1);

  const load = () => {
    if (!tenantId) return;
    void api<AccessCampaignListItem[]>("/api/v1/access-reviews", { tenantId })
      .then(setRows)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  async function open(id: string) {
    if (!tenantId) return;
    setItemQuery("");
    setItemPage(1);
    setDetail(await api<AccessCampaignDetail>(`/api/v1/access-reviews/${id}`, { tenantId }));
  }

  async function start(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId) return;
    setBusy(true);
    setError(null);
    try {
      const created = await api<AccessCampaignDetail>("/api/v1/access-reviews", {
        method: "POST",
        tenantId,
        body: { name, dueOn },
      });
      setDetail(created);
      setItemQuery("");
      setItemPage(1);
      setName("");
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  async function decide(itemId: string, decision: "Approved" | "Revoked") {
    if (!tenantId || !detail) return;
    const next = await api<AccessCampaignDetail>(
      `/api/v1/access-reviews/${detail.id}/items/${itemId}`,
      {
        method: "POST",
        tenantId,
        body: { decision },
      },
    );
    setDetail(next);
    load();
  }

  async function complete() {
    if (!tenantId || !detail) return;
    setBusy(true);
    try {
      const next = await api<AccessCampaignDetail>(`/api/v1/access-reviews/${detail.id}/complete`, {
        method: "POST",
        tenantId,
        body: {},
      });
      setDetail(next);
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  const pending = detail?.items.some((item) => item.decision === "Pending") ?? true;

  const accountTerm = itemQuery.trim().toLowerCase();
  const matching = (detail?.items ?? []).filter((item) => {
    if (!accountTerm) return true;
    return `${item.personName} ${item.personEmail} ${item.provider} ${item.accountName}`
      .toLowerCase()
      .includes(accountTerm);
  });
  const accountPage = matching.slice((itemPage - 1) * 25, itemPage * 25);

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("accessPage.title")}</h1>
          <div className="sub">{t("accessPage.sub")}</div>
        </div>
      </div>
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}
      {workspace?.canWrite && (
        <form className="card" onSubmit={(event) => void start(event)}>
          <div className="split-fields">
            <div className="field">
              <label htmlFor="campaign-name">{t("accessPage.name")}</label>
              <input
                id="campaign-name"
                value={name}
                onChange={(event) => setName(event.target.value)}
                required
              />
            </div>
            <div className="field">
              <label htmlFor="campaign-due">{t("accessPage.due")}</label>
              <input
                id="campaign-due"
                type="date"
                value={dueOn}
                onChange={(event) => setDueOn(event.target.value)}
                required
              />
            </div>
          </div>
          <button className="btn primary" type="submit" disabled={busy}>
            {t("accessPage.start")}
          </button>
        </form>
      )}
      <div className="card" style={{ marginTop: 14 }}>
        {!rows ? null : rows.length === 0 ? (
          <div className="empty">{t("accessPage.empty")}</div>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>{t("accessPage.name")}</th>
                  <th>{t("accessPage.due")}</th>
                  <th>{t("accessPage.progress")}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.id}>
                    <td>
                      <button className="linkish" type="button" onClick={() => void open(row.id)}>
                        <b>{row.name}</b>
                      </button>
                    </td>
                    <td className="m">{formatDate(row.dueOn)}</td>
                    <td>
                      <Pill status={row.state === "Completed" ? "pass" : "warn"}>
                        {t(`accessPage.states.${row.state}`)}
                      </Pill>
                      <div className="id">
                        {row.total - row.pending}/{row.total}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
      {detail && (
        <div className="card" style={{ marginTop: 14 }}>
          <h3>{detail.name}</h3>
          <label className="field">
            <span>{t("accessPage.search")}</span>
            <input
              value={itemQuery}
              onChange={(event) => {
                setItemQuery(event.target.value);
                setItemPage(1);
              }}
            />
          </label>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>{t("accessPage.person")}</th>
                  <th>{t("accessPage.account")}</th>
                  <th>{t("accessPage.decision")}</th>
                </tr>
              </thead>
              <tbody>
                {accountPage.map((item) => (
                  <tr key={item.id}>
                    <td>
                      <b>{item.personName}</b>
                      <div className="id">{item.personEmail}</div>
                    </td>
                    <td>
                      {item.provider} · {item.accountName}
                    </td>
                    <td>
                      <Pill
                        status={
                          item.decision === "Revoked"
                            ? "fail"
                            : item.decision === "Approved"
                              ? "pass"
                              : "warn"
                        }
                      >
                        {t(`accessPage.decisions.${item.decision}`)}
                      </Pill>
                      {workspace?.canWrite && detail.state === "Open" && (
                        <div className="act">
                          <button
                            className="btn"
                            type="button"
                            onClick={() => void decide(item.id, "Approved")}
                          >
                            {t("accessPage.approve")}
                          </button>
                          <button
                            className="btn"
                            type="button"
                            onClick={() => void decide(item.id, "Revoked")}
                          >
                            {t("accessPage.revoke")}
                          </button>
                        </div>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            <Pager page={itemPage} pageSize={25} total={matching.length} onPage={setItemPage} />
          </div>
          {detail.evidenceSha256 && (
            <p>
              {t("accessPage.evidence")}: <code>{detail.evidenceSha256}</code>
            </p>
          )}
          {workspace?.canWrite && detail.state === "Open" && (
            <button
              className="btn primary"
              type="button"
              disabled={busy || pending}
              onClick={() => void complete()}
            >
              {t("accessPage.complete")}
            </button>
          )}
        </div>
      )}
    </>
  );
}
