import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { api } from "../../shared/api/client";
import type { OnboardingState } from "./types";

export function GettingStartedCard({
  tenantId,
  canWrite,
}: {
  tenantId: string | null;
  canWrite: boolean;
}) {
  const { t } = useTranslation();
  const [state, setState] = useState<OnboardingState | null>(null);

  useEffect(() => {
    if (!tenantId) return;
    let live = true;
    void api<OnboardingState>("/api/v1/onboarding", { tenantId })
      .then((next) => {
        if (live) setState(next);
      })
      .catch(() => {
        if (live) setState(null);
      });
    return () => {
      live = false;
    };
  }, [tenantId]);

  async function dismiss() {
    if (!tenantId) return;
    const next = await api<OnboardingState>("/api/v1/onboarding/dismiss", {
      method: "POST",
      tenantId,
      body: {},
    });
    setState(next);
  }

  if (!state || !Array.isArray(state.steps) || state.dismissed || state.complete) return null;

  return (
    <section
      className="card getting-started"
      style={{ marginBottom: 14 }}
      aria-labelledby="getting-started-title"
    >
      <h3 id="getting-started-title">
        {t("onboarding.card.title")}
        <span className="r">{t("onboarding.percent", { percent: state.percent })}</span>
      </h3>
      <div className="bar" aria-hidden="true">
        <i style={{ width: `${state.percent}%` }} />
      </div>
      <ul className="stack" style={{ listStyle: "none", padding: 0 }}>
        {state.steps.map((step) => {
          const done = state.completedSteps.includes(step);
          return (
            <li
              key={step}
              className="check-row"
              aria-label={`${t(`onboarding.steps.${step}`)}: ${done ? t("onboarding.card.done") : t("onboarding.card.todo")}`}
            >
              <span aria-hidden="true">{done ? "✓" : "○"}</span>
              <span className={done ? "id" : ""}>{t(`onboarding.steps.${step}`)}</span>
            </li>
          );
        })}
      </ul>
      <div className="row">
        <Link className="btn primary" to="/onboarding">
          {t("onboarding.card.continue")}
        </Link>
        {canWrite && (
          <button className="btn" type="button" onClick={() => void dismiss()}>
            {t("onboarding.card.dismiss")}
          </button>
        )}
      </div>
    </section>
  );
}
