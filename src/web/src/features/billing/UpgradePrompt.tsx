import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import type { GateDecision } from "../onboarding/types";

/** Soft paywall hint: shown when a plan limit is reached; never blocks the action. */
export function UpgradePrompt({ decision }: { decision: GateDecision | null | undefined }) {
  const { t } = useTranslation();
  if (!decision || decision.allowed || decision.limit === null) return null;
  const feature = t(`billingPage.features.${decision.feature}`, { defaultValue: decision.feature });
  return (
    <div className="notice upgrade-prompt" role="status">
      <b>
        {decision.overLimit
          ? t("billingPage.upgrade.over", {
              feature,
              used: decision.used,
              limit: decision.limit,
            })
          : t("billingPage.upgrade.reached", {
              feature,
              used: decision.used,
              limit: decision.limit,
            })}
      </b>{" "}
      {decision.upgradeTo && (
        <Link to="/settings/billing">
          {t("billingPage.upgrade.cta", { plan: decision.upgradeTo })}
        </Link>
      )}
    </div>
  );
}
