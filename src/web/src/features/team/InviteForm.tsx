import { useState } from "react";
import { useTranslation } from "react-i18next";
import { ApiError, api } from "../../shared/api/client";
import { UpgradePrompt } from "../billing/UpgradePrompt";
import { toLocal } from "../signup/SignupPage";
import type { CreatedInvite } from "../onboarding/types";

export const inviteRoles = [
  "Admin",
  "ControlOwner",
  "Editor",
  "Viewer",
  "Employee",
  "Auditor",
] as const;

export function InviteForm({
  tenantId,
  onCreated,
}: {
  tenantId: string;
  onCreated?: (created: CreatedInvite) => void;
}) {
  const { t } = useTranslation();
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<string>("Editor");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [created, setCreated] = useState<CreatedInvite | null>(null);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const result = await api<CreatedInvite>("/api/v1/settings/invites", {
        method: "POST",
        tenantId,
        body: { email, role },
      });
      setCreated(result);
      setEmail("");
      onCreated?.(result);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.code : "generic");
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="stack" onSubmit={(event) => void submit(event)}>
      <div className="split-fields">
        <input
          type="email"
          aria-label={t("team.inviteEmail")}
          placeholder={t("team.inviteEmail")}
          value={email}
          onChange={(event) => setEmail(event.target.value)}
          required
        />
        <select
          aria-label={t("team.inviteRole")}
          value={role}
          onChange={(event) => setRole(event.target.value)}
        >
          {inviteRoles.map((item) => (
            <option key={item} value={item}>
              {t(`settingsPage.roles.${item}`)}
            </option>
          ))}
        </select>
      </div>
      <div className="row">
        <button className="btn primary" type="submit" disabled={busy}>
          {t("team.sendInvite")}
        </button>
      </div>
      {error && (
        <div className="error" role="alert">
          {t(`team.errors.${error}`, { defaultValue: t("errors.load") })}
        </div>
      )}
      {created && (
        <div className="notice" role="status">
          {t("team.invited", { email: created.invite.email })}
          {created.devAcceptUrl && (
            <div>
              {t("team.devLink")} <a href={toLocal(created.devAcceptUrl)}>{t("team.openLink")}</a>
            </div>
          )}
        </div>
      )}
      <UpgradePrompt decision={created?.seats} />
    </form>
  );
}
