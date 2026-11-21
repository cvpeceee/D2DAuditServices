import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { ApiError, api } from "../../shared/api/client";
import { formatWhen } from "../../shared/ui/ui";
import { useAuth } from "../auth/AuthContext";
import type { AcceptInviteResult, InvitePreview } from "../onboarding/types";

export function AcceptInvitePage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { refresh } = useAuth();
  const [params] = useSearchParams();
  const token = params.get("token") ?? "";
  const [preview, setPreview] = useState<InvitePreview | null>(null);
  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<AcceptInviteResult | null>(null);

  useEffect(() => {
    if (!token) {
      setError("invalid_token");
      return;
    }
    void api<InvitePreview>(`/api/v1/invites/preview?token=${encodeURIComponent(token)}`, {
      skipRefresh: true,
    })
      .then(setPreview)
      .catch((caught) => setError(caught instanceof ApiError ? caught.code : "generic"));
  }, [token]);

  async function accept(event: React.FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const accepted = await api<AcceptInviteResult>("/api/v1/invites/accept", {
        method: "POST",
        body: { token, displayName: displayName || null, password: password || null },
        skipRefresh: true,
      });
      if (accepted.nextAction === "dashboard") {
        await refresh();
        navigate("/");
        return;
      }
      setResult(accepted);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.code : "generic");
    } finally {
      setBusy(false);
    }
  }

  const usable = preview?.state === "Pending";

  return (
    <div className="login-stage">
      <section className="login-card">
        <div className="brand">
          <div className="mark">D2D</div>
          <span>{t("productName")}</span>
        </div>
        <h1>{t("team.accept.title")}</h1>
        {preview && (
          <div className="sub">
            {t("team.accept.sub", {
              tenant: preview.tenantName,
              role: t(`settingsPage.roles.${preview.role}`, { defaultValue: preview.role }),
              email: preview.email,
            })}
          </div>
        )}
        {preview && !usable && (
          <div className="error" role="alert">
            {t(`team.states.${preview.state}`, { defaultValue: preview.state })}
          </div>
        )}
        {error && (
          <div className="error" role="alert">
            {t(`team.errors.${error}`, { defaultValue: t("signup.errors.generic") })}
          </div>
        )}
        {result ? (
          <>
            <div className="notice" role="status">
              {result.nextAction === "sso" ? t("team.accept.sso") : t("team.accept.done")}
            </div>
            <Link className="btn primary auth-btn" to="/login">
              {t("signup.signInNow")}
            </Link>
          </>
        ) : (
          usable && (
            <form onSubmit={(event) => void accept(event)}>
              <div className="hint">
                {t("team.accept.expires", { date: formatWhen(preview.expiresAt) })}
              </div>
              <div className="field">
                <label htmlFor="invite-name">{t("signup.fullName")}</label>
                <input
                  id="invite-name"
                  value={displayName}
                  autoComplete="name"
                  onChange={(event) => setDisplayName(event.target.value)}
                />
              </div>
              {preview.passwordRequired && (
                <div className="field">
                  <label htmlFor="invite-password">{t("signup.password")}</label>
                  <input
                    id="invite-password"
                    type="password"
                    autoComplete="new-password"
                    value={password}
                    onChange={(event) => setPassword(event.target.value)}
                    required
                  />
                  <div className="hint">{t("team.accept.passwordHint")}</div>
                </div>
              )}
              <button className="btn primary auth-btn" type="submit" disabled={busy}>
                {t("team.accept.submit")}
              </button>
            </form>
          )
        )}
      </section>
    </div>
  );
}
