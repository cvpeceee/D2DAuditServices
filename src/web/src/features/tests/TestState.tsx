import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { ApiError, api } from "../../shared/api/client";

export type TestState = {
  testKey: string;
  state: "Enabled" | "Disabled" | "Snoozed";
  reason: string;
  until: string | null;
  byName: string;
  at: string;
  active: boolean;
};

export function TestStateBadge({ state }: { state: TestState | undefined }) {
  const { t } = useTranslation();
  if (!state || !state.active || state.state === "Enabled") return null;
  return (
    <div className="m" title={state.reason}>
      <span className="tag">{t(`assurance.state.${state.state}`)}</span>
      {state.until ? ` ${t("assurance.state.until", { date: state.until.slice(0, 10) })}` : ""}
      {` · ${state.reason} (${state.byName})`}
    </div>
  );
}

export function TestStateActions({
  tenantId,
  testKey,
  state,
  onChanged,
}: {
  tenantId: string | null;
  testKey: string;
  state: TestState | undefined;
  onChanged: () => void;
}) {
  const { t } = useTranslation();
  const [mode, setMode] = useState<"Disabled" | "Snoozed" | null>(null);
  const [reason, setReason] = useState("");
  const [until, setUntil] = useState(
    new Date(Date.now() + 14 * 86_400_000).toISOString().slice(0, 10),
  );
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const suppressed = Boolean(state?.active && state.state !== "Enabled");

  const send = async (body: { state: string; reason: string; until: string | null }) => {
    setBusy(true);
    setError(null);
    try {
      await api(`/api/v1/tests/${encodeURIComponent(testKey)}/state`, {
        method: "PUT",
        tenantId,
        body,
      });
      setMode(null);
      setReason("");
      onChanged();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.code : "save");
    } finally {
      setBusy(false);
    }
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (!mode) return;
    void send({ state: mode, reason, until: mode === "Snoozed" ? until : null });
  };

  return (
    <div className="stack" style={{ marginTop: 10 }}>
      <div className="act">
        {suppressed ? (
          <button
            className="btn"
            type="button"
            disabled={busy}
            onClick={() => void send({ state: "Enabled", reason: "", until: null })}
          >
            {t("assurance.state.enable")}
          </button>
        ) : (
          <>
            <button className="btn" type="button" onClick={() => setMode("Disabled")}>
              {t("assurance.state.disable")}
            </button>
            <button className="btn" type="button" onClick={() => setMode("Snoozed")}>
              {t("assurance.state.snooze")}
            </button>
          </>
        )}
      </div>
      {mode && (
        <form className="stack" onSubmit={submit}>
          <label className="field">
            <span>{t("assurance.state.reason")}</span>
            <input required value={reason} onChange={(event) => setReason(event.target.value)} />
          </label>
          {mode === "Snoozed" && (
            <label className="field">
              <span>{t("assurance.state.untilLabel")}</span>
              <input
                type="date"
                required
                value={until}
                onChange={(event) => setUntil(event.target.value)}
              />
            </label>
          )}
          <div className="act">
            <button className="btn primary" type="submit" disabled={busy}>
              {t(mode === "Snoozed" ? "assurance.state.snooze" : "assurance.state.disable")}
            </button>
            <button className="btn" type="button" onClick={() => setMode(null)}>
              {t("assurance.cancel")}
            </button>
          </div>
        </form>
      )}
      {error && (
        <div className="error" role="alert">
          {t(`assurance.errors.${error}`, { defaultValue: error })}
        </div>
      )}
    </div>
  );
}
