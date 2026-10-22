import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import {
  ApiError,
  api,
  type PersonDetail,
  type PersonListItem,
  type Workspace,
} from "../../shared/api/client";
import { formatDate, Pill } from "../../shared/ui/ui";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

export function PeoplePage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [rows, setRows] = useState<PersonListItem[] | null>(null);
  const [detail, setDetail] = useState<PersonDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = () => {
    if (!tenantId) return;
    void api<PersonListItem[]>("/api/v1/people", { tenantId })
      .then(setRows)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  async function open(id: string) {
    if (!tenantId) return;
    const next = await api<PersonDetail>(`/api/v1/people/${id}`, { tenantId });
    setDetail(next);
  }

  async function sync() {
    if (!tenantId) return;
    setBusy(true);
    setError(null);
    try {
      await api("/api/v1/people/sync", { method: "POST", tenantId, body: {} });
      setNotice(t("peoplePage.synced"));
      setDetail(null);
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  async function toggle(taskCode: string, done: boolean) {
    if (!tenantId || !detail) return;
    const next = await api<PersonDetail>(`/api/v1/people/${detail.id}/tasks`, {
      method: "POST",
      tenantId,
      body: { taskCode, done },
    });
    setDetail(next);
    load();
  }

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("peoplePage.title")}</h1>
          <div className="sub">{t("peoplePage.sub")}</div>
        </div>
        {workspace?.canWrite && (
          <button className="btn primary" type="button" disabled={busy} onClick={() => void sync()}>
            {t("peoplePage.sync")}
          </button>
        )}
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
      <div className="card">
        {!rows ? null : rows.length === 0 ? (
          <div className="empty">{t("peoplePage.empty")}</div>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>{t("peoplePage.title")}</th>
                  <th>{t("peoplePage.source")}</th>
                  <th>{t("peoplePage.state")}</th>
                  <th>{t("peoplePage.training")}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.id}>
                    <td>
                      <button className="linkish" type="button" onClick={() => void open(row.id)}>
                        <b>{row.name}</b>
                      </button>
                      <div className="id">{row.email}</div>
                      {row.leaverStillHasAccess && (
                        <Pill status="fail">{t("peoplePage.leaver")}</Pill>
                      )}
                    </td>
                    <td>{t(`peoplePage.sources.${row.source}`, { defaultValue: row.source })}</td>
                    <td>
                      <Pill status={row.state === "Offboarded" ? "fail" : "pass"}>
                        {t(`peoplePage.states.${row.state}`)}
                      </Pill>
                    </td>
                    <td>
                      <Pill
                        status={
                          row.training === "Overdue"
                            ? "warn"
                            : row.training === "Complete"
                              ? "pass"
                              : "info"
                        }
                      >
                        {t(`peoplePage.trainingStates.${row.training}`)}
                      </Pill>
                      {row.trainingDueOn && (
                        <div className="id">{formatDate(row.trainingDueOn)}</div>
                      )}
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
          {detail.leaverStillHasAccess && (
            <div className="error" role="status">
              {t("peoplePage.leaver")}
            </div>
          )}
          <h3>{t("peoplePage.access")}</h3>
          <ul>
            {detail.accounts.map((account) => (
              <li key={`${account.provider}-${account.accountName}`}>
                {account.provider} · {account.accountName} ·{" "}
                {account.active ? t("peoplePage.states.Active") : t("peoplePage.states.Offboarded")}
              </li>
            ))}
          </ul>
          <h3>{t("peoplePage.checklist")}</h3>
          {(["Onboarding", "Offboarding"] as const).map((kind) => (
            <div key={kind}>
              <div className="m">
                {t(`peoplePage.${kind === "Onboarding" ? "onboarding" : "offboarding"}`)}
              </div>
              {detail.tasks
                .filter((task) => task.kind === kind)
                .map((task) => (
                  <label key={task.taskCode} className="check-row">
                    <input
                      type="checkbox"
                      checked={task.done}
                      disabled={!workspace?.canWrite}
                      onChange={(event) => void toggle(task.taskCode, event.target.checked)}
                    />
                    {t(`peoplePage.tasks.${task.taskCode}`)}
                  </label>
                ))}
            </div>
          ))}
          <h3>{t("peoplePage.acknowledgements")}</h3>
          {detail.acknowledgements.length === 0 ? (
            <div className="empty">{t("peoplePage.waiting")}</div>
          ) : (
            <ul>
              {detail.acknowledgements.map((item) => (
                <li key={`${item.policyCode}-${item.dueOn}`}>
                  {item.policyCode} {item.policyTitle} ·{" "}
                  {item.acknowledgedAt ? t("peoplePage.acknowledged") : t("peoplePage.waiting")}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </>
  );
}
