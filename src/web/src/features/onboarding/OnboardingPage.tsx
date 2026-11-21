import { useCallback, useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useOutletContext } from "react-router-dom";
import { ApiError, api } from "../../shared/api/client";
import { formatDate, frameworkTag, Pill, Skeleton } from "../../shared/ui/ui";
import { UpgradePrompt } from "../billing/UpgradePrompt";
import { InviteForm } from "../team/InviteForm";
import type {
  BillingOverview,
  GateDecision,
  OnboardingState,
  ReadinessPlan,
  Recommendation,
} from "./types";

type Context = {
  workspace?: { role?: string; canWrite?: boolean } | null;
  tenantId: string | null;
};

function toggle(list: string[], value: string) {
  return list.includes(value) ? list.filter((item) => item !== value) : [...list, value];
}

export function defaultTargetDate(today = new Date()) {
  const date = new Date(today);
  date.setDate(date.getDate() + 120);
  return date.toISOString().slice(0, 10);
}

/** Local soft gate for an unsaved framework selection. */
export function frameworkDecision(
  selected: number,
  limit: number | null,
  plan = "Starter",
): GateDecision {
  const over = limit !== null && selected > limit;
  return {
    feature: "frameworks",
    allowed: !over,
    used: selected,
    limit,
    overLimit: over,
    upgradeTo: over ? (plan === "Starter" ? "Growth" : "Enterprise") : null,
  };
}

export function OnboardingPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<Context>();
  const [state, setState] = useState<OnboardingState | null>(null);
  const [billing, setBilling] = useState<BillingOverview | null>(null);
  const [step, setStep] = useState<string>("profile");
  const [size, setSize] = useState("");
  const [industry, setIndustry] = useState("");
  const [clouds, setClouds] = useState<string[]>([]);
  const [dataTypes, setDataTypes] = useState<string[]>([]);
  const [frameworks, setFrameworks] = useState<string[]>([]);
  const [scoped, setScoped] = useState<string[]>([]);
  const [recommendation, setRecommendation] = useState<Recommendation | null>(null);
  const [target, setTarget] = useState(defaultTargetDate());
  const [plan, setPlan] = useState<ReadinessPlan | null>(null);
  const [installed, setInstalled] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const apply = useCallback((next: OnboardingState, moveOn: boolean) => {
    setState(next);
    setSize(next.companySize || next.options.sizes[0] || "");
    setIndustry(next.industry || next.options.industries[0] || "");
    setClouds(next.cloudProviders);
    setDataTypes(next.dataTypes);
    if (next.frameworks.length > 0) setFrameworks(next.frameworks);
    if (next.scopedControls.length > 0) setScoped(next.scopedControls);
    if (next.auditTargetDate) setTarget(next.auditTargetDate);
    if (moveOn) {
      setStep(next.steps.find((item) => !next.completedSteps.includes(item)) ?? "plan");
    }
  }, []);

  useEffect(() => {
    if (!tenantId) return;
    void api<OnboardingState>("/api/v1/onboarding", { tenantId })
      .then((next) => apply(next, true))
      .catch((caught) => setError(caught instanceof ApiError ? caught.code : "load"));
    void api<BillingOverview>("/api/v1/billing", { tenantId })
      .then(setBilling)
      .catch(() => setBilling(null));
  }, [tenantId, apply]);

  const limit = billing?.entitlements.frameworks ?? null;
  const decision = useMemo(
    () => frameworkDecision(frameworks.length, limit, billing?.plan),
    [frameworks, limit, billing?.plan],
  );

  async function run<T>(work: () => Promise<T>) {
    setBusy(true);
    setError(null);
    try {
      return await work();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.code : "load");
      return null;
    } finally {
      setBusy(false);
    }
  }

  async function save(body: Record<string, unknown>) {
    if (!tenantId) return;
    const next = await run(() =>
      api<OnboardingState>("/api/v1/onboarding", { method: "PUT", tenantId, body }),
    );
    if (next) apply(next, true);
  }

  async function recommend() {
    if (!tenantId) return;
    const result = await run(() =>
      api<Recommendation>("/api/v1/onboarding/recommend", {
        method: "POST",
        tenantId,
        body: { companySize: size, industry, cloudProviders: clouds, dataTypes },
      }),
    );
    if (!result) return;
    setRecommendation(result);
    setFrameworks(result.frameworks.map((item) => item.frameworkKey));
    setScoped(result.scopedControls);
  }

  async function saveProfile() {
    if (!recommendation) await recommend();
    await save({
      companySize: size,
      industry,
      cloudProviders: clouds,
      dataTypes,
      completeStep: "profile",
    });
  }

  async function installPolicies() {
    if (!tenantId) return;
    const ok = await run(() =>
      api<number>("/api/v1/policies/library", { method: "POST", tenantId, body: {} }),
    );
    if (ok !== null) {
      setInstalled(true);
      await save({ completeStep: "policies" });
    }
  }

  async function generate() {
    if (!tenantId) return;
    const result = await run(() =>
      api<ReadinessPlan>("/api/v1/onboarding/plan", {
        method: "POST",
        tenantId,
        body: { targetDate: target },
      }),
    );
    if (!result) return;
    setPlan(result);
    const next = await api<OnboardingState>("/api/v1/onboarding", { tenantId });
    apply(next, false);
  }

  if (!state) {
    return (
      <>
        {error && (
          <div className="error" role="alert">
            {t(`onboarding.errors.${error}`, { defaultValue: t("errors.load") })}
          </div>
        )}
        <Skeleton className="lg" />
      </>
    );
  }

  const canWrite = workspace?.canWrite ?? false;
  const isAdmin = workspace?.role === "Admin";
  const reasons = new Map(
    (recommendation?.frameworks ?? []).map((item) => [item.frameworkKey, item.reason]),
  );

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("onboarding.title")}</h1>
          <div className="sub">{t("onboarding.sub")}</div>
        </div>
        <div className="act">
          <span className="id">{t("onboarding.percent", { percent: state.percent })}</span>
        </div>
      </div>
      <div className="bar" aria-hidden="true">
        <i style={{ width: `${state.percent}%` }} />
      </div>
      <div className="tabs" role="tablist" aria-label={t("onboarding.stepsLabel")}>
        {state.steps.map((item, index) => (
          <button
            key={item}
            type="button"
            role="tab"
            aria-selected={step === item}
            className={step === item ? "on" : ""}
            onClick={() => setStep(item)}
          >
            {state.completedSteps.includes(item) ? "✓ " : `${index + 1}. `}
            {t(`onboarding.steps.${item}`)}
          </button>
        ))}
      </div>
      {error && (
        <div className="error" role="alert">
          {t(`onboarding.errors.${error}`, { defaultValue: t("errors.load") })}
        </div>
      )}
      {!canWrite && <div className="notice">{t("onboarding.readOnly")}</div>}

      {step === "profile" && (
        <section className="card stack" aria-labelledby="ob-profile">
          <h3 id="ob-profile">{t("onboarding.profile.title")}</h3>
          <div className="sub">{t("onboarding.profile.sub")}</div>
          <div className="split-fields">
            <div className="field">
              <label htmlFor="ob-size">{t("onboarding.profile.size")}</label>
              <select id="ob-size" value={size} onChange={(event) => setSize(event.target.value)}>
                {state.options.sizes.map((item) => (
                  <option key={item} value={item}>
                    {t("onboarding.profile.sizeOption", { size: item })}
                  </option>
                ))}
              </select>
            </div>
            <div className="field">
              <label htmlFor="ob-industry">{t("onboarding.profile.industry")}</label>
              <select
                id="ob-industry"
                value={industry}
                onChange={(event) => setIndustry(event.target.value)}
              >
                {state.options.industries.map((item) => (
                  <option key={item} value={item}>
                    {t(`onboarding.industries.${item}`)}
                  </option>
                ))}
              </select>
            </div>
          </div>
          <fieldset className="chips">
            <legend>{t("onboarding.profile.clouds")}</legend>
            {state.options.cloudProviders.map((item) => (
              <label key={item} className="check-row chip">
                <input
                  type="checkbox"
                  checked={clouds.includes(item)}
                  onChange={() => setClouds((current) => toggle(current, item))}
                />
                {t(`onboarding.clouds.${item}`)}
              </label>
            ))}
          </fieldset>
          <fieldset className="chips">
            <legend>{t("onboarding.profile.dataTypes")}</legend>
            {state.options.dataTypes.map((item) => (
              <label key={item} className="check-row chip">
                <input
                  type="checkbox"
                  checked={dataTypes.includes(item)}
                  onChange={() => setDataTypes((current) => toggle(current, item))}
                />
                {t(`onboarding.dataTypes.${item}`)}
              </label>
            ))}
          </fieldset>
          {recommendation && (
            <div className="notice" role="status">
              {t("onboarding.profile.recommended", {
                frameworks: recommendation.frameworks
                  .map((item) => frameworkTag(item.frameworkKey))
                  .join(", "),
                count: recommendation.scopedControls.length,
              })}
            </div>
          )}
          <div className="row">
            <button className="btn" type="button" disabled={busy} onClick={() => void recommend()}>
              {t("onboarding.profile.recommend")}
            </button>
            <button
              className="btn primary"
              type="button"
              disabled={busy || !canWrite}
              onClick={() => void saveProfile()}
            >
              {t("onboarding.saveContinue")}
            </button>
          </div>
        </section>
      )}

      {step === "frameworks" && (
        <section className="card stack" aria-labelledby="ob-frameworks">
          <h3 id="ob-frameworks">{t("onboarding.frameworks.title")}</h3>
          <div className="sub">
            {t("onboarding.frameworks.sub", { count: state.options.frameworks.length })}
          </div>
          <div className="cardgrid">
            {state.options.frameworks.map((key) => (
              <label key={key} className="check-row">
                <input
                  type="checkbox"
                  checked={frameworks.includes(key)}
                  onChange={() => setFrameworks((current) => toggle(current, key))}
                />
                <b>{frameworkTag(key)}</b>
                {reasons.has(key) && (
                  <Pill status="info">
                    {t(`onboarding.reasons.${reasons.get(key)}`, {
                      defaultValue: t("onboarding.frameworks.recommended"),
                    })}
                  </Pill>
                )}
              </label>
            ))}
          </div>
          <div className="hint">
            {t("onboarding.frameworks.scoped", { count: scoped.length })}
            {limit !== null && ` ${t("onboarding.frameworks.limit", { limit })}`}
          </div>
          <UpgradePrompt decision={decision} />
          <div className="row">
            <button
              className="btn primary"
              type="button"
              disabled={busy || !canWrite || frameworks.length === 0}
              onClick={() =>
                void save({ frameworks, scopedControls: scoped, completeStep: "frameworks" })
              }
            >
              {t("onboarding.saveContinue")}
            </button>
          </div>
        </section>
      )}

      {step === "integrations" && (
        <section className="card stack" aria-labelledby="ob-integrations">
          <h3 id="ob-integrations">{t("onboarding.integrations.title")}</h3>
          <div className="sub">{t("onboarding.integrations.sub")}</div>
          <div className="row">
            <Link className="btn" to="/integrations">
              {t("onboarding.integrations.open")}
            </Link>
            <button
              className="btn primary"
              type="button"
              disabled={busy || !canWrite}
              onClick={() => void save({ completeStep: "integrations" })}
            >
              {t("onboarding.markDone")}
            </button>
          </div>
        </section>
      )}

      {step === "team" && (
        <section className="card stack" aria-labelledby="ob-team">
          <h3 id="ob-team">{t("onboarding.team.title")}</h3>
          <div className="sub">{t("onboarding.team.sub")}</div>
          {isAdmin && tenantId ? (
            <InviteForm tenantId={tenantId} />
          ) : (
            <div className="notice">{t("onboarding.team.adminOnly")}</div>
          )}
          <div className="row">
            <button
              className="btn primary"
              type="button"
              disabled={busy || !canWrite}
              onClick={() => void save({ completeStep: "team" })}
            >
              {t("onboarding.saveContinue")}
            </button>
          </div>
        </section>
      )}

      {step === "policies" && (
        <section className="card stack" aria-labelledby="ob-policies">
          <h3 id="ob-policies">{t("onboarding.policies.title")}</h3>
          <div className="sub">{t("onboarding.policies.sub")}</div>
          {installed && (
            <div className="notice" role="status">
              {t("onboarding.policies.installed")}
            </div>
          )}
          <div className="row">
            <button
              className="btn primary"
              type="button"
              disabled={busy || !canWrite}
              onClick={() => void installPolicies()}
            >
              {t("onboarding.policies.install")}
            </button>
            <Link className="btn" to="/policies">
              {t("onboarding.policies.open")}
            </Link>
          </div>
        </section>
      )}

      {step === "plan" && (
        <section className="card stack" aria-labelledby="ob-plan">
          <h3 id="ob-plan">{t("onboarding.plan.title")}</h3>
          <div className="sub">{t("onboarding.plan.sub")}</div>
          <div className="field">
            <label htmlFor="ob-target">{t("onboarding.plan.target")}</label>
            <input
              id="ob-target"
              type="date"
              value={target}
              onChange={(event) => setTarget(event.target.value)}
            />
            <div className="hint">{t("onboarding.plan.targetHint")}</div>
          </div>
          <div className="row">
            <button
              className="btn primary"
              type="button"
              disabled={busy || !canWrite || !target}
              onClick={() => void generate()}
            >
              {t("onboarding.plan.generate")}
            </button>
          </div>
          {plan && (
            <div className="stack" aria-label={t("onboarding.plan.result")}>
              <div className="notice" role="status">
                {t("onboarding.plan.created", {
                  count: plan.tasksCreated,
                  date: formatDate(plan.targetDate),
                })}{" "}
                <Link to="/tasks">{t("onboarding.plan.viewTasks")}</Link>
              </div>
              <UpgradePrompt decision={plan.frameworks} />
              <ol className="stack">
                {plan.milestones.map((milestone) => (
                  <li key={milestone.key}>
                    <b>{milestone.title}</b> · {formatDate(milestone.dueOn)}
                    <ul>
                      {milestone.tasks.map((task) => (
                        <li key={task.title}>
                          {task.title} <span className="id">{formatDate(task.dueOn)}</span>
                        </li>
                      ))}
                    </ul>
                  </li>
                ))}
              </ol>
            </div>
          )}
          {state.complete && (
            <div className="notice">
              {t("onboarding.complete")} <Link to="/">{t("onboarding.toDashboard")}</Link>
            </div>
          )}
        </section>
      )}
    </>
  );
}
