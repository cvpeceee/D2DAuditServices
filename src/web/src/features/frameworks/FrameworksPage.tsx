import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useOutletContext } from "react-router-dom";
import { ApiError, api, type FrameworkListItem, type Workspace } from "../../shared/api/client";
import { useAuth } from "../auth/AuthContext";
import { Pill, Ring, Skeleton } from "../../shared/ui/ui";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

type Simulation = {
  frameworkKey: string;
  frameworkName: string;
  totalRequirements: number;
  coveredRequirements: number;
  passingRequirements: number;
  overlapPercent: number;
  readinessPercent: number;
  gaps: { code: string; title: string; summary: string; suggestedControlCode: string }[];
};

export function FrameworksPage() {
  const { t } = useTranslation();
  const { tenantId } = useAuth();
  const outlet = useOutletContext<ShellContext | undefined>();
  const canWrite = outlet?.workspace?.canWrite ?? false;
  const [items, setItems] = useState<FrameworkListItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [simKey, setSimKey] = useState("");
  const [simulation, setSimulation] = useState<Simulation | null>(null);
  const [showCustom, setShowCustom] = useState(false);
  const [custom, setCustom] = useState({
    key: "",
    name: "",
    version: "1.0",
    description: "",
    requirementsCsv:
      "code,title,summary\nCUST-01,Zero-trust authentication,All interactive access uses phishing-resistant MFA.\nCUST-02,Encrypted customer exports,Bulk exports require owner sign-off and KMS encryption.",
  });

  function load() {
    if (!tenantId) return;
    void api<FrameworkListItem[]>("/api/v1/frameworks", { tenantId })
      .then((value) => {
        setItems(value);
        setError(null);
      })
      .catch((caught: unknown) => {
        setError(caught instanceof ApiError ? caught.code : "load");
      });
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  async function runSimulation(key: string) {
    if (!tenantId || !key) return;
    setSimKey(key);
    try {
      const result = await api<Simulation>(
        `/api/v1/frameworks/${encodeURIComponent(key)}/simulator`,
        { tenantId },
      );
      setSimulation(result);
      setError(null);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  async function adoptGaps() {
    if (!tenantId || !simulation) return;
    try {
      const created = await api<number>(
        `/api/v1/frameworks/${encodeURIComponent(simulation.frameworkKey)}/simulator/adopt`,
        { method: "POST", tenantId, body: {} },
      );
      setNotice(t("frameworks.adoptedNotice", { count: created, name: simulation.frameworkName }));
      load();
      await runSimulation(simulation.frameworkKey);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.code : "save");
    }
  }

  async function createCustom(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId) return;
    try {
      const created = await api<{ key: string; name: string; requirementCount: number }>(
        "/api/v1/frameworks/custom",
        { method: "POST", tenantId, body: custom },
      );
      setNotice(
        t("frameworks.customCreated", {
          name: created.name,
          count: created.requirementCount,
        }),
      );
      setCustom({ ...custom, key: "", name: "", description: "" });
      setShowCustom(false);
      load();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.code : "save");
    }
  }

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("frameworks.title")}</h1>
          <div className="sub">{t("frameworks.sub")}</div>
        </div>
        {canWrite && (
          <button
            className="btn primary"
            type="button"
            onClick={() => setShowCustom((open) => !open)}
          >
            {showCustom ? t("shell.close") : t("frameworks.uploadCustom")}
          </button>
        )}
      </div>

      {notice && (
        <div className="notice" role="status">
          {notice}
        </div>
      )}
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}

      {showCustom && canWrite && (
        <form className="card" onSubmit={(event) => void createCustom(event)}>
          <h3>{t("frameworks.uploadCustom")}</h3>
          <div className="split-fields">
            <label className="field">
              <span>{t("frameworks.customKey")}</span>
              <input
                value={custom.key}
                onChange={(event) => setCustom({ ...custom, key: event.target.value })}
                placeholder="internal-baseline"
                required
              />
            </label>
            <label className="field">
              <span>{t("frameworks.customName")}</span>
              <input
                value={custom.name}
                onChange={(event) => setCustom({ ...custom, name: event.target.value })}
                placeholder="Internal Cloud Baseline"
                required
              />
            </label>
            <label className="field">
              <span>{t("frameworks.customVersion")}</span>
              <input
                value={custom.version}
                onChange={(event) => setCustom({ ...custom, version: event.target.value })}
              />
            </label>
          </div>
          <label className="field">
            <span>{t("frameworks.customDescription")}</span>
            <input
              value={custom.description}
              onChange={(event) => setCustom({ ...custom, description: event.target.value })}
              placeholder="Company-specific governance clauses."
            />
          </label>
          <label className="field">
            <span>{t("frameworks.customCsv")}</span>
            <textarea
              rows={5}
              value={custom.requirementsCsv}
              onChange={(event) => setCustom({ ...custom, requirementsCsv: event.target.value })}
              required
            />
          </label>
          <button className="btn primary" type="submit">
            {t("frameworks.createCustom")}
          </button>
        </form>
      )}

      {items && items.length > 0 && (
        <section className="card">
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              flexWrap: "wrap",
              gap: 10,
            }}
          >
            <div>
              <h3>{t("frameworks.simulatorTitle")}</h3>
              <div className="m">{t("frameworks.simulatorSub")}</div>
            </div>
            <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
              <select
                aria-label={t("frameworks.simulatorSelect")}
                value={simKey}
                onChange={(event) => void runSimulation(event.target.value)}
              >
                <option value="">{t("frameworks.simulatorSelect")}</option>
                {items.map((fw) => (
                  <option key={fw.key} value={fw.key}>
                    {fw.name} ({fw.version})
                  </option>
                ))}
              </select>
            </div>
          </div>

          {simulation && (
            <div style={{ marginTop: 14 }}>
              <div className="kpis">
                <div className="kpi">
                  <span>{t("frameworks.overlap")}</span>
                  <b>{simulation.overlapPercent}%</b>
                </div>
                <div className="kpi">
                  <span>{t("frameworks.simReadiness")}</span>
                  <b>{simulation.readinessPercent}%</b>
                </div>
                <div className="kpi">
                  <span>{t("frameworks.coveredClauses")}</span>
                  <b>
                    {simulation.coveredRequirements} / {simulation.totalRequirements}
                  </b>
                </div>
                <div className="kpi">
                  <span>{t("frameworks.unmappedGaps")}</span>
                  <b>{simulation.gaps.length}</b>
                </div>
              </div>
              {simulation.gaps.length > 0 ? (
                <>
                  <div
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      margin: "12px 0 8px",
                    }}
                  >
                    <b>{t("frameworks.gapList")}</b>
                    {canWrite && (
                      <button
                        type="button"
                        className="btn primary"
                        onClick={() => void adoptGaps()}
                      >
                        {t("frameworks.adoptAllGaps", { count: simulation.gaps.length })}
                      </button>
                    )}
                  </div>
                  <ul>
                    {simulation.gaps.map((gap) => (
                      <li key={gap.code}>
                        <b>{gap.code}</b> · {gap.title}{" "}
                        <Pill status="warn">{gap.suggestedControlCode}</Pill>
                        <div className="id">{gap.summary}</div>
                      </li>
                    ))}
                  </ul>
                </>
              ) : (
                <div className="notice" style={{ marginTop: 10 }}>
                  {t("frameworks.zeroGaps")}
                </div>
              )}
            </div>
          )}
        </section>
      )}

      {!items && !error && (
        <div className="stack">
          <Skeleton className="lg" />
          <Skeleton className="lg" />
        </div>
      )}
      <div className="grid" style={{ gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))" }}>
        {items?.map((item) => (
          <Link
            key={item.key}
            to={`/frameworks/${item.key}`}
            className="card"
            style={{ textDecoration: "none", color: "inherit" }}
          >
            <div className="fw" style={{ alignItems: "flex-start", textAlign: "start" }}>
              <Ring
                value={item.percent}
                tone={item.percent >= 85 ? "var(--ok)" : "var(--accent)"}
              />
              <b>{item.name}</b>
              <div className="m">{item.version}</div>
              <p className="m">{item.description}</p>
              <small>{t("frameworks.requirements", { count: item.requirementCount })}</small>
              <small>{t("frameworks.mapped", { mapped: item.mappedControlCount })}</small>
            </div>
          </Link>
        ))}
      </div>
    </>
  );
}
