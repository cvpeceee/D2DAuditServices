import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import { ApiError, api, type AuthMode, type AuthResponse } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { useTheme, type ThemeChoice } from "../theme/ThemeProvider";

function messageFor(t: (key: string) => string, code: string) {
  const key = `login.errors.${code}`;
  const text = t(key);
  return text === key ? t("login.errors.generic") : text;
}

export function LoginPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { refresh } = useAuth();
  const { choice, setChoice } = useTheme();
  const [mode, setMode] = useState<AuthMode | null>(null);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [ssoEmail, setSsoEmail] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [pending, setPending] = useState<string | null>(null);
  const [challengeId, setChallengeId] = useState<string | null>(null);
  const [code, setCode] = useState("");
  const [passkey, setPasskey] = useState(false);

  useEffect(() => {
    void api<AuthMode>("/api/v1/auth/mode", { skipRefresh: true }).then(setMode).catch(() => setMode(null));
  }, []);

  async function finish(session: AuthResponse) {
    if (session.mfaRequired && session.pendingAuthenticationToken) {
      setPending(session.pendingAuthenticationToken);
      setChallengeId(session.challengeId ?? null);
      setBusy(false);
      return;
    }
    await refresh();
    navigate("/");
  }

  async function onPassword(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setNotice(null);
    setBusy(true);
    try {
      const session = await api<AuthResponse>("/api/v1/auth/login", {
        method: "POST",
        body: { email, password },
        skipRefresh: true,
      });
      await finish(session);
    } catch (caught) {
      setError(messageFor(t, caught instanceof ApiError ? caught.code : "generic"));
      setBusy(false);
    }
  }

  async function onMfa(event: React.FormEvent) {
    event.preventDefault();
    if (!pending) return;
    setError(null);
    setBusy(true);
    try {
      const session = await api<AuthResponse>("/api/v1/auth/mfa", {
        method: "POST",
        body: { pendingAuthenticationToken: pending, code, challengeId },
        skipRefresh: true,
      });
      await finish(session);
    } catch (caught) {
      setError(messageFor(t, caught instanceof ApiError ? caught.code : "mfa_invalid"));
      setBusy(false);
    }
  }

  async function startExternal(provider: "google" | "microsoft" | "sso") {
    setError(null);
    setNotice(null);
    const state = crypto.randomUUID().replace(/-/g, "");
    sessionStorage.setItem("d2d-oauth-state", state);
    setBusy(true);
    try {
      const result = await api<{ url: string }>("/api/v1/auth/authorize", {
        method: "POST",
        body: {
          provider,
          redirectUri: `${window.location.origin}/auth/callback`,
          state,
          email: provider === "sso" ? ssoEmail : null,
        },
        skipRefresh: true,
      });
      window.location.assign(result.url);
    } catch (caught) {
      setError(messageFor(t, caught instanceof ApiError ? caught.code : "generic"));
      setBusy(false);
    }
  }

  async function forgot() {
    setError(null);
    setNotice(null);
    if (!email.trim()) {
      setError(t("login.errors.email_required"));
      return;
    }
    setBusy(true);
    try {
      const result = await api<{ result: string }>("/api/v1/auth/forgot", {
        method: "POST",
        body: { email },
        skipRefresh: true,
      });
      setNotice(result.result === "unavailable" ? t("login.resetUnavailable") : t("login.resetSent"));
    } catch {
      setNotice(t("login.resetSent"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="login-stage">
      <div className="toggle login-theme" role="group" aria-label={t("theme.label")}>
        {(["light", "dark", "system", "high-contrast"] as ThemeChoice[]).map((theme) => (
          <button key={theme} type="button" className={choice === theme ? "on" : ""} onClick={() => setChoice(theme)}>
            {t(`theme.${theme === "high-contrast" ? "contrast" : theme}`)}
          </button>
        ))}
      </div>
      <section className="login-card">
        <div className="brand"><div className="mark">D2D</div><span>{t("productName")}</span></div>
        {mode?.localBanner && <div className="local-banner" role="status">{t("login.localBanner")}</div>}
        {pending ? (
          <form onSubmit={(event) => void onMfa(event)}>
            <h1>{t("login.mfaTitle")}</h1>
            <div className="sub">{t("login.mfaSub")}</div>
            <div className="field">
              <label htmlFor="code">{t("login.codeLabel")}</label>
              <input
                id="code"
                inputMode="numeric"
                autoComplete="one-time-code"
                maxLength={6}
                value={code}
                placeholder="000000"
                onChange={(event) => setCode(event.target.value.replace(/\D/g, "").slice(0, 6))}
              />
              <div className="hint">{t("login.codeHint")}</div>
            </div>
            {error && <div className="error" role="alert">{error}</div>}
            <button className="btn primary auth-btn" type="submit" disabled={busy || code.length !== 6}>{t("login.verify")}</button>
            <button className="btn auth-btn" type="button" onClick={() => setPasskey(true)}>{t("login.passkey")}</button>
            {passkey && <div className="notice">{t("login.passkeyNote")}</div>}
          </form>
        ) : (
          <>
            <h1>{t("login.title")}</h1>
            <div className="sub">{t("login.sub")}</div>
            <button className="btn auth-btn" type="button" disabled={busy} onClick={() => void startExternal("google")}>{t("login.google")}</button>
            <button className="btn auth-btn" type="button" disabled={busy} onClick={() => void startExternal("microsoft")}>{t("login.microsoft")}</button>
            <div className="field">
              <label htmlFor="sso">{t("login.ssoLabel")}</label>
              <input id="sso" type="email" autoComplete="username" placeholder={t("login.ssoPlaceholder")} value={ssoEmail} onChange={(event) => setSsoEmail(event.target.value)} />
            </div>
            <button className="btn auth-btn" type="button" disabled={busy} onClick={() => void startExternal("sso")}>{t("login.sso")}</button>
            <div className="hint">{t("login.ssoHelp")}</div>
            <div className="divider">{t("login.or")}</div>
            <form onSubmit={(event) => void onPassword(event)}>
              <div className="field">
                <label htmlFor="email">{t("login.email")}</label>
                <input id="email" type="email" autoComplete="username" placeholder={t("login.emailPlaceholder")} value={email} onChange={(event) => setEmail(event.target.value)} required />
              </div>
              <div className="field">
                <div className="label-row">
                  <label htmlFor="password">{t("login.password")}</label>
                  <button className="forgot" type="button" onClick={() => void forgot()}>{t("login.forgot")}</button>
                </div>
                <input id="password" type="password" autoComplete="current-password" placeholder={t("login.passwordPlaceholder")} value={password} onChange={(event) => setPassword(event.target.value)} required />
              </div>
              {error && <div className="error" role="alert">{error}</div>}
              {notice && <div className="notice" role="status">{notice}</div>}
              <button className="btn primary auth-btn" type="submit" disabled={busy} aria-busy={busy}>{busy ? t("login.working") : t("login.submit")}</button>
            </form>
            {mode?.localBanner && mode.exampleEmails.length > 0 && (
              <div className="notice">
                <b>{t("login.exampleAccounts")}</b>
                <div>{mode.exampleEmails.join(", ")}</div>
              </div>
            )}
            <div className="login-note">{t("login.auditorNote")}</div>
          </>
        )}
        {busy && <div className="busy" role="progressbar" aria-label={t("login.working")}><i /></div>}
      </section>
    </div>
  );
}
