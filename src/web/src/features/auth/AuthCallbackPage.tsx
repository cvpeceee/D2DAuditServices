import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import { ApiError, api, type AuthResponse } from "../../shared/api/client";
import { useAuth } from "./AuthContext";

export function AuthCallbackPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { refresh } = useAuth();
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const code = params.get("code");
    const state = params.get("state");
    const stored = sessionStorage.getItem("d2d-oauth-state");
    sessionStorage.removeItem("d2d-oauth-state");
    if (params.get("error") || !code || !state || state !== stored) {
      setError(t("login.errors.invalid_state"));
      return;
    }
    let cancelled = false;
    void api<AuthResponse>("/api/v1/auth/callback", {
      method: "POST",
      body: { code, redirectUri: `${window.location.origin}/auth/callback` },
      skipRefresh: true,
    })
      .then(async (session) => {
        if (cancelled) return;
        if (session.mfaRequired) {
          setError(t("login.errors.mfa_enrollment_required"));
          return;
        }
        await refresh();
        navigate("/", { replace: true });
      })
      .catch((caught: unknown) => {
        if (cancelled) return;
        const codeName = caught instanceof ApiError ? caught.code : "generic";
        const key = `login.errors.${codeName}`;
        const text = t(key);
        setError(text === key ? t("login.errors.generic") : text);
      });
    return () => {
      cancelled = true;
    };
  }, [navigate, refresh, t]);

  return (
    <div className="login-stage">
      <section className="login-card">
        <div className="brand">
          <div className="mark">D2D</div>
          <span>{t("productName")}</span>
        </div>
        <h1>{t("login.title")}</h1>
        {error ? (
          <div className="error" role="alert">
            {error}
          </div>
        ) : (
          <>
            <div className="sub">{t("login.checking")}</div>
            <div className="busy" role="progressbar" aria-label={t("login.checking")}>
              <i />
            </div>
          </>
        )}
      </section>
    </div>
  );
}
