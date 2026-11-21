import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useOutletContext } from "react-router-dom";
import {
  ApiError,
  api,
  type ConnectorActionResponse,
  type ConnectorCatalog,
  type ConnectorDetail,
  type ConnectorSummary,
  type ConnectResult,
  type Integration,
  type Job,
  type Resource,
  type Workspace,
} from "../../shared/api/client";
import { Pager } from "../../shared/ui/Pager";
import { formatWhen, Pill, safeHref } from "../../shared/ui/ui";

const providers = ["aws", "azure", "gcp", "google", "microsoft", "github"] as const;
const providerNames: Record<string, string> = {
  aws: "Amazon Web Services",
  azure: "Microsoft Azure",
  gcp: "Google Cloud",
  google: "Google Workspace",
  microsoft: "Microsoft 365",
  github: "GitHub",
};

type ShellContext = { workspace: Workspace | null; tenantId: string | null; jobs: Job[] };
type StatusFilter = "all" | "connected" | "available";

export function IntegrationsPage() {
  const { t } = useTranslation();
  const { workspace, tenantId, jobs } = useOutletContext<ShellContext>();
  const [rows, setRows] = useState<Integration[]>([]);
  const [resources, setResources] = useState<Resource[]>([]);
  const [catalog, setCatalog] = useState<ConnectorCatalog | null>(null);
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [detail, setDetail] = useState<ConnectorDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [formValues, setFormValues] = useState<Record<string, string>>({});
  const [actionBusy, setActionBusy] = useState<string | null>(null);
  const [drawerNotice, setDrawerNotice] = useState<string | null>(null);
  const [drawerError, setDrawerError] = useState<string | null>(null);
  const [category, setCategory] = useState<string>("");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");
  const [connectorQuery, setConnectorQuery] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [roleArn, setRoleArn] = useState("");
  const [installationId, setInstallationId] = useState("");
  const [azure, setAzure] = useState({
    tenantId: "",
    clientId: "",
    clientSecret: "",
    subscriptionId: "",
  });
  const [gcp, setGcp] = useState({ projectId: "", clientId: "", clientSecret: "" });
  const [connect, setConnect] = useState<ConnectResult | null>(null);
  const [resourceQuery, setResourceQuery] = useState("");
  const [resourcePage, setResourcePage] = useState(1);
  const handledCallbackRef = useRef<string | null>(null);

  const loadCatalog = useCallback(() => {
    if (!tenantId) return;
    void api<ConnectorCatalog>("/api/v1/integrations/catalog", { tenantId })
      .then((result) => {
        if (result && Array.isArray(result.connectors) && Array.isArray(result.categories)) {
          setCatalog(result);
        } else {
          setCatalog(null);
        }
      })
      .catch(() => setCatalog(null));
  }, [tenantId]);

  const load = useCallback(() => {
    if (!tenantId) return;
    void api<Integration[]>("/api/v1/integrations", { tenantId })
      .then((result) => setRows(Array.isArray(result) ? result : []))
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
    void api<Resource[]>("/api/v1/resources", { tenantId })
      .then((result) => setResources(Array.isArray(result) ? result : []))
      .catch(() => setResources([]));
    loadCatalog();
  }, [tenantId, loadCatalog]);

  useEffect(() => {
    load();
    const params = new URLSearchParams(window.location.search);
    const provider = sessionStorage.getItem("d2d-connect-provider");
    const state = params.get("state");
    if (!tenantId || !provider || !state) return;
    const callbackKey = `${tenantId}:${provider}:${state}`;
    if (handledCallbackRef.current === callbackKey) return;
    handledCallbackRef.current = callbackKey;
    sessionStorage.removeItem("d2d-connect-provider");
    window.history.replaceState({}, "", "/integrations");
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
      });
  }, [tenantId, load]);

  const syncFormFromDetail = (next: ConnectorDetail) => {
    const values: Record<string, string> = {};
    for (const field of next.fields) {
      const saved = next.config.find((item) => item.key === field.key);
      values[field.key] = !field.secret && saved?.value ? saved.value : "";
    }
    setFormValues(values);
  };

  async function openConnector(key: string) {
    if (!tenantId) return;
    setSelectedKey(key);
    setDrawerNotice(null);
    setDrawerError(null);
    setDetailLoading(true);
    try {
      const next = await api<ConnectorDetail>(
        `/api/v1/integrations/catalog/${encodeURIComponent(key)}`,
        { tenantId },
      );
      setDetail(next);
      syncFormFromDetail(next);
    } catch (caught: unknown) {
      setDrawerError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setDetailLoading(false);
    }
  }

  function closeDrawer() {
    setSelectedKey(null);
    setDetail(null);
    setDrawerNotice(null);
    setDrawerError(null);
  }

  async function connectCatalog(key: string) {
    if (!tenantId) return;
    setActionBusy("connect");
    setDrawerError(null);
    setDrawerNotice(null);
    try {
      const result = await api<ConnectorActionResponse>(
        `/api/v1/integrations/catalog/${encodeURIComponent(key)}/connect`,
        {
          method: "POST",
          body: { config: formValues },
          tenantId,
        },
      );
      setDetail(result.detail);
      syncFormFromDetail(result.detail);
      setDrawerNotice(
        t("integrations.connectedNotice", {
          name: result.detail.connector.name,
          mode: t(`integrations.modes.${result.detail.connector.mode ?? "mock"}`, {
            defaultValue: result.detail.connector.mode ?? "mock",
          }),
          count: result.detail.connector.resourceCount,
        }),
      );
      load();
    } catch (caught: unknown) {
      setDrawerError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setActionBusy(null);
    }
  }

  async function testCatalog(key: string) {
    if (!tenantId) return;
    setActionBusy("test");
    setDrawerError(null);
    setDrawerNotice(null);
    try {
      const result = await api<ConnectorActionResponse>(
        `/api/v1/integrations/catalog/${encodeURIComponent(key)}/test`,
        {
          method: "POST",
          body: { config: formValues },
          tenantId,
        },
      );
      setDetail(result.detail);
      if (result.run?.status === "failed") {
        setDrawerError(result.run.error ?? "connection_test_failed");
      } else {
        setDrawerNotice(
          t("integrations.testedNotice", {
            mode: t(`integrations.modes.${result.run?.mode ?? "mock"}`, {
              defaultValue: result.run?.mode ?? "mock",
            }),
          }),
        );
      }
      loadCatalog();
    } catch (caught: unknown) {
      setDrawerError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setActionBusy(null);
    }
  }

  async function syncCatalog(key: string) {
    if (!tenantId) return;
    setActionBusy(`sync:${key}`);
    setDrawerError(null);
    setDrawerNotice(null);
    setError(null);
    try {
      const result = await api<ConnectorActionResponse>(
        `/api/v1/integrations/catalog/${encodeURIComponent(key)}/sync`,
        {
          method: "POST",
          body: {},
          tenantId,
        },
      );
      if (selectedKey === key) {
        setDetail(result.detail);
        setDrawerNotice(
          t("integrations.syncedNotice", {
            name: result.detail.connector.name,
            count: result.detail.connector.resourceCount,
          }),
        );
      }
      load();
    } catch (caught: unknown) {
      const code = caught instanceof ApiError ? caught.code : "load";
      if (selectedKey === key) setDrawerError(code);
      else setError(code);
    } finally {
      setActionBusy(null);
    }
  }

  async function disconnectCatalog(key: string) {
    if (!tenantId) return;
    setActionBusy("disconnect");
    setDrawerError(null);
    setDrawerNotice(null);
    try {
      const updated = await api<ConnectorDetail>(
        `/api/v1/integrations/catalog/${encodeURIComponent(key)}/disconnect`,
        {
          method: "POST",
          body: {},
          tenantId,
        },
      );
      setDetail(updated);
      syncFormFromDetail(updated);
      setDrawerNotice(t("integrations.disconnectedNotice", { name: updated.connector.name }));
      load();
    } catch (caught: unknown) {
      setDrawerError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setActionBusy(null);
    }
  }

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
      const safeAuth = safeHref(result.authorizationUrl);
      if (safeAuth) {
        sessionStorage.setItem("d2d-connect-provider", provider);
        window.location.assign(safeAuth);
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

  const catalogNames = useMemo(() => {
    const map: Record<string, string> = { ...providerNames };
    for (const item of catalog?.connectors ?? []) {
      map[item.key] = item.name;
    }
    return map;
  }, [catalog]);

  const categoryCounts = useMemo(() => {
    const counts: Record<string, number> = {};
    for (const item of catalog?.connectors ?? []) {
      counts[item.category] = (counts[item.category] ?? 0) + 1;
    }
    return counts;
  }, [catalog]);

  const filteredConnectors = useMemo(() => {
    if (!catalog) return [];
    const term = connectorQuery.trim().toLowerCase();
    return catalog.connectors.filter((item) => {
      if (category && item.category !== category) return false;
      if (statusFilter === "connected" && item.status !== "connected") return false;
      if (statusFilter === "available" && item.status === "connected") return false;
      if (!term) return true;
      return (
        item.name.toLowerCase().includes(term) ||
        item.key.toLowerCase().includes(term) ||
        item.category.toLowerCase().includes(term) ||
        item.description.toLowerCase().includes(term) ||
        item.resources.some((resource) => resource.toLowerCase().includes(term)) ||
        item.tests.some((test) => test.toLowerCase().includes(term)) ||
        item.controls.some((control) => control.toLowerCase().includes(term))
      );
    });
  }, [catalog, category, statusFilter, connectorQuery]);

  const resourceTerm = resourceQuery.trim().toLowerCase();
  const filteredResources = resources
    .filter((resource) => {
      if (!resourceTerm) return true;
      return `${resource.provider} ${catalogNames[resource.provider] ?? ""} ${resource.kind} ${resource.name} ${resource.detail}`
        .toLowerCase()
        .includes(resourceTerm);
    })
    .sort(
      (left, right) =>
        Number(left.kind === "user") - Number(right.kind === "user") ||
        left.provider.localeCompare(right.provider) ||
        left.name.localeCompare(right.name),
    );
  const shownResources = filteredResources.slice((resourcePage - 1) * 20, resourcePage * 20);

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

      {catalog && (
        <section
          className="card"
          style={{ marginBottom: 16 }}
          aria-label={t("integrations.marketplaceTitle")}
        >
          <div className="head" style={{ marginBottom: 12 }}>
            <div>
              <h2 style={{ margin: 0, fontSize: 16 }}>{t("integrations.marketplaceTitle")}</h2>
              <div className="sub">{t("integrations.marketplaceSub")}</div>
            </div>
            <div className="kv" style={{ minWidth: 260 }}>
              <div>
                <small>{t("integrations.statConnected")}</small>
                <b>
                  {catalog.connected} / {catalog.total}
                </b>
              </div>
              <div>
                <small>{t("integrations.statCategories")}</small>
                <b>{catalog.categories.length}</b>
              </div>
              <div>
                <small>{t("integrations.statResources")}</small>
                <b>{resources.length}</b>
              </div>
            </div>
          </div>

          <div className="connector-toolbar">
            <input
              className="search-input"
              aria-label={t("integrations.searchConnectors")}
              placeholder={t("integrations.searchPlaceholder")}
              value={connectorQuery}
              onChange={(event) => setConnectorQuery(event.target.value)}
            />
            <div className="toggle" role="group" aria-label={t("integrations.marketplaceTitle")}>
              {(["all", "connected", "available"] as const).map((filter) => (
                <button
                  key={filter}
                  type="button"
                  className={statusFilter === filter ? "on" : ""}
                  onClick={() => setStatusFilter(filter)}
                >
                  {t(
                    filter === "all"
                      ? "integrations.filterAll"
                      : filter === "connected"
                        ? "integrations.filterConnected"
                        : "integrations.filterAvailable",
                  )}
                </button>
              ))}
            </div>
          </div>

          <div
            className="chips"
            style={{ marginBottom: 14 }}
            role="tablist"
            aria-label={t("integrations.statCategories")}
          >
            <button
              type="button"
              role="tab"
              aria-selected={category === ""}
              className={`chip ${category === "" ? "on" : ""}`}
              onClick={() => setCategory("")}
            >
              {t("integrations.allCategories")} ({catalog.total})
            </button>
            {catalog.categories.map((cat) => (
              <button
                key={cat}
                type="button"
                role="tab"
                aria-selected={category === cat}
                className={`chip ${category === cat ? "on" : ""}`}
                onClick={() => setCategory(cat)}
              >
                {t(`integrations.categories.${cat}`, { defaultValue: cat })} (
                {categoryCounts[cat] ?? 0})
              </button>
            ))}
          </div>

          {filteredConnectors.length === 0 ? (
            <div className="empty">{t("integrations.noConnectors")}</div>
          ) : (
            <div className="cardgrid">
              {filteredConnectors.map((item) => (
                <ConnectorCard
                  key={item.key}
                  item={item}
                  canWrite={Boolean(workspace?.canWrite)}
                  syncing={actionBusy === `sync:${item.key}`}
                  onOpen={() => void openConnector(item.key)}
                  onSync={() => void syncCatalog(item.key)}
                />
              ))}
            </div>
          )}
        </section>
      )}

      <div style={{ marginBottom: 10 }}>
        <h2 style={{ margin: 0, fontSize: 16 }}>{t("integrations.coreTitle")}</h2>
        <div className="sub">{t("integrations.coreSub")}</div>
      </div>

      <div className="grid" style={{ gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))" }}>
        {providers.map((provider) => {
          const row = rows.find((item) => item.provider === provider);
          return (
            <div className="card" key={provider}>
              <h3>
                {providerLabel(provider, catalogNames)}
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
              {workspace?.canWrite && provider === "azure" && !row?.example && (
                <form
                  className="stack"
                  onSubmit={(event) => {
                    event.preventDefault();
                    void send("azure", azure);
                  }}
                >
                  <input
                    aria-label={t("integrations.tenantId")}
                    placeholder={t("integrations.tenantId")}
                    value={azure.tenantId}
                    onChange={(event) => setAzure({ ...azure, tenantId: event.target.value })}
                  />
                  <input
                    aria-label={t("integrations.clientId")}
                    placeholder={t("integrations.clientId")}
                    value={azure.clientId}
                    onChange={(event) => setAzure({ ...azure, clientId: event.target.value })}
                  />
                  <input
                    aria-label={t("integrations.clientSecret")}
                    placeholder={t("integrations.clientSecret")}
                    type="password"
                    value={azure.clientSecret}
                    onChange={(event) => setAzure({ ...azure, clientSecret: event.target.value })}
                  />
                  <input
                    aria-label={t("integrations.subscriptionId")}
                    placeholder={t("integrations.subscriptionId")}
                    value={azure.subscriptionId}
                    onChange={(event) => setAzure({ ...azure, subscriptionId: event.target.value })}
                  />
                  <button className="btn" type="submit" disabled={busy}>
                    {t("integrations.connect")}
                  </button>
                </form>
              )}
              {workspace?.canWrite && provider === "gcp" && !row?.example && (
                <form
                  className="stack"
                  onSubmit={(event) => {
                    event.preventDefault();
                    void send("gcp", gcp);
                  }}
                >
                  <input
                    aria-label={t("integrations.projectId")}
                    placeholder={t("integrations.projectId")}
                    value={gcp.projectId}
                    onChange={(event) => setGcp({ ...gcp, projectId: event.target.value })}
                  />
                  <input
                    aria-label={t("integrations.clientId")}
                    placeholder={t("integrations.serviceAccount")}
                    value={gcp.clientId}
                    onChange={(event) => setGcp({ ...gcp, clientId: event.target.value })}
                  />
                  <textarea
                    aria-label={t("integrations.privateKey")}
                    placeholder={t("integrations.privateKey")}
                    value={gcp.clientSecret}
                    onChange={(event) => setGcp({ ...gcp, clientSecret: event.target.value })}
                  />
                  <button className="btn" type="submit" disabled={busy}>
                    {t("integrations.connect")}
                  </button>
                </form>
              )}
              {workspace?.canWrite && provider === "github" && (
                <form
                  className="stack"
                  onSubmit={(event) => {
                    event.preventDefault();
                    void send("github", {
                      installationId,
                      ...(row?.externalId ? { state: row.externalId } : {}),
                    });
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

      {resources.length > 0 && (
        <div className="card" style={{ marginTop: 14 }}>
          <h3>{t("integrations.resources")}</h3>
          <label className="field">
            <span>{t("integrations.search")}</span>
            <input
              value={resourceQuery}
              onChange={(event) => {
                setResourceQuery(event.target.value);
                setResourcePage(1);
              }}
            />
          </label>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>{t("integrations.provider")}</th>
                  <th>{t("integrations.kind")}</th>
                  <th>{t("integrations.resource")}</th>
                  <th>{t("integrations.detail")}</th>
                </tr>
              </thead>
              <tbody>
                {shownResources.map((resource, index) => (
                  <tr key={`${resource.provider}-${resource.kind}-${resource.name}-${index}`}>
                    <td>{providerLabel(resource.provider, catalogNames)}</td>
                    <td>{resource.kind}</td>
                    <td>{resource.name}</td>
                    <td className="m">{resource.detail}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <Pager
              page={resourcePage}
              pageSize={20}
              total={filteredResources.length}
              onPage={setResourcePage}
            />
          </div>
        </div>
      )}

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

      {selectedKey && (
        <aside className="drawer dock" aria-label={detail?.connector.name ?? selectedKey}>
          <div className="drawer-head">
            <div>
              <h2 style={{ margin: 0, fontSize: 17 }}>{detail?.connector.name ?? selectedKey}</h2>
              {detail && (
                <div className="chips" style={{ marginTop: 6 }}>
                  <span className="tag">
                    {t(`integrations.categories.${detail.connector.category}`, {
                      defaultValue: detail.connector.category,
                    })}
                  </span>
                  <span className="tag">
                    {t(`integrations.authTypes.${detail.connector.authType}`, {
                      defaultValue: detail.connector.authType,
                    })}
                  </span>
                  <Pill status={statusTone(detail.connector.status)}>
                    {t(`integrations.status.${detail.connector.status}`, {
                      defaultValue: detail.connector.status,
                    })}
                  </Pill>
                  {detail.connector.mode && (
                    <span className="pill info nodot">
                      {t(`integrations.modes.${detail.connector.mode}`, {
                        defaultValue: detail.connector.mode,
                      })}
                    </span>
                  )}
                </div>
              )}
            </div>
            <button
              className="btn"
              type="button"
              aria-label={t("integrations.closeDrawer")}
              onClick={closeDrawer}
            >
              {t("controls.close")}
            </button>
          </div>

          {detailLoading && (
            <div className="busy">
              <i />
            </div>
          )}
          {drawerError && (
            <div className="error" role="alert">
              {t(`integrations.errors.${drawerError}`, { defaultValue: t("errors.load") })}
            </div>
          )}
          {drawerNotice && (
            <div className="notice" role="status">
              {drawerNotice}
            </div>
          )}

          {detail && (
            <>
              <p className="m" style={{ margin: 0 }}>
                {detail.connector.description}
              </p>
              {safeHref(detail.connector.docsUrl) && (
                <div>
                  <a
                    href={safeHref(detail.connector.docsUrl) ?? undefined}
                    target="_blank"
                    rel="noopener noreferrer"
                  >
                    {t("integrations.docs")}
                  </a>
                </div>
              )}

              <div className="kv">
                <div>
                  <small>{t("integrations.statResources")}</small>
                  <b>{detail.connector.resourceCount}</b>
                </div>
                <div>
                  <small>{t("integrations.modeLabel")}</small>
                  <b>
                    {detail.connector.mode
                      ? t(`integrations.modes.${detail.connector.mode}`, {
                          defaultValue: detail.connector.mode,
                        })
                      : t("integrations.empty")}
                  </b>
                </div>
                <div>
                  <small>{t("integrations.lastSynced")}</small>
                  <b style={{ fontSize: 11.5 }}>
                    {detail.connector.lastSyncedAt
                      ? formatWhen(detail.connector.lastSyncedAt)
                      : t("integrations.neverSynced")}
                  </b>
                </div>
              </div>

              {detail.connector.lastError && (
                <div className="error" role="alert">
                  {detail.connector.lastError}
                </div>
              )}

              {detail.connector.builtin ? (
                <div className="drawer-section">
                  <div className="m">
                    {t("integrations.builtinNote", {
                      module: detail.connector.managedAt ?? "/integrations",
                    })}
                  </div>
                  {detail.connector.managedAt && detail.connector.managedAt !== "/integrations" && (
                    <div>
                      <Link className="btn" to={detail.connector.managedAt}>
                        {t("integrations.manageInModule")}
                      </Link>
                    </div>
                  )}
                  {detail.envKeys.length > 0 && (
                    <div>
                      <div className="m" style={{ marginBottom: 4 }}>
                        {t("integrations.envVars")}
                      </div>
                      <div className="chips">
                        {detail.envKeys.map((env) => (
                          <code className="tag" key={env}>
                            {env}
                          </code>
                        ))}
                      </div>
                    </div>
                  )}
                </div>
              ) : (
                <div className="drawer-section">
                  <b style={{ fontSize: 13 }}>{t("integrations.fieldsTitle")}</b>
                  {!workspace?.canWrite ? (
                    <div className="m">{t("integrations.readOnlyHint")}</div>
                  ) : (
                    <form
                      className="stack"
                      onSubmit={(event) => {
                        event.preventDefault();
                        void connectCatalog(detail.connector.key);
                      }}
                    >
                      {detail.fields.map((field) => {
                        const saved = detail.config.find((item) => item.key === field.key);
                        const inputId = `connector-field-${detail.connector.key}-${field.key}`;
                        return (
                          <div className="field" style={{ marginTop: 0 }} key={field.key}>
                            <div className="label-row">
                              <label htmlFor={inputId}>
                                {field.label}
                                {!field.required && ` (${t("integrations.optional")})`}
                              </label>
                              {saved?.configured && (
                                <span className="pill ok nodot">
                                  {t("integrations.configuredBadge")}
                                </span>
                              )}
                            </div>
                            <input
                              id={inputId}
                              type={field.secret ? "password" : "text"}
                              autoComplete="off"
                              placeholder={
                                field.secret && saved?.configured
                                  ? t("integrations.secretStored")
                                  : field.placeholder
                              }
                              value={formValues[field.key] ?? ""}
                              onChange={(event) =>
                                setFormValues((current) => ({
                                  ...current,
                                  [field.key]: event.target.value,
                                }))
                              }
                            />
                          </div>
                        );
                      })}
                      <div className="row" style={{ flexWrap: "wrap" }}>
                        <button
                          className="btn primary"
                          type="submit"
                          disabled={actionBusy !== null}
                        >
                          {detail.connector.status === "connected"
                            ? t("integrations.saveAndConnect")
                            : t("integrations.connect")}
                        </button>
                        <button
                          className="btn"
                          type="button"
                          disabled={actionBusy !== null}
                          onClick={() => void testCatalog(detail.connector.key)}
                        >
                          {t("integrations.testConnection")}
                        </button>
                        {detail.connector.status === "connected" && (
                          <>
                            <button
                              className="btn"
                              type="button"
                              disabled={actionBusy !== null}
                              onClick={() => void syncCatalog(detail.connector.key)}
                            >
                              {t("integrations.syncNow")}
                            </button>
                            <button
                              className="btn danger"
                              type="button"
                              disabled={actionBusy !== null}
                              onClick={() => void disconnectCatalog(detail.connector.key)}
                            >
                              {t("integrations.disconnect")}
                            </button>
                          </>
                        )}
                      </div>
                    </form>
                  )}
                </div>
              )}

              <div className="drawer-section">
                <b style={{ fontSize: 13 }}>{t("integrations.checksTitle")}</b>
                {detail.checks.length === 0 ? (
                  <div className="m">{t("integrations.checksEmpty")}</div>
                ) : (
                  <div className="stack">
                    {detail.checks.map((check) => (
                      <div key={check.key} className="card" style={{ padding: 10 }}>
                        <div className="row" style={{ justifyContent: "space-between" }}>
                          <span className="t">{check.name}</span>
                          <Pill status={check.outcome}>
                            {t(`status.${check.outcome}`, { defaultValue: check.outcome })}
                          </Pill>
                        </div>
                        <div className="m" style={{ marginTop: 4 }}>
                          <span className="id">{check.controlCode}</span>
                          {check.detail ? ` · ${check.detail}` : ""}
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>

              <div className="drawer-section">
                <b style={{ fontSize: 13 }}>{t("integrations.historyTitle")}</b>
                {detail.history.length === 0 ? (
                  <div className="m">{t("integrations.historyEmpty")}</div>
                ) : (
                  <div className="stack">
                    {detail.history.slice(0, 8).map((run) => (
                      <div key={run.id} className="row" style={{ justifyContent: "space-between" }}>
                        <div>
                          <div className="t">
                            {run.kind} · {run.mode} · {run.trigger}
                          </div>
                          <div className="m">
                            {formatWhen(run.startedAt)} ·{" "}
                            {t("integrations.resourcesCount", { count: run.resourceCount })}
                            {run.error ? ` · ${run.error}` : ""}
                          </div>
                        </div>
                        <Pill status={run.status === "ok" ? "pass" : "fail"}>{run.status}</Pill>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </>
          )}
        </aside>
      )}
    </>
  );
}

function ConnectorCard({
  item,
  canWrite,
  syncing,
  onOpen,
  onSync,
}: {
  item: ConnectorSummary;
  canWrite: boolean;
  syncing: boolean;
  onOpen: () => void;
  onSync: () => void;
}) {
  const { t } = useTranslation();
  const isConnected = item.status === "connected";
  return (
    <div className="card connector-card">
      <div className="row" style={{ justifyContent: "space-between", alignItems: "flex-start" }}>
        <h3 style={{ margin: 0 }}>{item.name}</h3>
        <Pill status={statusTone(item.status)}>
          {t(`integrations.status.${item.status}`, { defaultValue: item.status })}
        </Pill>
      </div>
      <div className="chips">
        <span className="tag">
          {t(`integrations.categories.${item.category}`, { defaultValue: item.category })}
        </span>
        <span className="tag">
          {t(`integrations.authTypes.${item.authType}`, { defaultValue: item.authType })}
        </span>
        {item.mode && (
          <span className="pill info nodot">
            {t(`integrations.modes.${item.mode}`, { defaultValue: item.mode })}
          </span>
        )}
      </div>
      <p className="m" style={{ margin: 0 }}>
        {item.description}
      </p>
      <div className="m">
        {isConnected && item.resourceCount > 0 && (
          <span>{t("integrations.resourcesCount", { count: item.resourceCount })} · </span>
        )}
        {item.controls.join(", ")}
      </div>
      <div className="connector-foot">
        <div className="row" style={{ gap: 6 }}>
          {item.builtin ? (
            <>
              <button className="btn" type="button" onClick={onOpen}>
                {t("integrations.details")}
              </button>
              {item.managedAt && item.managedAt !== "/integrations" && (
                <Link className="btn" to={item.managedAt}>
                  {t("integrations.manageInModule")}
                </Link>
              )}
            </>
          ) : (
            <>
              <button
                className={isConnected ? "btn" : "btn primary"}
                type="button"
                onClick={onOpen}
              >
                {isConnected ? t("integrations.configure") : t("integrations.connect")}
              </button>
              {isConnected && canWrite && (
                <button className="btn" type="button" disabled={syncing} onClick={onSync}>
                  {t("integrations.syncNow")}
                </button>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
}

function statusTone(status: string) {
  if (status === "connected") return "pass";
  if (status === "error") return "fail";
  if (status === "pending") return "warn";
  return "notRun";
}

function providerLabel(provider: string, names: Record<string, string>) {
  return names[provider] ?? provider;
}

function redirectUri() {
  return `${window.location.origin}/integrations`;
}
