import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { ApiError, api } from "../../shared/api/client";
import { useAuth } from "../auth/AuthContext";
import type { SignupStatus } from "../onboarding/types";

const pollMs = 1200;

export function SignupVerifyPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { refresh } = useAuth();
  const [params] = useSearchParams();
  const token = params.get("token") ?? "";
  const [status, setStatus] = useState<SignupStatus | null>(null);
  const [error, setError] = useState<string | null>(null);
  const started = useRef(false);

  useEffect(() => {
    if (started.current) return;
    started.current = true;
    if (!token) {
      setError("invalid_token");
      return;
    }
    void api<SignupStatus>("/api/v1/signup/verify", {
      method: "POST",
      body: { token },
      skipRefresh: true,
    })
      .then(setStatus)
      .catch((caught) => setError(caught instanceof ApiError ? caught.code : "generic"));
  }, [token]);

  useEffect(() => {
    if (!status || status.status !== "Provisioning") return;
    const timer = window.setTimeout(() => {
      void api<SignupStatus>(`/api/v1/signup/${status.signupId}`, { skipRefresh: true })
        .then(setStatus)
        .catch((caught) => setError(caught instanceof ApiError ? caught.code : "generic"));
    }, pollMs);
    return () => window.clearTimeout(timer);
  }, [status]);

  async function continueToWorkspace() {
    await refresh();
    navigate("/onboarding");
  }

  const ready = status?.status === "Ready";
  const failed = status?.status === "Failed";

  return (
    <div className="login-stage">
      <section className="login-card">
        <div className="brand">
          <div className="mark">D2D</div>
          <span>{t("productName")}</span>
        </div>
        <h1>{ready ? t("signup.readyTitle") : t("signup.provisioningTitle")}</h1>
        {status && (
          <>
            <div className="sub">
              {t("signup.provisioningSub", { company: status.companyName })}
            </div>
            <div
              className="bar"
              role="progressbar"
              aria-label={t("signup.progress")}
              aria-valuenow={status.percent}
              aria-valuemin={0}
              aria-valuemax={100}
            >
              <i style={{ width: `${status.percent}%` }} />
            </div>
            <div className="hint">
              {t(`signup.steps.${status.currentStep}`, { defaultValue: status.currentStep })} ·{" "}
              {status.percent}%
            </div>
          </>
        )}
        {(error || failed) && (
          <div className="error" role="alert">
            {t(`signup.errors.${error ?? status?.error ?? "generic"}`, {
              defaultValue: t("signup.errors.generic"),
            })}
          </div>
        )}
        {ready && status.nextAction === "onboarding" && (
          <button
            className="btn primary auth-btn"
            type="button"
            onClick={() => void continueToWorkspace()}
          >
            {t("signup.startOnboarding")}
          </button>
        )}
        {ready && status.nextAction === "login" && (
          <Link className="btn primary auth-btn" to="/login">
            {t("signup.signInNow")}
          </Link>
        )}
        {ready && status.nextAction === "sso" && (
          <>
            <div className="notice">{t("signup.ssoReady")}</div>
            <Link className="btn primary auth-btn" to="/login">
              {t("signup.signInNow")}
            </Link>
          </>
        )}
        {(error || failed) && (
          <div className="login-note">
            <Link to="/signup">{t("signup.startOver")}</Link>
          </div>
        )}
      </section>
    </div>
  );
}
