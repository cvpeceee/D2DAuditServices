import { HubConnectionBuilder } from "@microsoft/signalr";
import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { NavLink, Outlet, useLocation, useNavigate } from "react-router-dom";
import { api, type Job, type Workspace } from "../../shared/api/client";
import { useAuth } from "../auth/AuthContext";
import { initials } from "../../shared/ui/ui";
import { useTheme, type ThemeChoice } from "../../shared/theme/ThemeProvider";

const links = [
  { to: "/", key: "dashboard", end: true },
  { to: "/frameworks", key: "frameworks", end: false },
  { to: "/controls", key: "controls", end: false },
  { to: "/tests", key: "tests", end: false },
  { to: "/evidence", key: "evidence", end: false },
] as const;

const governance = [
  { to: "/policies", key: "policies" },
  { to: "/risks", key: "risks" },
  { to: "/vendors", key: "vendors" },
  { to: "/people", key: "people" },
] as const;

export function AppShell() {
  const { t } = useTranslation();
  const { user, tenants, tenantId, setTenantId, signOut } = useAuth();
  const { choice, setChoice } = useTheme();
  const [workspace, setWorkspace] = useState<Workspace | null>(null);
  const [jobs, setJobs] = useState<Job[]>([]);
  const [menuOpen, setMenuOpen] = useState(false);
  const [palette, setPalette] = useState(false);
  const [query, setQuery] = useState("");
  const [active, setActive] = useState(0);
  const [tray, setTray] = useState(false);
  const [account, setAccount] = useState(false);
  const [assist, setAssist] = useState(false);
  const paletteSearch = useRef<HTMLInputElement>(null);
  const location = useLocation();
  const navigate = useNavigate();

  useEffect(() => {
    if (!tenantId) return;
    let cancelled = false;
    void api<Workspace>("/api/v1/workspace", { tenantId }).then((value) => {
      if (!cancelled) setWorkspace(value);
    });
    void api<Job[]>("/api/v1/jobs", { tenantId }).then((value) => {
      if (!cancelled) setJobs(value);
    });
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/jobs", { headers: { "X-D2D-Request": "1" } })
      .withAutomaticReconnect()
      .build();
    void connection
      .start()
      .then(() => connection.invoke("Subscribe", tenantId))
      .catch(() => undefined);
    connection.on("job", (job: Job) => {
      setJobs((current) => [job, ...current.filter((item) => item.id !== job.id)].slice(0, 20));
    });
    return () => {
      cancelled = true;
      void connection.stop();
    };
  }, [tenantId]);

  const actions = useMemo(() => {
    const items: { id: string; label: string; run: () => void }[] = [
      { id: "dashboard", label: t("nav.dashboard"), run: () => navigate("/") },
      { id: "frameworks", label: t("nav.frameworks"), run: () => navigate("/frameworks") },
      { id: "controls", label: t("nav.controls"), run: () => navigate("/controls") },
      { id: "evidence", label: t("nav.evidence"), run: () => navigate("/evidence") },
      { id: "integrations", label: t("nav.integrations"), run: () => navigate("/integrations") },
    ];
    if (workspace?.role === "Admin")
      items.push({ id: "activity", label: t("nav.activity"), run: () => navigate("/activity") });
    (["light", "dark", "system", "high-contrast"] as ThemeChoice[]).forEach((theme) => {
      items.push({
        id: `theme-${theme}`,
        label: `${t("theme.label")}: ${t(`theme.${theme === "high-contrast" ? "contrast" : theme}`)}`,
        run: () => setChoice(theme),
      });
    });
    return items;
  }, [navigate, setChoice, t, workspace?.role]);

  const filtered = actions.filter((action) =>
    action.label.toLowerCase().includes(query.trim().toLowerCase()),
  );

  useEffect(() => {
    if (palette) paletteSearch.current?.focus();
  }, [palette]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        setPalette(true);
        setQuery("");
        setActive(0);
      } else if (event.key === "Escape") {
        setPalette(false);
        setTray(false);
        setAccount(false);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  const tenant = tenants.find((item) => item.tenantId === tenantId) ?? tenants[0];
  const crumbKey = location.pathname.split("/")[1] || "dashboard";
  const crumb = t(`nav.${crumbKey}`, { defaultValue: t("nav.dashboard") });
  const running = jobs.some(
    (job) =>
      job.status === "Running" ||
      job.status === "Queued" ||
      job.status === "running" ||
      job.status === "queued",
  );

  return (
    <div className="app">
      <a className="skip" href="#content">
        {t("skip")}
      </a>
      {menuOpen && (
        <button
          className="overlay"
          aria-label={t("shell.close")}
          onClick={() => setMenuOpen(false)}
        />
      )}
      <aside className={`side ${menuOpen ? "open" : ""}`}>
        <div className="logo">
          <div className="mark">D2D</div>
          {t("productShort")}
        </div>
        {tenants.length > 1 ? (
          <label className="org-select">
            <span>
              <b>{tenant?.name}</b>
              <small>
                {tenant?.region} · {tenant?.exampleData ? t("shell.example") : tenant?.role}
              </small>
            </span>
            <select
              aria-label={tenant?.name}
              value={tenantId ?? ""}
              onChange={(event) => setTenantId(event.target.value)}
            >
              {tenants.map((item) => (
                <option key={item.tenantId} value={item.tenantId}>
                  {item.name}
                </option>
              ))}
            </select>
          </label>
        ) : (
          <div className="org">
            <div>
              <b>{tenant?.name}</b>
              <small>
                {tenant?.region} · {tenant?.exampleData ? t("shell.example") : tenant?.role}
              </small>
            </div>
          </div>
        )}
        {links.map((link) => (
          <NavLink
            key={link.to}
            to={link.to}
            end={link.end}
            className={({ isActive }) => `nav ${isActive ? "on" : ""}`}
            onClick={() => setMenuOpen(false)}
          >
            <i />
            {t(`nav.${link.key}`)}
            {link.key === "controls" && (workspace?.failingControls ?? 0) > 0 && (
              <span className="ct">{workspace?.failingControls}</span>
            )}
          </NavLink>
        ))}
        <div className="sec">{t("nav.governance")}</div>
        {governance.map((link) => (
          <NavLink
            key={link.to}
            to={link.to}
            className={({ isActive }) => `nav ${isActive ? "on" : ""}`}
            onClick={() => setMenuOpen(false)}
          >
            <i />
            {t(`nav.${link.key}`)}
          </NavLink>
        ))}
        <div className="sec">{t("nav.audit")}</div>
        <NavLink
          to="/auditor"
          className={({ isActive }) => `nav ${isActive ? "on" : ""}`}
          onClick={() => setMenuOpen(false)}
        >
          <i />
          {t("nav.auditor")}
        </NavLink>
        <NavLink
          to="/integrations"
          className={({ isActive }) => `nav ${isActive ? "on" : ""}`}
          onClick={() => setMenuOpen(false)}
        >
          <i />
          {t("nav.integrations")}
        </NavLink>
        {workspace?.role === "Admin" && (
          <NavLink
            to="/activity"
            className={({ isActive }) => `nav ${isActive ? "on" : ""}`}
            onClick={() => setMenuOpen(false)}
          >
            <i />
            {t("nav.activity")}
          </NavLink>
        )}
        <div className="agentcard">
          <b>{t("shell.deviceAgent")}</b>
          {t("shell.noDevices")}
          <div className="bar" style={{ marginTop: 8 }}>
            <i style={{ width: "0%" }} />
          </div>
        </div>
      </aside>
      <div className="main">
        <header className="top">
          <button
            className="btn icon-btn menu-btn"
            type="button"
            aria-label={t("shell.menu")}
            onClick={() => setMenuOpen(true)}
          >
            ☰
          </button>
          <div className="crumb">
            {tenant?.name} / <b>{crumb}</b>
          </div>
          <button
            className="search"
            type="button"
            onClick={() => {
              setPalette(true);
              setQuery("");
              setActive(0);
            }}
          >
            ⌕ {t("shell.search")}
            <span className="kbd">Ctrl K</span>
          </button>
          <div className="toggle" role="group" aria-label={t("theme.label")}>
            {(["light", "dark", "system", "high-contrast"] as ThemeChoice[]).map((theme) => (
              <button
                key={theme}
                type="button"
                className={choice === theme ? "on" : ""}
                onClick={() => setChoice(theme)}
              >
                {t(`theme.${theme === "high-contrast" ? "contrast" : theme}`)}
              </button>
            ))}
          </div>
          <button
            className="btn icon-btn"
            type="button"
            aria-label={t("shell.jobs")}
            onClick={() => setTray(true)}
          >
            {running ? "●" : "○"}
          </button>
          <button
            className="btn icon-btn"
            type="button"
            aria-label={t("shell.openAssistant")}
            onClick={() => setAssist(true)}
          >
            ✦
          </button>
          <button
            className="av"
            type="button"
            aria-label={t("shell.account")}
            onClick={() => setAccount((open) => !open)}
          >
            {initials(user?.displayName ?? "")}
          </button>
          {account && (
            <div className="menu-pop">
              <div style={{ padding: "8px 10px" }}>
                <b>{user?.displayName}</b>
                <div className="m">{user?.email}</div>
              </div>
              <button type="button" onClick={() => void signOut().then(() => navigate("/login"))}>
                {t("shell.signOut")}
              </button>
            </div>
          )}
        </header>
        <div className="body">
          <div className="content" id="content">
            {workspace?.role === "Auditor" && (
              <div className="banner">
                <div className="spark" aria-hidden="true">
                  ⛨
                </div>
                <div>
                  <b>{t("auditor.banner")}</b>
                  <div className="m">
                    {user?.displayName} · {workspace.name}
                  </div>
                </div>
              </div>
            )}
            <Outlet context={{ workspace, tenantId }} />
          </div>
          <aside className={`assist ${assist ? "open" : ""}`}>
            <div className="ah">
              <div className="spark" aria-hidden="true">
                ✦
              </div>
              <div>
                <b>{t("shell.assistant")}</b>
                <div className="m">{t("shell.assistantHint")}</div>
              </div>
              <button
                className="btn icon-btn"
                type="button"
                style={{ marginInlineStart: "auto" }}
                aria-label={t("shell.close")}
                onClick={() => setAssist(false)}
              >
                ✕
              </button>
            </div>
            <div className="msgs">
              <p>{t("shell.assistantBody")}</p>
              <div className="chips">
                <NavLink className="chip" to="/controls">
                  {t("nav.controls")}
                </NavLink>
                <NavLink className="chip" to="/frameworks">
                  {t("nav.frameworks")}
                </NavLink>
              </div>
            </div>
          </aside>
        </div>
      </div>
      {palette && (
        <div className="palette">
          <button
            type="button"
            className="backdrop"
            aria-label={t("shell.close")}
            onClick={() => setPalette(false)}
          />
          <div
            className="palette-card"
            role="dialog"
            aria-modal="true"
            aria-label={t("palette.placeholder")}
          >
            <input
              ref={paletteSearch}
              aria-label={t("palette.placeholder")}
              placeholder={t("palette.placeholder")}
              value={query}
              onChange={(event) => {
                setQuery(event.target.value);
                setActive(0);
              }}
              onKeyDown={(event) => {
                if (event.key === "ArrowDown")
                  setActive((index) => Math.min(index + 1, filtered.length - 1));
                if (event.key === "ArrowUp") setActive((index) => Math.max(index - 1, 0));
                if (event.key === "Enter" && filtered[active]) {
                  filtered[active].run();
                  setPalette(false);
                }
              }}
            />
            {filtered.length === 0 && (
              <div className="empty" style={{ padding: 16 }}>
                {t("palette.empty")}
              </div>
            )}
            {filtered.map((action, index) => (
              <button
                key={action.id}
                type="button"
                className={`item ${index === active ? "on" : ""}`}
                onClick={() => {
                  action.run();
                  setPalette(false);
                }}
              >
                {action.label}
              </button>
            ))}
          </div>
        </div>
      )}
      {tray && (
        <div className="tray">
          <button
            type="button"
            className="backdrop"
            aria-label={t("shell.close")}
            onClick={() => setTray(false)}
          />
          <div className="tray-card" role="dialog" aria-modal="true" aria-label={t("jobs.title")}>
            <div style={{ padding: 16 }}>
              <b>{t("jobs.title")}</b>
            </div>
            {jobs.length === 0 && (
              <div className="empty" style={{ padding: 16 }}>
                {t("jobs.empty")}
              </div>
            )}
            {jobs.map((job) => (
              <div className="job" key={job.id}>
                <b>{job.currentStep}</b>
                <div className="m">
                  {t("jobs.step", { step: job.step, total: job.totalSteps })} ·{" "}
                  {t(`jobs.${job.status.toLowerCase()}`, { defaultValue: job.status })}
                </div>
                <div
                  className="bar"
                  style={{ marginTop: 8 }}
                  aria-label={job.currentStep}
                  aria-valuenow={job.percent ?? 0}
                  aria-valuemin={0}
                  aria-valuemax={100}
                  role="progressbar"
                >
                  <i style={{ width: `${job.percent ?? 0}%` }} />
                </div>
                {job.error && <div className="error">{job.error}</div>}
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

export function useWorkspace() {
  const { tenantId } = useAuth();
  return tenantId;
}
