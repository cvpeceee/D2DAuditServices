import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import { ApiError, api, type Workspace } from "../../shared/api/client";
import { formatDate, Pill } from "../../shared/ui/ui";
import type { Grant, Member, SsoStatus } from "./types";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

const roles = ["Admin", "Editor", "Viewer", "Employee", "Auditor", "ControlOwner"] as const;

export function SettingsPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [members, setMembers] = useState<Member[] | null>(null);
  const [sso, setSso] = useState<SsoStatus | null>(null);
  const [grants, setGrants] = useState<Grant[]>([]);
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<(typeof roles)[number]>("Viewer");
  const [grantEmail, setGrantEmail] = useState("auditor@northgate.example");
  const [auditName, setAuditName] = useState("SOC 2 Type II");
  const [firmName, setFirmName] = useState("Northgate Assurance LLP");
  const [error, setError] = useState<string | null>(null);

  function load() {
    if (!tenantId || workspace?.role !== "Admin") return;
    void Promise.all([
      api<Member[]>("/api/v1/settings/members", { tenantId }),
      api<SsoStatus>("/api/v1/settings/sso", { tenantId }),
      api<Grant[]>("/api/v1/settings/grants", { tenantId }),
    ])
      .then(([people, domains, rows]) => {
        setMembers(people);
        setSso(domains);
        setGrants(rows);
      })
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId, workspace?.role]);

  async function invite(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId) return;
    await api("/api/v1/settings/members", {
      method: "POST",
      tenantId,
      body: { name, email, role },
    });
    setName("");
    setEmail("");
    load();
  }

  async function changeRole(id: string, next: string) {
    if (!tenantId) return;
    await api(`/api/v1/settings/members/${id}/role`, {
      method: "POST",
      tenantId,
      body: { role: next },
    });
    load();
  }

  async function deactivate(id: string) {
    if (!tenantId) return;
    await api(`/api/v1/settings/members/${id}/deactivate`, { method: "POST", tenantId, body: {} });
    load();
  }

  async function saveGrant(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId) return;
    await api("/api/v1/settings/grants", {
      method: "POST",
      tenantId,
      body: {
        email: grantEmail,
        auditName,
        firmName,
        periodStart: "2026-04-01",
        periodEnd: "2026-09-30",
        accessExpiresOn: "2026-12-15",
        frameworks: ["soc2"],
      },
    });
    load();
  }

  if (workspace && workspace.role !== "Admin") {
    return <div className="empty">{t("settingsPage.admins")}</div>;
  }

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("settingsPage.title")}</h1>
          <div className="sub">{t("settingsPage.sub")}</div>
        </div>
      </div>
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}
      <div className="card">
        <h3>{t("settingsPage.sso")}</h3>
        {sso && (
          <p>
            {sso.domain} ·{" "}
            {sso.connected ? t("settingsPage.connected") : t("settingsPage.notConnected")}
          </p>
        )}
      </div>
      <form className="card" style={{ marginTop: 14 }} onSubmit={(event) => void invite(event)}>
        <h3>{t("settingsPage.invite")}</h3>
        <div className="split-fields">
          <input
            value={name}
            placeholder={t("settingsPage.name")}
            onChange={(event) => setName(event.target.value)}
            required
          />
          <input
            value={email}
            placeholder={t("settingsPage.email")}
            onChange={(event) => setEmail(event.target.value)}
            required
          />
          <select
            value={role}
            onChange={(event) => setRole(event.target.value as (typeof roles)[number])}
          >
            {roles.map((item) => (
              <option key={item} value={item}>
                {t(`settingsPage.roles.${item}`)}
              </option>
            ))}
          </select>
        </div>
        <button className="btn primary" type="submit">
          {t("settingsPage.invite")}
        </button>
      </form>
      <div className="card" style={{ marginTop: 14 }}>
        <table>
          <thead>
            <tr>
              <th>{t("settingsPage.name")}</th>
              <th>{t("settingsPage.role")}</th>
              <th>{t("settingsPage.state")}</th>
            </tr>
          </thead>
          <tbody>
            {(members ?? []).map((member) => (
              <tr key={member.id}>
                <td>
                  <b>{member.name}</b>
                  <div className="id">{member.email}</div>
                </td>
                <td>
                  <select
                    value={member.role}
                    onChange={(event) => void changeRole(member.id, event.target.value)}
                  >
                    {roles.map((item) => (
                      <option key={item} value={item}>
                        {t(`settingsPage.roles.${item}`)}
                      </option>
                    ))}
                  </select>
                </td>
                <td>
                  {member.active ? (
                    <Pill status="pass">{t("settingsPage.active")}</Pill>
                  ) : (
                    <Pill status="fail">{t("settingsPage.deactivated")}</Pill>
                  )}
                  {member.active && (
                    <button
                      className="btn"
                      type="button"
                      onClick={() => void deactivate(member.id)}
                    >
                      {t("settingsPage.deactivate")}
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="card" style={{ marginTop: 14 }}>
        <h3>{t("settingsPage.grants")}</h3>
        <ul>
          {grants.map((grant) => (
            <li key={grant.id}>
              {grant.name} · {grant.auditName} · {grant.firmName} ·{" "}
              {formatDate(grant.accessExpiresOn)} · {grant.frameworks.join(", ")}
            </li>
          ))}
        </ul>
        <form onSubmit={(event) => void saveGrant(event)}>
          <div className="split-fields">
            <input value={grantEmail} onChange={(event) => setGrantEmail(event.target.value)} />
            <input value={auditName} onChange={(event) => setAuditName(event.target.value)} />
            <input value={firmName} onChange={(event) => setFirmName(event.target.value)} />
          </div>
          <button className="btn" type="submit">
            {t("settingsPage.saveGrant")}
          </button>
        </form>
      </div>
    </>
  );
}
