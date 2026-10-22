import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import {
  ApiError,
  api,
  type PersonDetail,
  type PersonListItem,
  type PersonPage,
  type Workspace,
} from "../../shared/api/client";
import { Pager } from "../../shared/ui/Pager";
import { formatDate, Pill } from "../../shared/ui/ui";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

type PersonExtras = {
  mfaEnrolled?: boolean;
  background?: string;
  backgroundOn?: string | null;
  device?: string;
  statusReason?: string;
  trainingCompletedOn?: string | null;
};

type RichPerson = PersonDetail & PersonExtras;

const states = ["Active", "Offboarding", "Offboarded"] as const;

function StatusOverride({
  person,
  tenantId,
  canWrite,
  onSaved,
}: {
  person: RichPerson;
  tenantId: string;
  canWrite: boolean;
  onSaved: () => Promise<void>;
}) {
  const { t } = useTranslation();
  const [state, setState] = useState(person.state);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  if (!canWrite) return null;

  async function save(event: React.FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api(`/api/v1/people/${person.id}/status`, {
        method: "POST",
        tenantId,
        body: { state, reason },
      });
      setReason("");
      await onSaved();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={(event) => void save(event)}>
      <h3>{t("peoplePage.override")}</h3>
      <div className="split-fields">
        <div className="field">
          <label htmlFor="person-state">{t("peoplePage.state")}</label>
          <select
            id="person-state"
            value={state}
            onChange={(event) => setState(event.target.value)}
          >
            {states.map((item) => (
              <option key={item} value={item}>
                {t(`peoplePage.states.${item}`)}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label htmlFor="person-reason">{t("peoplePage.reason")}</label>
          <input
            id="person-reason"
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            required
          />
        </div>
      </div>
      <button className="btn" type="submit" disabled={busy}>
        {t("peoplePage.saveStatus")}
      </button>
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}
    </form>
  );
}

export function PeoplePage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [result, setResult] = useState<PersonPage | null>(null);
  const [page, setPage] = useState(1);
  const [query, setQuery] = useState("");
  const [detail, setDetail] = useState<RichPerson | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = () => {
    if (!tenantId) return;
    const params = new URLSearchParams({ page: String(page), pageSize: "40" });
    if (query.trim()) params.set("q", query.trim());
    void api<PersonPage>(`/api/v1/people?${params}`, { tenantId })
      .then(setResult)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId, page, query]);

  async function open(id: string) {
    if (!tenantId) return;
    const next = await api<RichPerson>(`/api/v1/people/${id}`, { tenantId });
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
    const next = await api<RichPerson>(`/api/v1/people/${detail.id}/tasks`, {
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
      <label className="field">
        <span>{t("peoplePage.searchLabel")}</span>
        <input
          value={query}
          placeholder={t("peoplePage.search")}
          onChange={(event) => {
            setQuery(event.target.value);
            setPage(1);
          }}
        />
      </label>
      <div className="card">
        {!result ? null : result.total === 0 ? (
          <div className="empty">{t("peoplePage.empty")}</div>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>{t("peoplePage.title")}</th>
                  <th>{t("peoplePage.role")}</th>
                  <th>{t("peoplePage.hired")}</th>
                  <th>{t("peoplePage.state")}</th>
                  <th>{t("peoplePage.training")}</th>
                </tr>
              </thead>
              <tbody>
                {result.items.map((row) => (
                  <tr key={row.id}>
                    <td>
                      <button className="linkish" type="button" onClick={() => void open(row.id)}>
                        <b>{row.name}</b>
                      </button>
                      <div className="id">{row.email}</div>
                      <div className="id">{row.department}</div>
                      <div className="id">
                        {(row as PersonListItem & PersonExtras).mfaEnrolled
                          ? t("peoplePage.enrolled")
                          : t("peoplePage.missingMfa")}
                        {" · "}
                        {t(
                          `peoplePage.backgrounds.${(row as PersonListItem & PersonExtras).background ?? "NotStarted"}`,
                        )}
                        {" · "}
                        {t(
                          `peoplePage.devices.${(row as PersonListItem & PersonExtras).device ?? "Missing"}`,
                        )}
                      </div>
                      {row.leaverStillHasAccess && (
                        <Pill status="fail">{t("peoplePage.leaver")}</Pill>
                      )}
                    </td>
                    <td>{row.role}</td>
                    <td>
                      {row.hireOn ? formatDate(row.hireOn) : "—"}
                      {row.leftOn && (
                        <div className="id">
                          {t("peoplePage.left")} {formatDate(row.leftOn)}
                        </div>
                      )}
                    </td>
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
            <Pager
              page={result.page}
              pageSize={result.pageSize}
              total={result.total}
              onPage={setPage}
            />
          </div>
        )}
      </div>
      {detail && (
        <div className="card" style={{ marginTop: 14 }}>
          <h3>{detail.name}</h3>
          <div className="kv">
            <div>
              <small>{t("peoplePage.mfa")}</small>
              {detail.mfaEnrolled ? t("peoplePage.enrolled") : t("peoplePage.missingMfa")}
            </div>
            <div>
              <small>{t("peoplePage.background")}</small>
              {t(`peoplePage.backgrounds.${detail.background ?? "NotStarted"}`)}
              {detail.backgroundOn ? ` · ${formatDate(detail.backgroundOn)}` : ""}
            </div>
            <div>
              <small>{t("peoplePage.device")}</small>
              {t(`peoplePage.devices.${detail.device ?? "Missing"}`)}
            </div>
            <div>
              <small>{t("peoplePage.training")}</small>
              {t(`peoplePage.trainingStates.${detail.training}`)}
              {detail.trainingCompletedOn ? ` · ${formatDate(detail.trainingCompletedOn)}` : ""}
            </div>
            <div>
              <small>{t("peoplePage.hired")}</small>
              {detail.hireOn ? formatDate(detail.hireOn) : "—"}
            </div>
            <div>
              <small>{t("peoplePage.left")}</small>
              {detail.leftOn ? formatDate(detail.leftOn) : "—"}
            </div>
          </div>
          {detail.statusReason && (
            <p className="m">
              {t("peoplePage.reason")}: {detail.statusReason}
            </p>
          )}
          {tenantId && (
            <StatusOverride
              key={detail.id}
              person={detail}
              tenantId={tenantId}
              canWrite={Boolean(workspace?.canWrite)}
              onSaved={async () => {
                setNotice(t("peoplePage.savedStatus"));
                await open(detail.id);
                load();
              }}
            />
          )}
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
