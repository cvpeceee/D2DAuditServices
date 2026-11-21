import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link, useSearchParams } from "react-router-dom";
import { ApiError, api } from "../../shared/api/client";

const minLength = 12;

export function ResetPasswordPage() {
  const { t } = useTranslation();
  const [params] = useSearchParams();
  const token = params.get("token") ?? "";
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    if (password.length < minLength) {
      setError(t("reset.errors.weak_password", { count: minLength }));
      return;
    }
    if (password !== confirm) {
      setError(t("reset.errors.mismatch"));
      return;
    }
    setBusy(true);
    try {
      await api<{ result: string }>("/api/v1/auth/reset", {
        method: "POST",
        body: { token, password },
        skipRefresh: true,
      });
      setDone(true);
    } catch (caught) {
      const code = caught instanceof ApiError ? caught.code : "generic";
      setError(
        t(`reset.errors.${code}`, {
          count: minLength,
          defaultValue: t("reset.errors.generic"),
        }),
      );
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="login-stage">
      <section className="login-card">
        <div className="brand">
          <div className="mark">D2D</div>
          <span>{t("productName")}</span>
        </div>
        <h1>{t("reset.title")}</h1>
        {!token ? (
          <>
            <div className="error" role="alert">
              {t("reset.errors.invalid_token")}
            </div>
            <Link className="btn auth-btn" to="/login">
              {t("reset.back")}
            </Link>
          </>
        ) : done ? (
          <>
            <div className="notice" role="status">
              {t("reset.done")}
            </div>
            <Link className="btn primary auth-btn" to="/login">
              {t("reset.signIn")}
            </Link>
          </>
        ) : (
          <form onSubmit={(event) => void submit(event)} noValidate>
            <div className="sub">{t("reset.sub", { count: minLength })}</div>
            <div className="field">
              <label htmlFor="new-password">{t("reset.password")}</label>
              <input
                id="new-password"
                type="password"
                autoComplete="new-password"
                value={password}
                minLength={minLength}
                maxLength={200}
                aria-describedby="new-password-hint"
                onChange={(event) => setPassword(event.target.value)}
                required
              />
              <div className="hint" id="new-password-hint">
                {t("reset.hint", { count: minLength })}
              </div>
            </div>
            <div className="field">
              <label htmlFor="confirm-password">{t("reset.confirm")}</label>
              <input
                id="confirm-password"
                type="password"
                autoComplete="new-password"
                value={confirm}
                maxLength={200}
                onChange={(event) => setConfirm(event.target.value)}
                required
              />
            </div>
            {error && (
              <div className="error" role="alert">
                {error}
              </div>
            )}
            <button className="btn primary auth-btn" type="submit" disabled={busy} aria-busy={busy}>
              {t("reset.submit")}
            </button>
            <Link className="btn auth-btn" to="/login">
              {t("reset.back")}
            </Link>
          </form>
        )}
      </section>
    </div>
  );
}
