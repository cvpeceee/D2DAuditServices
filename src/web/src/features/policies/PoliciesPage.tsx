import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import {
  ApiError,
  api,
  type Job,
  type PolicyDetail,
  type PolicyListItem,
  type Workspace,
} from "../../shared/api/client";
import { Pager } from "../../shared/ui/Pager";
import { formatDate, formatWhen, Pill } from "../../shared/ui/ui";
import { AssignTask } from "../program/AssignTask";

type ShellContext = { workspace: Workspace | null; tenantId: string | null; jobs: Job[] };

function tone(state: string) {
  if (state === "Approved" || state === "Accepted") return "pass";
  if (state === "InReview" || state === "Submitted") return "info";
  if (state === "Open") return "warn";
  return undefined;
}

export function PoliciesPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [rows, setRows] = useState<PolicyListItem[] | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const [detail, setDetail] = useState<PolicyDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [ackQuery, setAckQuery] = useState("");
  const [ackPage, setAckPage] = useState(1);

  const load = () => {
    if (!tenantId) return;
    void api<PolicyListItem[]>("/api/v1/policies", { tenantId })
      .then(setRows)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  };

  useEffect(() => {
    load();
    // The list is loaded once for this workspace.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  useEffect(() => {
    setAckQuery("");
    setAckPage(1);
    if (!tenantId || !selected) {
      setDetail(null);
      return;
    }
    void api<PolicyDetail>(`/api/v1/policies/${selected}`, { tenantId })
      .then(setDetail)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  }, [selected, tenantId]);

  async function act(path: string) {
    if (!tenantId) return;
    setBusy(true);
    setError(null);
    try {
      const next = await api<PolicyDetail | number>(path, { method: "POST", tenantId, body: {} });
      if (typeof next === "object" && next && "code" in next) setDetail(next);
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  const latest = detail?.versions[0];
  const ackTerm = ackQuery.trim().toLowerCase();
  const matchingAcks = (detail?.acknowledgements ?? []).filter((item) => {
    if (!ackTerm) return true;
    return `${item.personName} ${item.personEmail}`.toLowerCase().includes(ackTerm);
  });
  const ackRows = matchingAcks.slice((ackPage - 1) * 20, ackPage * 20);
  const mine = detail?.acknowledgements.find(
    (item) =>
      item.personEmail.toLowerCase() === (workspace?.email ?? "").toLowerCase() &&
      !item.acknowledgedAt,
  );

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("policiesPage.title")}</h1>
          <div className="sub">{t("policiesPage.sub")}</div>
        </div>
        {workspace?.canWrite && (
          <div className="act">
            <button
              className="btn"
              type="button"
              disabled={busy}
              onClick={() => void act("/api/v1/policies/reminders")}
            >
              {t("policiesPage.remind")}
            </button>
          </div>
        )}
      </div>
      {error && (
        <div className="error" role="alert">
          {t(`policiesPage.errors.${error}`, { defaultValue: t("errors.load") })}
        </div>
      )}
      <div className="grid" style={{ gridTemplateColumns: "minmax(240px, 0.9fr) 1.4fr" }}>
        <div className="card" style={{ paddingBottom: 6 }}>
          <table>
            <thead>
              <tr>
                <th>{t("policiesPage.policy")}</th>
                <th>{t("policiesPage.state")}</th>
              </tr>
            </thead>
            <tbody>
              {rows?.map((row) => (
                <tr key={row.code} className={selected === row.code ? "sel" : ""}>
                  <td>
                    <button type="button" className="row-hit" onClick={() => setSelected(row.code)}>
                      <span className="id">{row.code}</span> {row.title}
                    </button>
                    <div className="m">
                      {t("policiesPage.version", { number: row.version })} ·{" "}
                      {row.controlCodes.join(", ") || "—"}
                      {Number(row.outstanding) > 0
                        ? ` · ${t("policiesPage.outstanding", { count: row.outstanding })}`
                        : ""}
                    </div>
                  </td>
                  <td>
                    <Pill status={tone(row.state)}>
                      {t(`policiesPage.states.${row.state}`, { defaultValue: row.state })}
                    </Pill>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {rows?.length === 0 && <div className="empty">{t("policiesPage.empty")}</div>}
        </div>
        <div className="card">
          {!detail || !latest ? (
            <div className="empty">{t("policiesPage.pick")}</div>
          ) : (
            <>
              <div className="row">
                <h3>
                  {detail.code} · {detail.title}
                </h3>
                <span style={{ marginInlineStart: "auto" }}>
                  <Pill status={tone(latest.state)}>
                    {t(`policiesPage.states.${latest.state}`, { defaultValue: latest.state })}
                  </Pill>
                </span>
              </div>
              <p>{latest.body}</p>
              <div className="m">
                {t("policiesPage.owner")}: {detail.ownerName}
                {detail.controlCodes.length > 0 ? ` · ${detail.controlCodes.join(", ")}` : ""}
              </div>
              {workspace?.canWrite && latest.state === "Approved" && (
                <button
                  className="btn"
                  type="button"
                  disabled={busy}
                  onClick={() => void act(`/api/v1/policies/${detail.code}/draft`)}
                >
                  {t("policiesPage.newVersion")}
                </button>
              )}
              {workspace?.canWrite && latest.state === "Draft" && (
                <button
                  className="btn"
                  type="button"
                  disabled={busy}
                  onClick={() => void act(`/api/v1/policies/${detail.code}/submit`)}
                >
                  {t("policiesPage.submit")}
                </button>
              )}
              {workspace?.role === "Admin" && latest.state === "InReview" && (
                <button
                  className="btn primary"
                  type="button"
                  disabled={busy}
                  onClick={() => void act(`/api/v1/policies/${detail.code}/approve`)}
                >
                  {t("policiesPage.approve")}
                </button>
              )}
              {mine && (
                <button
                  className="btn primary"
                  type="button"
                  disabled={busy}
                  onClick={() => void act(`/api/v1/policies/${detail.code}/acknowledge`)}
                >
                  {t("policiesPage.acknowledge")}
                </button>
              )}
              <h3 style={{ marginTop: 16 }}>{t("policiesPage.acknowledgements")}</h3>
              <label className="field">
                <span>{t("policiesPage.search")}</span>
                <input
                  value={ackQuery}
                  onChange={(event) => {
                    setAckQuery(event.target.value);
                    setAckPage(1);
                  }}
                />
              </label>
              {detail.acknowledgements.length === 0 ? (
                <div className="empty">{t("policiesPage.noAcknowledgements")}</div>
              ) : (
                <>
                  <table>
                    <thead>
                      <tr>
                        <th>{t("policiesPage.person")}</th>
                        <th>{t("policiesPage.due")}</th>
                        <th>{t("policiesPage.ack")}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {ackRows.map((item) => (
                        <tr key={item.personEmail}>
                          <td>
                            <div className="t">{item.personName}</div>
                            <div className="m">{item.personEmail}</div>
                          </td>
                          <td className="m">{formatDate(item.dueOn)}</td>
                          <td className="m">
                            {item.acknowledgedAt
                              ? formatWhen(item.acknowledgedAt)
                              : item.reminderDue
                                ? t("policiesPage.reminderDue")
                                : item.remindedAt
                                  ? t("policiesPage.reminded", {
                                      date: formatWhen(item.remindedAt),
                                    })
                                  : t("policiesPage.waiting")}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  <Pager
                    page={ackPage}
                    pageSize={20}
                    total={matchingAcks.length}
                    onPage={setAckPage}
                  />
                </>
              )}
              <h3 style={{ marginTop: 16 }}>{t("policiesPage.history")}</h3>
              {detail.versions.map((version) => (
                <div className="m" key={version.number}>
                  {t("policiesPage.version", { number: version.number })} ·{" "}
                  {t(`policiesPage.states.${version.state}`, { defaultValue: version.state })}
                  {version.approvedByName ? ` · ${version.approvedByName}` : ""}
                </div>
              ))}
              <AssignTask
                tenantId={tenantId}
                linkedKind="Policy"
                linkedCode={detail.code}
                canWrite={Boolean(workspace?.canWrite)}
              />
            </>
          )}
        </div>
      </div>
    </>
  );
}
