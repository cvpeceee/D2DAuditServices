import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { ApiError, api } from "../../shared/api/client";
import { formatWhen, Pill } from "../../shared/ui/ui";
import type { Invite, ScimToken, SecuritySettings } from "../onboarding/types";
import { InviteForm } from "./InviteForm";

function inviteTone(state: string) {
  if (state === "Accepted") return "pass";
  if (state === "Pending") return "info";
  if (state === "Expired") return "warn";
  return "neutral";
}

export function TeamAccessPanel({ tenantId }: { tenantId: string | null }) {
  const { t } = useTranslation();
  const [invites, setInvites] = useState<Invite[]>([]);
  const [security, setSecurity] = useState<SecuritySettings | null>(null);
  const [token, setToken] = useState<ScimToken | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(() => {
    if (!tenantId) return;
    void api<Invite[]>("/api/v1/settings/invites", { tenantId })
      .then((rows) => setInvites(Array.isArray(rows) ? rows : []))
      .catch(() => setInvites([]));
    void api<SecuritySettings>("/api/v1/settings/security", { tenantId })
      .then((value) => setSecurity(value && "scimEndpoint" in value ? value : null))
      .catch(() => setSecurity(null));
  }, [tenantId]);

  useEffect(load, [load]);

  async function act<T>(work: () => Promise<T>) {
    setError(null);
    try {
      return await work();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.code : "load");
      return null;
    }
  }

  async function revoke(id: string) {
    if (!tenantId) return;
    await act(() =>
      api(`/api/v1/settings/invites/${id}/revoke`, { method: "POST", tenantId, body: {} }),
    );
    load();
  }

  async function enforce(next: boolean) {
    if (!tenantId) return;
    const result = await act(() =>
      api<SecuritySettings>("/api/v1/settings/security", {
        method: "PUT",
        tenantId,
        body: { ssoEnforced: next },
      }),
    );
    if (result) setSecurity(result);
  }

  async function rotate() {
    if (!tenantId) return;
    const result = await act(() =>
      api<ScimToken>("/api/v1/settings/security/scim-token", {
        method: "POST",
        tenantId,
        body: {},
      }),
    );
    if (result) {
      setToken(result);
      load();
    }
  }

  return (
    <>
      {error && (
        <div className="error" role="alert" style={{ marginTop: 14 }}>
          {t(`team.errors.${error}`, { defaultValue: t("errors.load") })}
        </div>
      )}
      <section className="card stack" style={{ marginTop: 14 }} aria-labelledby="team-invites">
        <h3 id="team-invites">
          {t("team.invitesTitle")}
          <Link className="r" to="/settings/billing">
            {t("team.billingLink")}
          </Link>
        </h3>
        <div className="sub">{t("team.invitesSub")}</div>
        {tenantId && <InviteForm tenantId={tenantId} onCreated={load} />}
        {invites.length > 0 && (
          <table>
            <thead>
              <tr>
                <th>{t("settingsPage.email")}</th>
                <th>{t("settingsPage.role")}</th>
                <th>{t("team.expires")}</th>
                <th>{t("settingsPage.state")}</th>
              </tr>
            </thead>
            <tbody>
              {invites.map((invite) => (
                <tr key={invite.id}>
                  <td>
                    <b>{invite.email}</b>
                    <div className="id">{t("team.invitedBy", { name: invite.invitedBy })}</div>
                  </td>
                  <td>{t(`settingsPage.roles.${invite.role}`, { defaultValue: invite.role })}</td>
                  <td>{formatWhen(invite.expiresAt)}</td>
                  <td>
                    <Pill status={inviteTone(invite.state)}>
                      {t(`team.states.${invite.state}`, { defaultValue: invite.state })}
                    </Pill>
                    {invite.state === "Pending" && (
                      <button className="btn" type="button" onClick={() => void revoke(invite.id)}>
                        {t("team.revoke")}
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
      {security && (
        <section className="card stack" style={{ marginTop: 14 }} aria-labelledby="team-security">
          <h3 id="team-security">{t("team.securityTitle")}</h3>
          {security.mode !== "workos" && <div className="notice">{t("team.mockSso")}</div>}
          <label className="check-row">
            <input
              type="checkbox"
              checked={security.ssoEnforced}
              onChange={(event) => void enforce(event.target.checked)}
            />
            {t("team.enforceSso", { domain: security.domain || "—" })}
          </label>
          <div className="hint">{t("team.enforceHint")}</div>
          <div>
            <b>{t("team.scimTitle")}</b>
            <div className="id">{t("team.scimEndpoint", { url: security.scimEndpoint })}</div>
            <div className="id">
              {security.scimTokenPrefix
                ? t("team.scimToken", {
                    prefix: security.scimTokenPrefix,
                    date: security.scimTokenCreatedAt
                      ? formatWhen(security.scimTokenCreatedAt)
                      : "",
                  })
                : t("team.scimNone")}
            </div>
          </div>
          {token && (
            <div className="notice" role="status">
              <b>{t("settingsPage.copyOnce")}</b>
              <div>
                <code>{token.token}</code>
              </div>
            </div>
          )}
          <div className="row">
            <button className="btn" type="button" onClick={() => void rotate()}>
              {security.scimTokenPrefix ? t("team.rotateScim") : t("team.createScim")}
            </button>
          </div>
        </section>
      )}
    </>
  );
}
