import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { ApiError, api } from "../../shared/api/client";
import type { SignupAccepted, SignupOptions } from "../onboarding/types";

export function SignupPage() {
  const { t } = useTranslation();
  const [options, setOptions] = useState<SignupOptions | null>(null);
  const [companyName, setCompanyName] = useState("");
  const [fullName, setFullName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [region, setRegion] = useState("US");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [accepted, setAccepted] = useState<SignupAccepted | null>(null);

  useEffect(() => {
    void api<SignupOptions>("/api/v1/signup/options", { skipRefresh: true })
      .then(setOptions)
      .catch(() => setOptions(null));
  }, []);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const result = await api<SignupAccepted>("/api/v1/signup", {
        method: "POST",
        body: {
          companyName,
          email,
          password: options?.passwordRequired === false ? null : password,
          region,
          fullName: fullName || null,
        },
        skipRefresh: true,
      });
      setAccepted(result);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.code : "generic");
    } finally {
      setBusy(false);
    }
  }

  const passwordRequired = options?.passwordRequired ?? true;
  const devLink = accepted?.devVerificationUrl ? toLocal(accepted.devVerificationUrl) : null;

  return (
    <div className="login-stage">
      <section className="login-card">
        <div className="brand">
          <div className="mark">D2D</div>
          <span>{t("productName")}</span>
        </div>
        {accepted ? (
          <div>
            <h1>{t("signup.checkEmail")}</h1>
            <div className="sub">{t("signup.checkEmailSub", { email: accepted.email })}</div>
            {devLink && (
              <div className="notice" role="status">
                <b>{t("signup.devLinkTitle")}</b>
                <div>{t("signup.devLinkSub")}</div>
                <Link className="btn primary auth-btn" to={devLink}>
                  {t("signup.verifyNow")}
                </Link>
              </div>
            )}
          </div>
        ) : (
          <form onSubmit={(event) => void submit(event)}>
            <h1>{t("signup.title")}</h1>
            <div className="sub">{t("signup.sub")}</div>
            <div className="field">
              <label htmlFor="signup-company">{t("signup.company")}</label>
              <input
                id="signup-company"
                value={companyName}
                autoComplete="organization"
                onChange={(event) => setCompanyName(event.target.value)}
                required
              />
            </div>
            <div className="field">
              <label htmlFor="signup-name">{t("signup.fullName")}</label>
              <input
                id="signup-name"
                value={fullName}
                autoComplete="name"
                onChange={(event) => setFullName(event.target.value)}
              />
            </div>
            <div className="field">
              <label htmlFor="signup-email">{t("signup.email")}</label>
              <input
                id="signup-email"
                type="email"
                value={email}
                autoComplete="email"
                placeholder={t("signup.emailPlaceholder")}
                onChange={(event) => setEmail(event.target.value)}
                required
              />
              <div className="hint">{t("signup.emailHint")}</div>
            </div>
            {passwordRequired ? (
              <div className="field">
                <label htmlFor="signup-password">{t("signup.password")}</label>
                <input
                  id="signup-password"
                  type="password"
                  value={password}
                  autoComplete="new-password"
                  minLength={options?.minimumPasswordLength ?? 12}
                  onChange={(event) => setPassword(event.target.value)}
                  required
                />
                <div className="hint">
                  {t("signup.passwordHint", { count: options?.minimumPasswordLength ?? 12 })}
                </div>
              </div>
            ) : (
              <div className="notice">{t("signup.ssoNote")}</div>
            )}
            <div className="field">
              <label htmlFor="signup-region">{t("signup.region")}</label>
              <select
                id="signup-region"
                value={region}
                onChange={(event) => setRegion(event.target.value)}
              >
                {(options?.regions ?? ["US", "EU"]).map((item) => (
                  <option key={item} value={item}>
                    {t(`signup.regions.${item}`, { defaultValue: item })}
                  </option>
                ))}
              </select>
              <div className="hint">{t("signup.regionHint")}</div>
            </div>
            {error && (
              <div className="error" role="alert">
                {t(`signup.errors.${error}`, { defaultValue: t("signup.errors.generic") })}
              </div>
            )}
            <button className="btn primary auth-btn" type="submit" disabled={busy} aria-busy={busy}>
              {busy ? t("login.working") : t("signup.submit")}
            </button>
          </form>
        )}
        <div className="login-note">
          {t("signup.haveAccount")} <Link to="/login">{t("signup.signIn")}</Link>
        </div>
      </section>
    </div>
  );
}

/** Keeps dev links on the current origin so they open inside the SPA router. */
export function toLocal(url: string) {
  try {
    const parsed = new URL(url, window.location.origin);
    return `${parsed.pathname}${parsed.search}`;
  } catch {
    return url;
  }
}
