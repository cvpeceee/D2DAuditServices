import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import { ApiError, api, type Workspace } from "../../shared/api/client";
import { formatWhen, Pill } from "../../shared/ui/ui";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

type Computer = {
  id: string;
  serial: string;
  operatingSystem: string;
  ownerName: string;
  ownerEmail: string;
  encrypted: boolean;
  screenLock: boolean;
  antivirus: boolean;
  passwordManager: boolean;
  autoUpdate: boolean;
  lastCheckIn: string;
  source: string;
  screenshotName: string | null;
};

type AgentCheckView = {
  key: string;
  title: string;
  controlCode: string;
  pass: boolean;
  detail: string;
  remediation: string[];
};

type AgentDeviceItem = {
  id: string;
  serial: string;
  hostname: string;
  platform: string;
  operatingSystem: string;
  personEmail: string;
  personName: string;
  agentKind: string;
  agentVersion: string;
  enrolledAt: string;
  lastSeenAt: string | null;
  stale: boolean;
  revoked: boolean;
  passing: number;
  failing: number;
  checks: AgentCheckView[];
};

type PostureHistoryItem = {
  at: string;
  agentKind: string;
  agentVersion: string;
  passing: number;
  failing: number;
  checks: AgentCheckView[];
};

type EnrollmentTokenCreated = {
  id: string;
  token: string;
  personEmail: string;
  personName: string;
  expiresAt: string;
};

type EnrollmentTokenItem = {
  id: string;
  hint: string;
  personEmail: string;
  personName: string;
  createdByEmail: string;
  createdAt: string;
  expiresAt: string;
  state: string;
};

type InstallerOs = "windows" | "macos" | "linux" | "dotnet";

const blank = {
  serial: "",
  operatingSystem: "",
  ownerName: "",
  ownerEmail: "",
  encrypted: true,
  screenLock: true,
  antivirus: true,
  passwordManager: true,
  autoUpdate: true,
  source: "Manual",
};

function flag(on: boolean, yes: string, no: string) {
  return <Pill status={on ? "pass" : "fail"}>{on ? yes : no}</Pill>;
}

function installerCommand(os: InstallerOs, server: string, token: string): string {
  const safeToken = token || "<ENROLLMENT_TOKEN>";
  if (os === "windows") {
    return `.\\agent\\windows\\install.ps1 -Server ${server} -Token ${safeToken}`;
  }
  if (os === "macos") {
    return `./agent/unix/install.sh --server ${server} --token ${safeToken}  # installs LaunchAgent com.d2daudit.agent.plist`;
  }
  if (os === "linux") {
    return `./agent/unix/install.sh --server ${server} --token ${safeToken}  # enables systemd user timer d2d-agent.timer`;
  }
  return `d2d-agent enroll --server ${server} --token ${safeToken} && d2d-agent run`;
}

export function ComputersPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [tab, setTab] = useState<"agent" | "inventory">("agent");
  const [rows, setRows] = useState<Computer[] | null>(null);
  const [agentDevices, setAgentDevices] = useState<AgentDeviceItem[] | null>(null);
  const [tokens, setTokens] = useState<EnrollmentTokenItem[] | null>(null);
  const [issued, setIssued] = useState<EnrollmentTokenCreated | null>(null);
  const [personEmail, setPersonEmail] = useState("");
  const [installerOs, setInstallerOs] = useState<InstallerOs>("windows");
  const [copiedKey, setCopiedKey] = useState<string | null>(null);
  const [selectedDeviceId, setSelectedDeviceId] = useState<string | null>(null);
  const [history, setHistory] = useState<PostureHistoryItem[] | null>(null);
  const [form, setForm] = useState(blank);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const serverUrl =
    typeof window !== "undefined" && window.location?.origin
      ? window.location.origin
      : "https://app.d2daudit.example";

  function load() {
    if (!tenantId) return;
    void api<Computer[]>("/api/v1/computers", { tenantId })
      .then(setRows)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
    void api<AgentDeviceItem[]>("/api/v1/agent/devices", { tenantId })
      .then(setAgentDevices)
      .catch(() => setAgentDevices([]));
    void api<EnrollmentTokenItem[]>("/api/v1/agent/enrollment-tokens", { tenantId })
      .then(setTokens)
      .catch(() => setTokens([]));
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  async function inspectDevice(id: string) {
    if (!tenantId) return;
    if (selectedDeviceId === id) {
      setSelectedDeviceId(null);
      setHistory(null);
      return;
    }
    setSelectedDeviceId(id);
    setHistory(null);
    try {
      const items = await api<PostureHistoryItem[]>(`/api/v1/agent/devices/${id}/history`, {
        tenantId,
      });
      setHistory(items);
    } catch {
      setHistory([]);
    }
  }

  async function issueToken(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId) return;
    setError(null);
    setNotice(null);
    try {
      const created = await api<EnrollmentTokenCreated>("/api/v1/agent/enrollment-tokens", {
        method: "POST",
        tenantId,
        body: { personEmail: personEmail.trim() || null },
      });
      setIssued(created);
      setPersonEmail("");
      setNotice(t("computersPage.agent.tokenIssued", { when: formatWhen(created.expiresAt) }));
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  async function revokeToken(token: EnrollmentTokenItem) {
    if (!tenantId) return;
    setError(null);
    try {
      await api(`/api/v1/agent/enrollment-tokens/${token.id}/revoke`, {
        method: "POST",
        tenantId,
      });
      setNotice(t("computersPage.agent.revokedNotice", { target: token.hint }));
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  async function revokeDevice(device: AgentDeviceItem) {
    if (!tenantId) return;
    setError(null);
    try {
      await api(`/api/v1/agent/devices/${device.id}/revoke`, {
        method: "POST",
        tenantId,
      });
      setNotice(t("computersPage.agent.revokedNotice", { target: device.serial }));
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  async function copyText(key: string, text: string) {
    try {
      if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(text);
      }
      setCopiedKey(key);
      window.setTimeout(() => setCopiedKey((prev) => (prev === key ? null : prev)), 2000);
    } catch {
      // Clipboard may be unavailable in non-secure contexts.
    }
  }

  function downloadSeedConfig() {
    const payload = JSON.stringify(
      { server: serverUrl, token: issued?.token ?? "<ENROLLMENT_TOKEN>" },
      null,
      2,
    );
    const blob = new Blob([payload], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = "d2d-agent.json";
    link.click();
    URL.revokeObjectURL(url);
  }

  async function save(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId) return;
    setError(null);
    try {
      const path = form.source === "Agent" ? "/api/v1/computers/check-in" : "/api/v1/computers";
      await api(path, { method: "POST", tenantId, body: form });
      setForm(blank);
      setNotice(t("computersPage.saved"));
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  async function upload(id: string, file: File) {
    if (!tenantId) return;
    const bytes = new Uint8Array(await file.arrayBuffer());
    let binary = "";
    bytes.forEach((byte) => {
      binary += String.fromCharCode(byte);
    });
    await api(`/api/v1/computers/${id}/screenshot`, {
      method: "POST",
      tenantId,
      body: { fileName: file.name, contentBase64: btoa(binary) },
    });
    setNotice(t("computersPage.uploaded"));
    load();
  }

  const activeDevices = (agentDevices ?? []).filter((d) => !d.revoked);
  const healthyCount = activeDevices.filter((d) => !d.stale && d.failing === 0).length;
  const attentionCount = activeDevices.filter((d) => d.failing > 0).length;
  const staleCount = activeDevices.filter((d) => d.stale).length;
  const activeTokensCount = (tokens ?? []).filter((tk) => tk.state === "Active").length;
  const selectedDevice = (agentDevices ?? []).find((d) => d.id === selectedDeviceId) ?? null;
  const cmd = installerCommand(installerOs, serverUrl, issued?.token ?? "");

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("computersPage.title")}</h1>
          <div className="sub">{t("computersPage.sub")}</div>
        </div>
        {workspace?.canWrite && (
          <div className="act">
            <button
              className="btn"
              type="button"
              onClick={() => {
                if (!tenantId) return;
                void api<{ imported: number }>("/api/v1/computers/mdm-sync", {
                  method: "POST",
                  tenantId,
                }).then((result) => {
                  setNotice(t("computersPage.mdmSynced", { count: result.imported }));
                  load();
                });
              }}
            >
              {t("computersPage.mdmSync")}
            </button>
          </div>
        )}
      </div>

      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}
      {notice && (
        <div className="notice" role="status">
          {notice}
        </div>
      )}

      <div className="tabs" role="tablist" aria-label={t("computersPage.title")}>
        <button
          type="button"
          role="tab"
          aria-selected={tab === "agent"}
          className={tab === "agent" ? "on" : ""}
          onClick={() => setTab("agent")}
        >
          {t("computersPage.tabs.agent")} ({activeDevices.length})
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={tab === "inventory"}
          className={tab === "inventory" ? "on" : ""}
          onClick={() => setTab("inventory")}
        >
          {t("computersPage.tabs.inventory")} ({rows?.length ?? 0})
        </button>
      </div>

      {tab === "agent" && (
        <div className="grid">
          <div
            className="kv"
            style={{ gridTemplateColumns: "repeat(auto-fit, minmax(150px, 1fr))" }}
          >
            <div>
              <small>{t("computersPage.agent.enrolled")}</small>
              <b>{activeDevices.length}</b>
            </div>
            <div>
              <small>{t("computersPage.agent.healthy")}</small>
              <b className="ready">{healthyCount}</b>
            </div>
            <div>
              <small>{t("computersPage.agent.attention")}</small>
              <b>{attentionCount}</b>
            </div>
            <div>
              <small>{t("computersPage.agent.stale")}</small>
              <b>{staleCount}</b>
            </div>
            <div>
              <small>{t("computersPage.agent.activeTokens")}</small>
              <b>{activeTokensCount}</b>
            </div>
          </div>

          <div className="card">
            <h3>{t("computersPage.agent.enrollTitle")}</h3>
            <p className="m">{t("computersPage.agent.enrollSub")}</p>
            <form
              className="row"
              style={{ flexWrap: "wrap", marginBottom: 12 }}
              onSubmit={(event) => void issueToken(event)}
            >
              <input
                className="search-input"
                style={{ maxWidth: 320 }}
                aria-label={t("computersPage.agent.personEmail")}
                placeholder={t("computersPage.agent.personEmail")}
                value={personEmail}
                onChange={(event) => setPersonEmail(event.target.value)}
              />
              <button className="btn primary" type="submit">
                {t("computersPage.agent.issueToken")}
              </button>
              <button className="btn" type="button" onClick={downloadSeedConfig}>
                {t("computersPage.agent.downloadConfig")}
              </button>
            </form>

            {issued && (
              <div className="local-banner" role="status">
                <div className="row" style={{ justifyContent: "space-between", flexWrap: "wrap" }}>
                  <div>
                    <b>{issued.personName}</b> <span className="m">({issued.personEmail})</span>
                    <div>
                      <code>{issued.token}</code>
                    </div>
                  </div>
                  <button
                    className="btn"
                    type="button"
                    onClick={() => void copyText("token", issued.token)}
                  >
                    {copiedKey === "token"
                      ? t("computersPage.agent.copied")
                      : t("computersPage.agent.copy")}
                  </button>
                </div>
              </div>
            )}

            <div className="toggle" style={{ width: "fit-content", marginBottom: 10 }}>
              {(["windows", "macos", "linux", "dotnet"] as const).map((osKey) => (
                <button
                  key={osKey}
                  type="button"
                  className={installerOs === osKey ? "on" : ""}
                  onClick={() => setInstallerOs(osKey)}
                >
                  {t(`computersPage.agent.installers.${osKey}`)}
                </button>
              ))}
            </div>

            <div
              className="row"
              style={{
                justifyContent: "space-between",
                background: "var(--surface2)",
                border: "1px solid var(--border)",
                borderRadius: 10,
                padding: "10px 12px",
                flexWrap: "wrap",
              }}
            >
              <code style={{ fontFamily: "var(--mono)", fontSize: 12 }}>{cmd}</code>
              <button className="btn" type="button" onClick={() => void copyText("cmd", cmd)}>
                {copiedKey === "cmd"
                  ? t("computersPage.agent.copied")
                  : t("computersPage.agent.copy")}
              </button>
            </div>
          </div>

          <div className="card">
            <h3>{t("computersPage.agent.fleetTitle")}</h3>
            {!agentDevices ? null : agentDevices.length === 0 ? (
              <div className="empty">{t("computersPage.agent.fleetEmpty")}</div>
            ) : (
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>{t("computersPage.serial")}</th>
                      <th>{t("computersPage.agent.hostname")}</th>
                      <th>{t("computersPage.owner")}</th>
                      <th>{t("computersPage.agent.kind")}</th>
                      <th>{t("computersPage.agent.heartbeat")}</th>
                      <th>{t("computersPage.agent.posture")}</th>
                      <th />
                    </tr>
                  </thead>
                  <tbody>
                    {agentDevices.map((device) => (
                      <tr key={device.id} className={device.stale ? "late" : ""}>
                        <td>
                          <b>{device.serial}</b>
                        </td>
                        <td>
                          {device.hostname || "—"}
                          <div className="id">
                            {device.operatingSystem} · {device.platform}
                          </div>
                        </td>
                        <td>
                          {device.personName}
                          <div className="id">{device.personEmail}</div>
                        </td>
                        <td>
                          <span className="tag">{device.agentKind}</span>
                          {device.agentVersion && (
                            <span className="id">v{device.agentVersion}</span>
                          )}
                        </td>
                        <td>
                          {device.lastSeenAt ? formatWhen(device.lastSeenAt) : "—"}
                          <div>
                            {device.revoked ? (
                              <Pill status="neutral">{t("computersPage.agent.revokedBadge")}</Pill>
                            ) : device.stale ? (
                              <Pill status="warn">{t("computersPage.agent.staleBadge")}</Pill>
                            ) : (
                              <Pill status="pass">{t("computersPage.agent.healthyBadge")}</Pill>
                            )}
                          </div>
                        </td>
                        <td>
                          <Pill status={device.failing === 0 ? "pass" : "fail"}>
                            {`${device.passing}/${device.passing + device.failing}`}
                          </Pill>
                        </td>
                        <td>
                          <div className="row">
                            <button
                              className="btn"
                              type="button"
                              onClick={() => void inspectDevice(device.id)}
                            >
                              {selectedDeviceId === device.id
                                ? t("computersPage.agent.hide")
                                : t("computersPage.agent.inspect")}
                            </button>
                            {workspace?.canWrite && !device.revoked && (
                              <button
                                className="btn"
                                type="button"
                                onClick={() => void revokeDevice(device)}
                              >
                                {t("computersPage.agent.revoke")}
                              </button>
                            )}
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {selectedDevice && (
              <div style={{ marginTop: 16, borderTop: "1px solid var(--border)", paddingTop: 14 }}>
                <h3>{t("computersPage.agent.checksTitle", { serial: selectedDevice.serial })}</h3>
                <div className="cardgrid">
                  {selectedDevice.checks.map((check) => (
                    <div key={check.key} className="card">
                      <h3>
                        {check.title}
                        <span className="r">
                          <Pill status={check.pass ? "pass" : "fail"}>
                            {check.pass ? t("computersPage.on") : t("computersPage.off")}
                          </Pill>
                        </span>
                      </h3>
                      <div className="id">{check.controlCode}</div>
                      <p className="m">{check.detail}</p>
                      {!check.pass && check.remediation.length > 0 && (
                        <ul className="m" style={{ paddingInlineStart: 16, margin: "6px 0 0" }}>
                          {check.remediation.map((step) => (
                            <li key={step}>{step}</li>
                          ))}
                        </ul>
                      )}
                    </div>
                  ))}
                </div>

                <h3 style={{ marginTop: 14 }}>{t("computersPage.agent.historyTitle")}</h3>
                {!history || history.length === 0 ? (
                  <div className="empty">{t("computersPage.agent.noHistory")}</div>
                ) : (
                  <div className="table-wrap">
                    <table>
                      <thead>
                        <tr>
                          <th>{t("computersPage.agent.heartbeat")}</th>
                          <th>{t("computersPage.agent.kind")}</th>
                          <th>{t("computersPage.agent.posture")}</th>
                        </tr>
                      </thead>
                      <tbody>
                        {history.map((snap, idx) => (
                          <tr key={`${snap.at}-${idx}`}>
                            <td>{formatWhen(snap.at)}</td>
                            <td>
                              <span className="tag">{snap.agentKind}</span>
                              {snap.agentVersion && (
                                <span className="id">v{snap.agentVersion}</span>
                              )}
                            </td>
                            <td>
                              <Pill status={snap.failing === 0 ? "pass" : "fail"}>
                                {`${snap.passing}/${snap.passing + snap.failing}`}
                              </Pill>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </div>
            )}
          </div>

          {tokens && tokens.length > 0 && (
            <div className="card">
              <h3>{t("computersPage.agent.tokensTitle")}</h3>
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>{t("computersPage.agent.tokenHint")}</th>
                      <th>{t("computersPage.owner")}</th>
                      <th>{t("computersPage.agent.createdBy")}</th>
                      <th>{t("computersPage.agent.expires")}</th>
                      <th>{t("computersPage.agent.state")}</th>
                      <th />
                    </tr>
                  </thead>
                  <tbody>
                    {tokens.map((token) => (
                      <tr key={token.id}>
                        <td className="id">{token.hint}</td>
                        <td>
                          {token.personName}
                          <div className="id">{token.personEmail}</div>
                        </td>
                        <td className="id">{token.createdByEmail}</td>
                        <td>{formatWhen(token.expiresAt)}</td>
                        <td>
                          <Pill
                            status={
                              token.state === "Active"
                                ? "pass"
                                : token.state === "Used"
                                  ? "info"
                                  : "neutral"
                            }
                          >
                            {token.state}
                          </Pill>
                        </td>
                        <td>
                          {token.state === "Active" && (
                            <button
                              className="btn"
                              type="button"
                              onClick={() => void revokeToken(token)}
                            >
                              {t("computersPage.agent.revoke")}
                            </button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      )}

      {tab === "inventory" && (
        <>
          <div className="card">
            {!rows ? null : rows.length === 0 ? (
              <div className="empty">{t("computersPage.empty")}</div>
            ) : (
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>{t("computersPage.serial")}</th>
                      <th>{t("computersPage.os")}</th>
                      <th>{t("computersPage.owner")}</th>
                      <th>{t("computersPage.encryption")}</th>
                      <th>{t("computersPage.screen")}</th>
                      <th>{t("computersPage.antivirus")}</th>
                      <th>{t("computersPage.passwords")}</th>
                      <th>{t("computersPage.updates")}</th>
                      <th>{t("computersPage.checkIn")}</th>
                      <th>{t("computersPage.source")}</th>
                      <th>{t("computersPage.screenshot")}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {rows.map((row) => (
                      <tr key={row.id}>
                        <td>
                          <b>{row.serial}</b>
                        </td>
                        <td>{row.operatingSystem}</td>
                        <td>
                          {row.ownerName}
                          <div className="id">{row.ownerEmail}</div>
                        </td>
                        <td>
                          {flag(row.encrypted, t("computersPage.on"), t("computersPage.off"))}
                        </td>
                        <td>
                          {flag(row.screenLock, t("computersPage.on"), t("computersPage.off"))}
                        </td>
                        <td>
                          {flag(row.antivirus, t("computersPage.on"), t("computersPage.off"))}
                        </td>
                        <td>
                          {flag(row.passwordManager, t("computersPage.on"), t("computersPage.off"))}
                        </td>
                        <td>
                          {flag(row.autoUpdate, t("computersPage.on"), t("computersPage.off"))}
                        </td>
                        <td>{formatWhen(row.lastCheckIn)}</td>
                        <td>{t(`computersPage.sources.${row.source}`)}</td>
                        <td>
                          {row.screenshotName && <div className="id">{row.screenshotName}</div>}
                          {workspace?.canWrite && (
                            <label className="linkish">
                              {t("computersPage.upload")}
                              <input
                                type="file"
                                accept="image/*,.png,.jpg,.pdf"
                                hidden
                                onChange={(event) => {
                                  const file = event.target.files?.[0];
                                  if (file) void upload(row.id, file);
                                }}
                              />
                            </label>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
          {workspace?.canWrite && (
            <form className="card" style={{ marginTop: 14 }} onSubmit={(event) => void save(event)}>
              <h3>{t("computersPage.add")}</h3>
              <div className="split-fields">
                <input
                  aria-label={t("computersPage.serial")}
                  placeholder={t("computersPage.serial")}
                  value={form.serial}
                  onChange={(event) => setForm({ ...form, serial: event.target.value })}
                  required
                />
                <input
                  aria-label={t("computersPage.os")}
                  placeholder={t("computersPage.os")}
                  value={form.operatingSystem}
                  onChange={(event) => setForm({ ...form, operatingSystem: event.target.value })}
                  required
                />
                <input
                  aria-label={t("computersPage.owner")}
                  placeholder={t("computersPage.owner")}
                  value={form.ownerName}
                  onChange={(event) => setForm({ ...form, ownerName: event.target.value })}
                  required
                />
                <input
                  aria-label={t("computersPage.email")}
                  placeholder={t("computersPage.email")}
                  value={form.ownerEmail}
                  onChange={(event) => setForm({ ...form, ownerEmail: event.target.value })}
                  required
                />
                <select
                  aria-label={t("computersPage.source")}
                  value={form.source}
                  onChange={(event) => setForm({ ...form, source: event.target.value })}
                >
                  <option value="Manual">{t("computersPage.sources.Manual")}</option>
                  <option value="Agent">{t("computersPage.sources.Agent")}</option>
                  <option value="Mdm">{t("computersPage.sources.Mdm")}</option>
                </select>
              </div>
              {(
                [
                  ["encrypted", "encryption"],
                  ["screenLock", "screen"],
                  ["antivirus", "antivirus"],
                  ["passwordManager", "passwords"],
                  ["autoUpdate", "updates"],
                ] as const
              ).map(([key, label]) => (
                <label key={key} className="check-row">
                  <input
                    type="checkbox"
                    checked={form[key]}
                    onChange={(event) => setForm({ ...form, [key]: event.target.checked })}
                  />
                  {t(`computersPage.${label}`)}
                </label>
              ))}
              <button className="btn primary" type="submit">
                {t("computersPage.save")}
              </button>
            </form>
          )}
        </>
      )}
    </>
  );
}
