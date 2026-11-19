import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import { ApiError, api, type Workspace } from "../../shared/api/client";
import { formatDate, formatWhen, Pill } from "../../shared/ui/ui";
import type { Grant, Member, SsoStatus } from "./types";

type Rule = {
  id: string;
  name: string;
  trigger: string;
  action: string;
  target: string;
  enabled: boolean;
};
type Delivery = {
  id: string;
  trigger: string;
  action: string;
  target: string;
  summary: string;
  externalId: string;
  at: string;
};
const triggers = ["TestFailed", "ControlNotReady", "RiskCreated", "ReviewDue"] as const;
const actions = ["Jira", "Slack", "Teams", "Email"] as const;

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

const roles = ["Admin", "Editor", "Viewer", "Employee", "Auditor", "ControlOwner"] as const;

export function SettingsPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [members, setMembers] = useState<Member[] | null>(null);
  const [sso, setSso] = useState<SsoStatus[]>([]);
  const [grants, setGrants] = useState<Grant[]>([]);
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<(typeof roles)[number]>("Viewer");
  const [rules, setRules] = useState<Rule[]>([]);
  const [deliveries, setDeliveries] = useState<Delivery[]>([]);
  const [rule, setRule] = useState({
    name: "",
    trigger: "TestFailed",
    action: "Jira",
    target: "",
    enabled: true,
  });
  const [grantEmail, setGrantEmail] = useState("auditor@northgate.example");
  const [auditName, setAuditName] = useState("SOC 2 Type II");
  const [firmName, setFirmName] = useState("Northgate Assurance LLP");
  const [error, setError] = useState<string | null>(null);

  function load() {
    if (!tenantId || workspace?.role !== "Admin") return;
    void Promise.all([
      api<Member[]>("/api/v1/settings/members", { tenantId }),
      api<SsoStatus | SsoStatus[]>("/api/v1/settings/sso", { tenantId }),
      api<Grant[]>("/api/v1/settings/grants", { tenantId }),
      api<Rule[]>("/api/v1/settings/automation", { tenantId }),
      api<Delivery[]>("/api/v1/settings/automation/deliveries", { tenantId }),
    ])
      .then(([people, domains, rows, automation, sent]) => {
        setMembers(people);
        setSso(Array.isArray(domains) ? domains : domains ? [domains] : []);
        setGrants(rows);
        setRules(automation);
        setDeliveries(sent);
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
    setError(null);
    try {
      await api("/api/v1/settings/members", {
        method: "POST",
        tenantId,
        body: { name, email, role },
      });
      setName("");
      setEmail("");
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  async function changeRole(id: string, next: string) {
    if (!tenantId) return;
    setError(null);
    try {
      await api(`/api/v1/settings/members/${id}/role`, {
        method: "POST",
        tenantId,
        body: { role: next },
      });
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  async function deactivate(id: string) {
    if (!tenantId) return;
    setError(null);
    try {
      await api(`/api/v1/settings/members/${id}/deactivate`, {
        method: "POST",
        tenantId,
        body: {},
      });
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  async function saveGrant(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId) return;
    setError(null);
    try {
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
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    }
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
          {error === "last_admin" ? t("settingsPage.lastAdmin") : t("errors.load")}
        </div>
      )}
      <div className="card">
        <h3>{t("settingsPage.sso")}</h3>
        {sso.map((entry) => (
          <p key={entry.domain || "default"}>
            {entry.domain} ·{" "}
            {entry.connected ? t("settingsPage.connected") : t("settingsPage.notConnected")}
          </p>
        ))}
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
        <h3>{t("settingsPage.automation")}</h3>
        <p className="m">{t("settingsPage.automationHelp")}</p>
        <ul>
          {rules.map((item) => (
            <li key={item.id}>
              <b>{item.name}</b> · {t(`settingsPage.triggers.${item.trigger}`)} ·{" "}
              {t(`settingsPage.actions.${item.action}`)} · {item.target}
            </li>
          ))}
        </ul>
        <form
          onSubmit={(event) => {
            event.preventDefault();
            if (!tenantId) return;
            void api("/api/v1/settings/automation", { method: "POST", tenantId, body: rule }).then(
              () => {
                setRule({ ...rule, name: "", target: "" });
                load();
              },
            );
          }}
        >
          <div className="split-fields">
            <input
              aria-label={t("settingsPage.ruleName")}
              placeholder={t("settingsPage.ruleName")}
              value={rule.name}
              onChange={(event) => setRule({ ...rule, name: event.target.value })}
              required
            />
            <select
              aria-label={t("settingsPage.trigger")}
              value={rule.trigger}
              onChange={(event) => setRule({ ...rule, trigger: event.target.value })}
            >
              {triggers.map((item) => (
                <option key={item} value={item}>
                  {t(`settingsPage.triggers.${item}`)}
                </option>
              ))}
            </select>
            <select
              aria-label={t("settingsPage.action")}
              value={rule.action}
              onChange={(event) => setRule({ ...rule, action: event.target.value })}
            >
              {actions.map((item) => (
                <option key={item} value={item}>
                  {t(`settingsPage.actions.${item}`)}
                </option>
              ))}
            </select>
            <input
              aria-label={t("settingsPage.target")}
              placeholder={t("settingsPage.target")}
              value={rule.target}
              onChange={(event) => setRule({ ...rule, target: event.target.value })}
              required
            />
          </div>
          <button className="btn" type="submit">
            {t("settingsPage.saveRule")}
          </button>
          <button
            className="btn"
            type="button"
            onClick={() => {
              if (!tenantId) return;
              void api("/api/v1/settings/automation/run", {
                method: "POST",
                tenantId,
                body: {},
              }).then(() => load());
            }}
          >
            {t("settingsPage.runDue")}
          </button>
        </form>
        <h3>{t("settingsPage.deliveries")}</h3>
        <ul>
          {deliveries.map((item) => (
            <li key={item.id}>
              {item.externalId} · {item.summary} · {formatWhen(item.at)}
            </li>
          ))}
        </ul>
      </div>
    </>
  );
}
