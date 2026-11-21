import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { ApiError, api } from "../../shared/api/client";
import { formatDate, formatWhen, Pill } from "../../shared/ui/ui";
import {
  selfServiceCategories,
  slaTone,
  type LifecycleChecklist,
  type PersonLifecycle,
} from "./lifecycleTypes";

function code(caught: unknown) {
  return caught instanceof ApiError ? caught.code : "load";
}

export function ChecklistCard({
  checklist,
  tenantId,
  canWrite,
  selfService = false,
  onChanged,
  onError,
}: {
  checklist: LifecycleChecklist;
  tenantId: string;
  canWrite: boolean;
  selfService?: boolean;
  onChanged: () => void;
  onError: (value: string | null) => void;
}) {
  const { t } = useTranslation();
  const [busy, setBusy] = useState(false);
  const open = checklist.state === "Open";

  async function post(path: string, body?: unknown) {
    setBusy(true);
    onError(null);
    try {
      await api(path, { method: "POST", tenantId, body });
      onChanged();
    } catch (caught: unknown) {
      onError(code(caught));
    } finally {
      setBusy(false);
    }
  }

  const stepBase = selfService ? "/api/v1/me/lifecycle/steps" : "/api/v1/lifecycle/steps";
  const pendingAccess = checklist.steps.some((step) => step.revokesAccess && !step.done);

  return (
    <article className="card" style={{ marginTop: 10 }}>
      <h3>
        {selfService
          ? t(`lifecycle.kinds.${checklist.kind}`)
          : `${checklist.personName} · ${t(`lifecycle.kinds.${checklist.kind}`)}`}
        <span className="r">
          <Pill status={checklist.state === "Complete" ? "pass" : slaTone(checklist.sla)}>
            {t(`lifecycle.sla.${checklist.sla}`, { defaultValue: checklist.sla })}
          </Pill>
        </span>
      </h3>
      <div className="m">
        {t(`lifecycle.triggers.${checklist.trigger}`, { defaultValue: checklist.trigger })} ·{" "}
        {t("lifecycle.effective")} {formatDate(checklist.effectiveOn)} · {t("lifecycle.due")}{" "}
        {formatWhen(checklist.dueAt)} · {checklist.doneCount}/{checklist.totalCount}
      </div>
      {checklist.reason && <div className="id">{checklist.reason}</div>}
      {checklist.kind === "Offboarding" && checklist.accessDueAt && (
        <div className="m">
          {t("lifecycle.accessDue", { when: formatWhen(checklist.accessDueAt) })}{" "}
          <Pill status={slaTone(checklist.accessSla)}>
            {t(`lifecycle.sla.${checklist.accessSla}`, { defaultValue: checklist.accessSla })}
          </Pill>
          {checklist.accessRevokedAt &&
            ` · ${t("lifecycle.revokedAt", { when: formatWhen(checklist.accessRevokedAt) })}`}
        </div>
      )}
      {checklist.groups.length > 0 && (
        <div className="id">
          {t("lifecycle.groups")}: {checklist.groups.join(", ")}
        </div>
      )}
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>{t("lifecycle.step")}</th>
              <th>{t("lifecycle.due")}</th>
              <th>{t("lifecycle.slaLabel")}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {checklist.steps.map((step) => {
              const allowed = selfService
                ? selfServiceCategories.includes(step.category)
                : canWrite;
              return (
                <tr key={step.id}>
                  <td>
                    <b>{step.title}</b>
                    <div className="id">
                      {t(`lifecycle.categories.${step.category}`, { defaultValue: step.category })}
                      {step.revokesAccess ? ` · ${t("lifecycle.revokesAccess")}` : ""}
                    </div>
                    {step.done && (
                      <div className="id">
                        {step.completedBy}
                        {step.completedAt ? ` · ${formatWhen(step.completedAt)}` : ""}
                        {step.result ? ` · ${step.result}` : ""}
                      </div>
                    )}
                    {step.evidenceId && (
                      <div className="id">
                        {t("lifecycle.evidence")} {step.evidenceId.slice(0, 8)}
                      </div>
                    )}
                  </td>
                  <td className="m">{formatWhen(step.dueAt)}</td>
                  <td>
                    <Pill status={slaTone(step.sla)}>
                      {t(`lifecycle.sla.${step.sla}`, { defaultValue: step.sla })}
                    </Pill>
                  </td>
                  <td>
                    {open && !step.done && allowed && (
                      <>
                        {step.revokesAccess && !selfService ? (
                          <button
                            className="btn primary"
                            type="button"
                            disabled={busy}
                            onClick={() => void post(`/api/v1/lifecycle/steps/${step.id}/revoke`)}
                          >
                            {t("lifecycle.revoke")}
                          </button>
                        ) : (
                          <button
                            className="btn"
                            type="button"
                            disabled={busy}
                            onClick={() => void post(`${stepBase}/${step.id}`, { done: true })}
                          >
                            {t("lifecycle.markDone")}
                          </button>
                        )}
                      </>
                    )}
                    {open && step.done && !step.revokesAccess && allowed && (
                      <button
                        className="linkish"
                        type="button"
                        disabled={busy}
                        onClick={() => void post(`${stepBase}/${step.id}`, { done: false })}
                      >
                        {t("lifecycle.undo")}
                      </button>
                    )}
                    {selfService && !allowed && !step.done && (
                      <span className="m">{t("lifecycle.hrManaged")}</span>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      {!selfService && canWrite && open && pendingAccess && (
        <button
          className="btn primary"
          type="button"
          disabled={busy}
          onClick={() => void post(`/api/v1/lifecycle/checklists/${checklist.id}/revoke-all`)}
        >
          {t("lifecycle.revokeAll")}
        </button>
      )}
    </article>
  );
}

export function PersonLifecyclePanel({
  personId,
  tenantId,
  canWrite,
  onChanged,
}: {
  personId: string;
  tenantId: string;
  canWrite: boolean;
  onChanged?: () => void;
}) {
  const { t } = useTranslation();
  const [data, setData] = useState<PersonLifecycle | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [terminationOn, setTerminationOn] = useState(() => new Date().toISOString().slice(0, 10));
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);

  const load = () => {
    void api<PersonLifecycle>(`/api/v1/lifecycle/people/${personId}`, { tenantId })
      .then(setData)
      .catch((caught: unknown) => setError(code(caught)));
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [personId, tenantId]);

  async function start(kind: "offboard" | "onboard") {
    setBusy(true);
    setError(null);
    try {
      await api(`/api/v1/lifecycle/people/${personId}/${kind}`, {
        method: "POST",
        tenantId,
        body: kind === "offboard" ? { terminationOn, reason } : {},
      });
      setReason("");
      load();
      onChanged?.();
    } catch (caught: unknown) {
      setError(code(caught));
    } finally {
      setBusy(false);
    }
  }

  const openOffboarding = data?.checklists.some(
    (item) => item.kind === "Offboarding" && item.state === "Open",
  );
  const openOnboarding = data?.checklists.some(
    (item) => item.kind === "Onboarding" && item.state === "Open",
  );

  return (
    <section style={{ marginTop: 14 }} aria-label={t("lifecycle.tab")}>
      <h3>{t("lifecycle.tab")}</h3>
      {error && (
        <div className="error" role="alert">
          {t(`lifecycle.errors.${error}`, { defaultValue: `${t("errors.load")} (${error})` })}
        </div>
      )}
      {data && (
        <>
          <div className="kv">
            <div>
              <small>{t("lifecycle.groups")}</small>
              {data.groups.join(", ") || "—"}
            </div>
            <div>
              <small>{t("lifecycle.requiredPolicies")}</small>
              {data.requiredPolicies.join(", ") || "—"}
            </div>
            <div>
              <small>{t("lifecycle.requiredTraining")}</small>
              {data.requiredTraining.join(", ") || "—"}
            </div>
            <div>
              <small>{t("lifecycle.termination")}</small>
              {data.terminationOn ? formatDate(data.terminationOn) : "—"}
            </div>
          </div>
          {canWrite && !openOffboarding && data.state !== "Offboarded" && (
            <div className="split-fields">
              <label className="field">
                <span>{t("lifecycle.terminationOn")}</span>
                <input
                  type="date"
                  value={terminationOn}
                  onChange={(event) => setTerminationOn(event.target.value)}
                />
              </label>
              <label className="field">
                <span>{t("lifecycle.reason")}</span>
                <input value={reason} onChange={(event) => setReason(event.target.value)} />
              </label>
              <div className="field">
                <span>&nbsp;</span>
                <button
                  className="btn primary"
                  type="button"
                  disabled={busy}
                  onClick={() => void start("offboard")}
                >
                  {t("lifecycle.startOffboarding")}
                </button>
              </div>
            </div>
          )}
          {canWrite && !openOnboarding && data.state === "Active" && (
            <button
              className="btn"
              type="button"
              disabled={busy}
              onClick={() => void start("onboard")}
            >
              {t("lifecycle.startOnboarding")}
            </button>
          )}
          {data.checklists.length === 0 ? (
            <div className="empty">{t("lifecycle.noChecklists")}</div>
          ) : (
            data.checklists.map((checklist) => (
              <ChecklistCard
                key={checklist.id}
                checklist={checklist}
                tenantId={tenantId}
                canWrite={canWrite}
                onChanged={() => {
                  load();
                  onChanged?.();
                }}
                onError={setError}
              />
            ))
          )}
        </>
      )}
    </section>
  );
}

export function MyOnboarding({ tenantId }: { tenantId: string | null }) {
  const { t } = useTranslation();
  const [data, setData] = useState<PersonLifecycle | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = () => {
    if (!tenantId) return;
    void api<PersonLifecycle | undefined>("/api/v1/me/lifecycle", { tenantId })
      .then((value) => setData(value ?? null))
      .catch((caught: unknown) => setError(code(caught)));
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  const open = (data?.checklists ?? []).filter(
    (item) => item.kind === "Onboarding" && item.state === "Open",
  );
  if (!tenantId || open.length === 0) return null;

  return (
    <section style={{ marginTop: 14 }} aria-label={t("lifecycle.myOnboarding")}>
      <h2>{t("lifecycle.myOnboarding")}</h2>
      <div className="sub">{t("lifecycle.myOnboardingSub")}</div>
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}
      {open.map((checklist) => (
        <ChecklistCard
          key={checklist.id}
          checklist={checklist}
          tenantId={tenantId}
          canWrite={false}
          selfService
          onChanged={load}
          onError={setError}
        />
      ))}
    </section>
  );
}
