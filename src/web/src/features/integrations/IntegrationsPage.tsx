import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import {
  ApiError,
  api,
  type ConnectResult,
  type Integration,
  type Job,
  type Workspace,
} from "../../shared/api/client";
import { Pill } from "../../shared/ui/ui";

const providers = ["aws", "google", "microsoft", "github"] as const;

type ShellContext = { workspace: Workspace | null; tenantId: string | null; jobs: Job[] };

export function IntegrationsPage() {
  const { t } = useTranslation();
  const { workspace, tenantId, jobs } = useOutletContext<ShellContext>();
  const [rows, setRows] = useState<Integration[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [roleArn, setRoleArn] = useState("");
  const [installationId, setInstallationId] = useState("");
  const [connect, setConnect] = useState<ConnectResult | null>(null);

  const load = () => {
    if (!tenantId) return;
    void api<Integration[]>("/api/v1/integrations", { tenantId })
      .then(setRows)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  };

  useEffect(() => {
    load();
    const params = new URLSearchParams(window.location.search);
    const provider = sessionStorage.getItem("d2d-connect-provider");
    const state = params.get("state");
    if (!tenantId || !provider || !state) return;
    const code = params.get("code") ?? params.get("tenant");
    const installation = params.get("installation_id");
    const body = installation
      ? { installationId: installation, state, redirectUri: redirectUri() }
      : { code, state, redirectUri: redirectUri() };
    setBusy(true);
    void api<ConnectResult>(`/api/v1/integrations/${provider}/connect`, {
      method: "POST",
      body,
      tenantId,
    })
      .then((result) => {
        setConnect(result);
        load();
      })
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"))
      .finally(() => {
        setBusy(false);
        sessionStorage.removeItem("d2d-connect-provider");
        window.history.replaceState({}, "", "/integrations");
      });
    // The authorization redirect is handled once per tenant load.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  const collection = jobs.find((job) => job.kind === "collection");
  const running =
    collection?.status === "Running" ||
    collection?.status === "Queued" ||
    collection?.status === "running" ||
    collection?.status === "queued";

  async function send(provider: string, body: Record<string, string>) {
    if (!tenantId) return;
    setBusy(true);
    setError(null);
    try {
      const result = await api<ConnectResult>(`/api/v1/integrations/${provider}/connect`, {
        method: "POST",
        body: { ...body, redirectUri: redirectUri() },
        tenantId,
      });
      setConnect(result);
      if (result.authorizationUrl) {
        sessionStorage.setItem("d2d-connect-provider", provider);
        window.location.assign(result.authorizationUrl);
        return;
      }
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  async function loadExample() {
    if (!tenantId) return;
    setBusy(true);
    setError(null);
    const fixture = (workspace?.name ?? "").toLowerCase().includes("northwind")
      ? "northwind"
      : "acme";
    try {
      await api<Integration[]>("/api/v1/integrations/example", {
        method: "POST",
        body: { fixture },
        tenantId,
      });
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  async function collect() {
    if (!tenantId) return;
    setBusy(true);
    setError(null);
    try {
      await api<string>("/api/v1/integrations/collect", { method: "POST", tenantId });
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("integrations.title")}</h1>
          <div className="sub">{t("integrations.sub")}</div>
        </div>
        <div className="act">
          {workspace?.exampleData && workspace.canWrite && (
            <button
              className="btn"
              type="button"
              disabled={busy}
              onClick={() => void loadExample()}
            >
              {t("integrations.useExample")}
            </button>
          )}
          {workspace?.canWrite && (
            <button
              className="btn primary"
              type="button"
              disabled={busy || running}
              onClick={() => void collect()}
            >
              {t("integrations.collect")}
            </button>
          )}
        </div>
      </div>
      {error && (
        <div className="error" role="alert">
          {t(`integrations.errors.${error}`, { defaultValue: t("errors.load") })}
        </div>
      )}
      {collection && (
        <div className="card" style={{ marginBottom: 14 }}>
          <b>{t("integrations.progress")}</b>
          <div className="m">{collection.currentStep}</div>
          <div
            className="bar"
            style={{ marginTop: 8 }}
            role="progressbar"
            aria-valuenow={collection.percent ?? 0}
            aria-valuemin={0}
            aria-valuemax={100}
            aria-label={collection.currentStep}
          >
            <i style={{ width: `${collection.percent ?? 0}%` }} />
          </div>
        </div>
      )}
      <div className="grid" style={{ gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))" }}>
        {providers.map((provider) => {
          const row = rows.find((item) => item.provider === provider);
          return (
            <div className="card" key={provider}>
              <h3>
                {providerLabel(provider)}
                {row?.example && (
                  <span className="pill info nodot">{t("integrations.example")}</span>
                )}
              </h3>
              <p className="m">{t(`integrations.providers.${provider}`)}</p>
              <div style={{ margin: "8px 0" }}>
                <Pill status={row?.status === "connected" ? "pass" : "notRun"}>
                  {row
                    ? t(`integrations.status.${row.status}`, { defaultValue: row.status })
                    : t("integrations.empty")}
                </Pill>
              </div>
              {row?.accountLabel && <div className="m">{row.accountLabel}</div>}
              {row?.setupHint && <div className="m">{row.setupHint}</div>}
              {workspace?.canWrite && provider === "aws" && (
                <form
                  className="stack"
                  onSubmit={(event) => {
                    event.preventDefault();
                    void send("aws", { roleArn });
                  }}
                >
                  <label className="field">
                    <span>{t("integrations.roleArn")}</span>
                    <input value={roleArn} onChange={(event) => setRoleArn(event.target.value)} />
                  </label>
                  <button className="btn" type="submit" disabled={busy}>
                    {t("integrations.connect")}
                  </button>
                </form>
              )}
              {workspace?.canWrite && (provider === "google" || provider === "microsoft") && (
                <button
                  className="btn"
                  type="button"
                  disabled={busy}
                  onClick={() => void send(provider, {})}
                >
                  {t("integrations.authorize")}
                </button>
              )}
              {workspace?.canWrite && provider === "github" && (
                <form
                  className="stack"
                  onSubmit={(event) => {
                    event.preventDefault();
                    void send("github", { installationId });
                  }}
                >
                  <label className="field">
                    <span>{t("integrations.installation")}</span>
                    <input
                      value={installationId}
                      onChange={(event) => setInstallationId(event.target.value)}
                    />
                  </label>
                  <button
                    className="btn"
                    type="button"
                    disabled={busy}
                    onClick={() => void send("github", {})}
                  >
                    {t("integrations.install")}
                  </button>
                  <button className="btn" type="submit" disabled={busy}>
                    {t("integrations.connect")}
                  </button>
                </form>
              )}
            </div>
          );
        })}
      </div>
      {connect?.trustPolicy && (
        <div className="card" style={{ marginTop: 14 }}>
          <h3>{t("integrations.trust")}</h3>
          {connect.integration.externalId && (
            <div className="m">
              {t("integrations.externalId")}: {connect.integration.externalId}
            </div>
          )}
          <pre>{connect.trustPolicy}</pre>
          <h3>{t("integrations.permissions")}</h3>
          <pre>{connect.permissionPolicy}</pre>
        </div>
      )}
    </>
  );
}

function providerLabel(provider: string) {
  if (provider === "aws") return "Amazon Web Services";
  if (provider === "google") return "Google Workspace";
  if (provider === "microsoft") return "Microsoft 365";
  return "GitHub";
}

function redirectUri() {
  return `${window.location.origin}/integrations`;
}
