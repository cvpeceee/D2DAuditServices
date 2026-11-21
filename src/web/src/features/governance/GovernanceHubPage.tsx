import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useOutletContext } from "react-router-dom";
import { ApiError, api, type Workspace } from "../../shared/api/client";
import { formatWhen, Pill, Skeleton } from "../../shared/ui/ui";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

type LegalEntity = {
  id: string;
  code: string;
  name: string;
  jurisdiction: string;
  region: string;
  headcount: number;
  controlsPassing: number;
  controlsTotal: number;
  readinessPercent: number;
  primaryFramework: string;
  status: string;
  createdAt: string;
};

type EntityRollup = {
  summary: {
    entityCount: number;
    totalHeadcount: number;
    controlsPassing: number;
    controlsTotal: number;
    consolidatedReadinessPercent: number;
  };
  entities: LegalEntity[];
};

type PrivacyAssessment = {
  id: string;
  code: string;
  kind: string;
  activityName: string;
  dataSubjects: string;
  lawfulBasis: string;
  transferMechanism: string;
  riskLevel: string;
  status: string;
  ownerEmail: string;
  reviewDueOn: string;
  updatedAt: string;
};

type AiModel = {
  id: string;
  code: string;
  modelName: string;
  provider: string;
  euAiActRiskTier: string;
  trainingDataProvenance: string;
  guardrails: string;
  redTeamStatus: string;
  linkedControlCode: string;
  ownerEmail: string;
  evaluatedAt: string;
};

type PhishingCampaign = {
  id: string;
  code: string;
  name: string;
  source: string;
  sentCount: number;
  clickedCount: number;
  reportedCount: number;
  clickRatePercent: number;
  reportRatePercent: number;
  riskBand: string;
  remedialTrainingStatus: string;
  ranAt: string;
};

type AttackSurfaceAsset = {
  id: string;
  hostname: string;
  ipAddress: string;
  service: string;
  port: number;
  tlsDaysRemaining: number;
  shadowIt: boolean;
  severity: string;
  status: string;
  lastSeenAt: string;
};

type AuditorFirm = {
  id: string;
  code: string;
  firmName: string;
  accreditation: string;
  specialties: string;
  regions: string;
  leadAuditorEmail: string;
  invited: boolean;
  invitedAt: string | null;
};

type RemediationPr = {
  id: string;
  controlCode: string;
  targetResource: string;
  repository: string;
  branchName: string;
  title: string;
  artifactType: string;
  unifiedDiff: string;
  prNumber: number;
  prUrl: string;
  mode: string;
  status: string;
  evidenceId: string | null;
  evidenceSha256: string;
  createdAt: string;
};

type TimeMachineSnapshot = {
  date: string;
  passingTests: number;
  failingTests: number;
  testPassPercent: number;
  estimatedReadinessPercent: number;
  chainEntryCount: number;
  latestChainSequence: number;
  latestChainHead: string;
};

type TimeMachineComparison = {
  asOf: TimeMachineSnapshot;
  compareTo: TimeMachineSnapshot;
  passPercentDelta: number;
  readinessDelta: number;
  chainEntriesDelta: number;
  summary: string;
};

type Overview = {
  entities: EntityRollup;
  privacyAssessments: PrivacyAssessment[];
  aiModels: AiModel[];
  phishingCampaigns: PhishingCampaign[];
  attackSurface: AttackSurfaceAsset[];
  auditorNetwork: AuditorFirm[];
  remediationPrs: RemediationPr[];
  timeMachine: TimeMachineComparison;
};

type SyncResult = {
  provider: string;
  mode: string;
  updatedRecords: number;
  summary: string;
  syncedAt: string;
};

type ScimResult = {
  email: string;
  personName: string;
  state: string;
  leaverAccessFlagged: boolean;
  remainingActiveAccounts: number;
  offboardedCheckOutcome: string;
  offboardedCheckDetail: string;
  backgroundCheckOutcome: string;
  processedAt: string;
};

type TabKey = "entities" | "privacy" | "ai" | "workforce" | "asm" | "timemachine";

export function GovernanceHubPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [tab, setTab] = useState<TabKey>("entities");
  const [data, setData] = useState<Overview | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // Forms
  const [entityForm, setEntityForm] = useState({
    code: "",
    name: "",
    jurisdiction: "",
    region: "eu-central-1",
    headcount: 85,
    controlsPassing: 40,
    controlsTotal: 48,
    primaryFramework: "ISO 27001 + GDPR",
    status: "Active",
  });
  const [privacyForm, setPrivacyForm] = useState({
    code: "",
    kind: "RoPA",
    activityName: "",
    dataSubjects: "",
    lawfulBasis: "Art. 6(1)(b) Contract performance",
    transferMechanism: "Standard Contractual Clauses",
    riskLevel: "Medium",
    status: "InReview",
    ownerEmail: "",
    reviewDueOn: new Date(Date.now() + 90 * 86_400_000).toISOString().slice(0, 10),
  });
  const [aiForm, setAiForm] = useState({
    code: "",
    modelName: "",
    provider: "",
    euAiActRiskTier: "Limited",
    trainingDataProvenance: "",
    guardrails: "",
    redTeamStatus: "InProgress",
    linkedControlCode: "AI-01",
    ownerEmail: "",
  });
  const [scimForm, setScimForm] = useState({
    email: "dev.patel@harborline.example",
    reason: "SCIM 2.0 termination event from Okta/WorkOS",
    revokeDownstreamAccounts: false,
  });
  const [scimResult, setScimResult] = useState<ScimResult | null>(null);
  const [phishingForm, setPhishingForm] = useState({
    name: "",
    source: "KnowBe4",
    sentCount: 250,
    clickedCount: 6,
    reportedCount: 185,
  });
  const [asmForm, setAsmForm] = useState({
    hostname: "",
    ipAddress: "",
    service: "HTTPS Edge",
    port: 443,
    tlsDaysRemaining: 30,
    shadowIt: false,
    severity: "Medium",
    status: "Open",
  });
  const [prForm, setPrForm] = useState({
    controlCode: "CFG-01",
    targetResource: "harborline-staging-exports",
    repository: "harborline-data/infra-terraform",
    artifactType: "Terraform",
    customFix: "",
  });
  const [tmDates, setTmDates] = useState({
    asOf: new Date(Date.now() - 90 * 86_400_000).toISOString().slice(0, 10),
    compareTo: new Date().toISOString().slice(0, 10),
  });
  const [tmResult, setTmResult] = useState<TimeMachineComparison | null>(null);

  async function load() {
    if (!tenantId) return;
    setError(null);
    try {
      const overview = await api<Overview>("/api/v1/governance/overview", { tenantId });
      setData(overview);
      setTmResult(overview.timeMachine);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  async function runAction(fn: () => Promise<void>) {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await fn();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  const tabs: { key: TabKey; label: string }[] = [
    { key: "entities", label: t("governanceHubPage.tabs.entities") },
    { key: "privacy", label: t("governanceHubPage.tabs.privacy") },
    { key: "ai", label: t("governanceHubPage.tabs.ai") },
    { key: "workforce", label: t("governanceHubPage.tabs.workforce") },
    { key: "asm", label: t("governanceHubPage.tabs.asm") },
    { key: "timemachine", label: t("governanceHubPage.tabs.timemachine") },
  ];

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("governanceHubPage.title")}</h1>
          <div className="sub">{t("governanceHubPage.sub")}</div>
        </div>
        <div className="act">
          <Link className="btn" to="/tutorial">
            {t("nav.tutorial")}
          </Link>
        </div>
      </div>

      {notice && (
        <div className="notice" role="status">
          {notice}
        </div>
      )}
      {error && (
        <div className="error" role="alert">
          {error === "forbidden" ? t("errors.forbidden") : `${t("errors.load")} (${error})`}
        </div>
      )}

      <div className="tabs" role="tablist" aria-label={t("governanceHubPage.title")}>
        {tabs.map((item) => (
          <button
            key={item.key}
            type="button"
            role="tab"
            aria-selected={tab === item.key}
            className={`tab ${tab === item.key ? "on" : ""}`}
            onClick={() => setTab(item.key)}
          >
            {item.label}
          </button>
        ))}
      </div>

      {!data ? (
        <Skeleton className="lg" />
      ) : (
        <>
          {tab === "entities" && (
            <>
              <div className="kpis">
                <div className="kpi">
                  <small>{t("governanceHubPage.entityCount")}</small>
                  <b>{data.entities.summary.entityCount}</b>
                </div>
                <div className="kpi">
                  <small>{t("governanceHubPage.totalHeadcount")}</small>
                  <b>{data.entities.summary.totalHeadcount.toLocaleString()}</b>
                </div>
                <div className="kpi">
                  <small>{t("governanceHubPage.controlsRollup")}</small>
                  <b>
                    {data.entities.summary.controlsPassing}/{data.entities.summary.controlsTotal}
                  </b>
                </div>
                <div className="kpi">
                  <small>{t("governanceHubPage.consolidatedReadiness")}</small>
                  <b>{data.entities.summary.consolidatedReadinessPercent}%</b>
                </div>
              </div>

              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>Code</th>
                      <th>Subsidiary / Entity</th>
                      <th>Jurisdiction & Region</th>
                      <th>Headcount</th>
                      <th>Primary Framework</th>
                      <th>Readiness</th>
                      <th>Status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.entities.entities.map((ent) => (
                      <tr key={ent.id}>
                        <td className="id">{ent.code}</td>
                        <td>
                          <b>{ent.name}</b>
                        </td>
                        <td>
                          {ent.jurisdiction} · <span className="id">{ent.region}</span>
                        </td>
                        <td>{ent.headcount}</td>
                        <td>{ent.primaryFramework}</td>
                        <td>
                          <b>{ent.readinessPercent}%</b> ({ent.controlsPassing}/{ent.controlsTotal})
                        </td>
                        <td>
                          <Pill status={ent.status === "Active" ? "pass" : "warn"}>
                            {ent.status}
                          </Pill>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {workspace?.canWrite && (
                <form
                  className="card"
                  onSubmit={(e) => {
                    e.preventDefault();
                    void runAction(async () => {
                      await api("/api/v1/governance/entities", {
                        method: "POST",
                        tenantId: tenantId!,
                        body: entityForm,
                      });
                      setNotice(`Saved legal entity ${entityForm.code.toUpperCase()}.`);
                      setEntityForm({ ...entityForm, code: "", name: "", jurisdiction: "" });
                      await load();
                    });
                  }}
                >
                  <h3>{t("governanceHubPage.addEntity")}</h3>
                  <div className="grid2">
                    <label className="field">
                      <span>Code</span>
                      <input
                        required
                        placeholder="ENT-UK"
                        value={entityForm.code}
                        onChange={(e) => setEntityForm({ ...entityForm, code: e.target.value })}
                      />
                    </label>
                    <label className="field">
                      <span>Entity name</span>
                      <input
                        required
                        placeholder="Harborline UK Ltd."
                        value={entityForm.name}
                        onChange={(e) => setEntityForm({ ...entityForm, name: e.target.value })}
                      />
                    </label>
                    <label className="field">
                      <span>Jurisdiction</span>
                      <input
                        required
                        placeholder="United Kingdom (London)"
                        value={entityForm.jurisdiction}
                        onChange={(e) =>
                          setEntityForm({ ...entityForm, jurisdiction: e.target.value })
                        }
                      />
                    </label>
                    <label className="field">
                      <span>Cloud region</span>
                      <input
                        value={entityForm.region}
                        onChange={(e) => setEntityForm({ ...entityForm, region: e.target.value })}
                      />
                    </label>
                    <label className="field">
                      <span>Headcount</span>
                      <input
                        type="number"
                        min={0}
                        value={entityForm.headcount}
                        onChange={(e) =>
                          setEntityForm({ ...entityForm, headcount: Number(e.target.value) })
                        }
                      />
                    </label>
                    <label className="field">
                      <span>Primary framework</span>
                      <input
                        value={entityForm.primaryFramework}
                        onChange={(e) =>
                          setEntityForm({ ...entityForm, primaryFramework: e.target.value })
                        }
                      />
                    </label>
                    <label className="field">
                      <span>Controls passing</span>
                      <input
                        type="number"
                        min={0}
                        value={entityForm.controlsPassing}
                        onChange={(e) =>
                          setEntityForm({ ...entityForm, controlsPassing: Number(e.target.value) })
                        }
                      />
                    </label>
                    <label className="field">
                      <span>Controls total</span>
                      <input
                        type="number"
                        min={1}
                        value={entityForm.controlsTotal}
                        onChange={(e) =>
                          setEntityForm({ ...entityForm, controlsTotal: Number(e.target.value) })
                        }
                      />
                    </label>
                  </div>
                  <button className="btn primary" type="submit" disabled={busy}>
                    Save entity
                  </button>
                </form>
              )}
            </>
          )}

          {tab === "privacy" && (
            <>
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>Code</th>
                      <th>Type</th>
                      <th>Processing Activity</th>
                      <th>Lawful Basis & Transfer</th>
                      <th>Risk</th>
                      <th>Status</th>
                      <th>Review Due</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.privacyAssessments.map((item) => (
                      <tr key={item.id}>
                        <td className="id">{item.code}</td>
                        <td>
                          <Pill status={item.kind === "DPIA" ? "warn" : "info"}>{item.kind}</Pill>
                        </td>
                        <td>
                          <b>{item.activityName}</b>
                          <div className="sub">{item.dataSubjects}</div>
                        </td>
                        <td>
                          <div>{item.lawfulBasis}</div>
                          <div className="sub">{item.transferMechanism}</div>
                        </td>
                        <td>
                          <Pill
                            status={
                              item.riskLevel === "High"
                                ? "fail"
                                : item.riskLevel === "Medium"
                                  ? "warn"
                                  : "pass"
                            }
                          >
                            {item.riskLevel}
                          </Pill>
                        </td>
                        <td>
                          <Pill status={item.status === "Approved" ? "pass" : "warn"}>
                            {item.status}
                          </Pill>
                        </td>
                        <td>{item.reviewDueOn}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {workspace?.canWrite && (
                <form
                  className="card"
                  onSubmit={(e) => {
                    e.preventDefault();
                    void runAction(async () => {
                      await api("/api/v1/governance/privacy", {
                        method: "POST",
                        tenantId: tenantId!,
                        body: privacyForm,
                      });
                      setNotice(`Saved privacy record ${privacyForm.code.toUpperCase()}.`);
                      setPrivacyForm({
                        ...privacyForm,
                        code: "",
                        activityName: "",
                        dataSubjects: "",
                      });
                      await load();
                    });
                  }}
                >
                  <h3>Record RoPA (GDPR Art. 30) or DPIA (GDPR Art. 35)</h3>
                  <div className="grid2">
                    <label className="field">
                      <span>Code</span>
                      <input
                        required
                        placeholder="DPIA-02"
                        value={privacyForm.code}
                        onChange={(e) => setPrivacyForm({ ...privacyForm, code: e.target.value })}
                      />
                    </label>
                    <label className="field">
                      <span>Kind</span>
                      <select
                        value={privacyForm.kind}
                        onChange={(e) => setPrivacyForm({ ...privacyForm, kind: e.target.value })}
                      >
                        <option value="RoPA">RoPA (Art. 30)</option>
                        <option value="DPIA">DPIA (Art. 35)</option>
                      </select>
                    </label>
                    <label className="field">
                      <span>Processing activity</span>
                      <input
                        required
                        value={privacyForm.activityName}
                        onChange={(e) =>
                          setPrivacyForm({ ...privacyForm, activityName: e.target.value })
                        }
                      />
                    </label>
                    <label className="field">
                      <span>Data subjects</span>
                      <input
                        required
                        value={privacyForm.dataSubjects}
                        onChange={(e) =>
                          setPrivacyForm({ ...privacyForm, dataSubjects: e.target.value })
                        }
                      />
                    </label>
                    <label className="field">
                      <span>Lawful basis</span>
                      <input
                        required
                        value={privacyForm.lawfulBasis}
                        onChange={(e) =>
                          setPrivacyForm({ ...privacyForm, lawfulBasis: e.target.value })
                        }
                      />
                    </label>
                    <label className="field">
                      <span>Transfer mechanism</span>
                      <input
                        value={privacyForm.transferMechanism}
                        onChange={(e) =>
                          setPrivacyForm({ ...privacyForm, transferMechanism: e.target.value })
                        }
                      />
                    </label>
                    <label className="field">
                      <span>Risk level</span>
                      <select
                        value={privacyForm.riskLevel}
                        onChange={(e) =>
                          setPrivacyForm({ ...privacyForm, riskLevel: e.target.value })
                        }
                      >
                        <option value="Low">Low</option>
                        <option value="Medium">Medium</option>
                        <option value="High">High</option>
                      </select>
                    </label>
                    <label className="field">
                      <span>Status</span>
                      <select
                        value={privacyForm.status}
                        onChange={(e) => setPrivacyForm({ ...privacyForm, status: e.target.value })}
                      >
                        <option value="Draft">Draft</option>
                        <option value="InReview">In review</option>
                        <option value="Approved">Approved</option>
                      </select>
                    </label>
                  </div>
                  <button className="btn primary" type="submit" disabled={busy}>
                    Save privacy record
                  </button>
                </form>
              )}
            </>
          )}

          {tab === "ai" && (
            <>
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>Code</th>
                      <th>Model & Provider</th>
                      <th>EU AI Act Tier</th>
                      <th>Training Provenance & Guardrails</th>
                      <th>Red-Team</th>
                      <th>Control</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.aiModels.map((model) => (
                      <tr key={model.id}>
                        <td className="id">{model.code}</td>
                        <td>
                          <b>{model.modelName}</b>
                          <div className="sub">{model.provider}</div>
                        </td>
                        <td>
                          <Pill
                            status={
                              model.euAiActRiskTier === "High"
                                ? "fail"
                                : model.euAiActRiskTier === "Limited"
                                  ? "warn"
                                  : "pass"
                            }
                          >
                            {model.euAiActRiskTier}
                          </Pill>
                        </td>
                        <td>
                          <div>{model.trainingDataProvenance}</div>
                          <div className="sub">{model.guardrails}</div>
                        </td>
                        <td>
                          <Pill status={model.redTeamStatus === "Passed" ? "pass" : "warn"}>
                            {model.redTeamStatus}
                          </Pill>
                        </td>
                        <td>
                          <Link
                            className="id"
                            to={`/controls/${encodeURIComponent(model.linkedControlCode)}`}
                          >
                            {model.linkedControlCode}
                          </Link>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {workspace?.canWrite && (
                <form
                  className="card"
                  onSubmit={(e) => {
                    e.preventDefault();
                    void runAction(async () => {
                      await api("/api/v1/governance/ai-models", {
                        method: "POST",
                        tenantId: tenantId!,
                        body: aiForm,
                      });
                      setNotice(`Registered AI model ${aiForm.code.toUpperCase()}.`);
                      setAiForm({
                        ...aiForm,
                        code: "",
                        modelName: "",
                        provider: "",
                        trainingDataProvenance: "",
                        guardrails: "",
                      });
                      await load();
                    });
                  }}
                >
                  <h3>Register AI model (ISO 42001 / EU AI Act / NIST AI RMF)</h3>
                  <div className="grid2">
                    <label className="field">
                      <span>Code</span>
                      <input
                        required
                        placeholder="AIM-03"
                        value={aiForm.code}
                        onChange={(e) => setAiForm({ ...aiForm, code: e.target.value })}
                      />
                    </label>
                    <label className="field">
                      <span>Model name</span>
                      <input
                        required
                        value={aiForm.modelName}
                        onChange={(e) => setAiForm({ ...aiForm, modelName: e.target.value })}
                      />
                    </label>
                    <label className="field">
                      <span>Provider</span>
                      <input
                        required
                        value={aiForm.provider}
                        onChange={(e) => setAiForm({ ...aiForm, provider: e.target.value })}
                      />
                    </label>
                    <label className="field">
                      <span>EU AI Act risk tier</span>
                      <select
                        value={aiForm.euAiActRiskTier}
                        onChange={(e) => setAiForm({ ...aiForm, euAiActRiskTier: e.target.value })}
                      >
                        <option value="Minimal">Minimal</option>
                        <option value="Limited">Limited</option>
                        <option value="High">High</option>
                      </select>
                    </label>
                    <label className="field">
                      <span>Training data provenance</span>
                      <input
                        required
                        value={aiForm.trainingDataProvenance}
                        onChange={(e) =>
                          setAiForm({ ...aiForm, trainingDataProvenance: e.target.value })
                        }
                      />
                    </label>
                    <label className="field">
                      <span>Human-in-the-loop guardrails</span>
                      <input
                        value={aiForm.guardrails}
                        onChange={(e) => setAiForm({ ...aiForm, guardrails: e.target.value })}
                      />
                    </label>
                  </div>
                  <button className="btn primary" type="submit" disabled={busy}>
                    Save AI model
                  </button>
                </form>
              )}
            </>
          )}

          {tab === "workforce" && (
            <>
              <div className="grid2">
                <section className="card">
                  <h3>Checkr Background Screening & KnowBe4 Sync</h3>
                  <p className="sub">
                    Sync background screenings from Checkr (updating HR-01) and security awareness +
                    phishing drills from KnowBe4.
                  </p>
                  <div className="act">
                    {workspace?.canWrite && (
                      <>
                        <button
                          className="btn primary"
                          type="button"
                          disabled={busy}
                          onClick={() =>
                            void runAction(async () => {
                              const res = await api<SyncResult>("/api/v1/governance/checkr/sync", {
                                method: "POST",
                                tenantId: tenantId!,
                              });
                              setNotice(res.summary);
                              await load();
                            })
                          }
                        >
                          Sync Checkr now
                        </button>
                        <button
                          className="btn"
                          type="button"
                          disabled={busy}
                          onClick={() =>
                            void runAction(async () => {
                              const res = await api<SyncResult>("/api/v1/governance/knowbe4/sync", {
                                method: "POST",
                                tenantId: tenantId!,
                              });
                              setNotice(res.summary);
                              await load();
                            })
                          }
                        >
                          Sync KnowBe4 now
                        </button>
                      </>
                    )}
                  </div>
                </section>

                <section className="card">
                  <h3>SCIM 2.0 Automated User Deprovisioning</h3>
                  <p className="sub">
                    Deactivates a user via SCIM 2.0, checks remaining active cloud/repo accounts for
                    leaver access, and recomputes <code>offboarded-removed</code> &{" "}
                    <code>HR-01</code>.
                  </p>
                  {workspace?.canWrite && (
                    <form
                      onSubmit={(e) => {
                        e.preventDefault();
                        void runAction(async () => {
                          const res = await api<ScimResult>(
                            `/api/v1/scim/v2/Users/${encodeURIComponent(scimForm.email)}/deprovision`,
                            {
                              method: "POST",
                              tenantId: tenantId!,
                              body: scimForm,
                            },
                          );
                          setScimResult(res);
                          setNotice(
                            `SCIM 2.0 deprovisioned ${res.personName}: offboarded-removed=${res.offboardedCheckOutcome}.`,
                          );
                        });
                      }}
                    >
                      <label className="field">
                        <span>User email</span>
                        <input
                          required
                          value={scimForm.email}
                          onChange={(e) => setScimForm({ ...scimForm, email: e.target.value })}
                        />
                      </label>
                      <label className="field">
                        <span>Reason</span>
                        <input
                          value={scimForm.reason}
                          onChange={(e) => setScimForm({ ...scimForm, reason: e.target.value })}
                        />
                      </label>
                      <label className="check" style={{ marginBottom: 10 }}>
                        <input
                          type="checkbox"
                          checked={scimForm.revokeDownstreamAccounts}
                          onChange={(e) =>
                            setScimForm({
                              ...scimForm,
                              revokeDownstreamAccounts: e.target.checked,
                            })
                          }
                        />
                        <span>Also revoke downstream cloud/repo accounts immediately</span>
                      </label>
                      <button className="btn primary" type="submit" disabled={busy}>
                        Trigger SCIM 2.0 deprovision
                      </button>
                    </form>
                  )}
                  {scimResult && (
                    <div className="notice" style={{ marginTop: 10 }}>
                      <b>{scimResult.personName}</b> ({scimResult.state}) · Leaver flagged:{" "}
                      <b>{scimResult.leaverAccessFlagged ? "Yes" : "No"}</b> · Remaining accounts:{" "}
                      {scimResult.remainingActiveAccounts} · Test <code>offboarded-removed</code>:{" "}
                      <b>{scimResult.offboardedCheckOutcome}</b> ({scimResult.offboardedCheckDetail}
                      )
                    </div>
                  )}
                </section>
              </div>

              <h3 style={{ marginTop: 18 }}>Phishing Simulation Campaigns (KnowBe4 & Internal)</h3>
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>Code</th>
                      <th>Campaign</th>
                      <th>Source</th>
                      <th>Sent</th>
                      <th>Click Rate</th>
                      <th>Report Rate</th>
                      <th>Risk</th>
                      <th>Remedial Training</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.phishingCampaigns.map((camp) => (
                      <tr key={camp.id}>
                        <td className="id">{camp.code}</td>
                        <td>
                          <b>{camp.name}</b>
                          <div className="sub">{formatWhen(camp.ranAt)}</div>
                        </td>
                        <td>{camp.source}</td>
                        <td>{camp.sentCount}</td>
                        <td>
                          <b>{camp.clickRatePercent}%</b> ({camp.clickedCount})
                        </td>
                        <td>
                          <b>{camp.reportRatePercent}%</b> ({camp.reportedCount})
                        </td>
                        <td>
                          <Pill
                            status={
                              camp.riskBand === "High"
                                ? "fail"
                                : camp.riskBand === "Medium"
                                  ? "warn"
                                  : "pass"
                            }
                          >
                            {camp.riskBand}
                          </Pill>
                        </td>
                        <td>{camp.remedialTrainingStatus}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {workspace?.canWrite && (
                <form
                  className="card"
                  onSubmit={(e) => {
                    e.preventDefault();
                    void runAction(async () => {
                      await api("/api/v1/governance/phishing", {
                        method: "POST",
                        tenantId: tenantId!,
                        body: phishingForm,
                      });
                      setNotice(`Recorded phishing drill "${phishingForm.name}".`);
                      setPhishingForm({ ...phishingForm, name: "" });
                      await load();
                    });
                  }}
                >
                  <h3>Log phishing simulation drill</h3>
                  <div className="grid2">
                    <label className="field">
                      <span>Campaign name</span>
                      <input
                        required
                        placeholder="Q1 MFA Fatigue Simulation"
                        value={phishingForm.name}
                        onChange={(e) => setPhishingForm({ ...phishingForm, name: e.target.value })}
                      />
                    </label>
                    <label className="field">
                      <span>Source</span>
                      <select
                        value={phishingForm.source}
                        onChange={(e) =>
                          setPhishingForm({ ...phishingForm, source: e.target.value })
                        }
                      >
                        <option value="KnowBe4">KnowBe4</option>
                        <option value="Internal">Internal</option>
                      </select>
                    </label>
                    <label className="field">
                      <span>Sent count</span>
                      <input
                        type="number"
                        min={1}
                        value={phishingForm.sentCount}
                        onChange={(e) =>
                          setPhishingForm({ ...phishingForm, sentCount: Number(e.target.value) })
                        }
                      />
                    </label>
                    <label className="field">
                      <span>Clicked count</span>
                      <input
                        type="number"
                        min={0}
                        value={phishingForm.clickedCount}
                        onChange={(e) =>
                          setPhishingForm({ ...phishingForm, clickedCount: Number(e.target.value) })
                        }
                      />
                    </label>
                    <label className="field">
                      <span>Reported count</span>
                      <input
                        type="number"
                        min={0}
                        value={phishingForm.reportedCount}
                        onChange={(e) =>
                          setPhishingForm({
                            ...phishingForm,
                            reportedCount: Number(e.target.value),
                          })
                        }
                      />
                    </label>
                  </div>
                  <button className="btn primary" type="submit" disabled={busy}>
                    Save campaign
                  </button>
                </form>
              )}
            </>
          )}

          {tab === "asm" && (
            <>
              <h3>External Attack Surface Monitoring (ASM)</h3>
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>Hostname / IP</th>
                      <th>Service & Port</th>
                      <th>TLS Expiry</th>
                      <th>Shadow IT</th>
                      <th>Severity</th>
                      <th>Status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.attackSurface.map((asset) => (
                      <tr key={asset.id}>
                        <td>
                          <b>{asset.hostname}</b>
                          <div className="id">{asset.ipAddress}</div>
                        </td>
                        <td>
                          {asset.service} (:{asset.port})
                        </td>
                        <td>
                          <Pill status={asset.tlsDaysRemaining <= 14 ? "fail" : "pass"}>
                            {`${asset.tlsDaysRemaining}d left`}
                          </Pill>
                        </td>
                        <td>{asset.shadowIt ? <Pill status="warn">Shadow IT</Pill> : "Tracked"}</td>
                        <td>
                          <Pill
                            status={
                              asset.severity === "Critical" || asset.severity === "High"
                                ? "fail"
                                : "warn"
                            }
                          >
                            {asset.severity}
                          </Pill>
                        </td>
                        <td>
                          <Pill status={asset.status === "Mitigated" ? "pass" : "warn"}>
                            {asset.status}
                          </Pill>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {workspace?.canWrite && (
                <form
                  className="card"
                  onSubmit={(e) => {
                    e.preventDefault();
                    void runAction(async () => {
                      await api("/api/v1/governance/attack-surface", {
                        method: "POST",
                        tenantId: tenantId!,
                        body: asmForm,
                      });
                      setNotice(`Recorded external edge asset ${asmForm.hostname}.`);
                      setAsmForm({ ...asmForm, hostname: "", ipAddress: "" });
                      await load();
                    });
                  }}
                >
                  <h3>Register discovered external asset</h3>
                  <div className="grid2">
                    <label className="field">
                      <span>Hostname</span>
                      <input
                        required
                        placeholder="staging-api.harborline.example"
                        value={asmForm.hostname}
                        onChange={(e) => setAsmForm({ ...asmForm, hostname: e.target.value })}
                      />
                    </label>
                    <label className="field">
                      <span>IP address</span>
                      <input
                        required
                        placeholder="54.112.40.8"
                        value={asmForm.ipAddress}
                        onChange={(e) => setAsmForm({ ...asmForm, ipAddress: e.target.value })}
                      />
                    </label>
                    <label className="field">
                      <span>Service</span>
                      <input
                        required
                        value={asmForm.service}
                        onChange={(e) => setAsmForm({ ...asmForm, service: e.target.value })}
                      />
                    </label>
                    <label className="field">
                      <span>Port</span>
                      <input
                        type="number"
                        min={1}
                        max={65535}
                        value={asmForm.port}
                        onChange={(e) => setAsmForm({ ...asmForm, port: Number(e.target.value) })}
                      />
                    </label>
                  </div>
                  <button className="btn" type="submit" disabled={busy}>
                    Save ASM asset
                  </button>
                </form>
              )}

              <h3 style={{ marginTop: 20 }}>
                1-Click Remediation Pull Request Generator (Signed into EvidenceChain)
              </h3>
              {workspace?.canWrite && (
                <form
                  className="card"
                  onSubmit={(e) => {
                    e.preventDefault();
                    void runAction(async () => {
                      const created = await api<RemediationPr>(
                        "/api/v1/governance/remediation-prs",
                        {
                          method: "POST",
                          tenantId: tenantId!,
                          body: prForm,
                        },
                      );
                      setNotice(
                        `Generated Remediation PR #${created.prNumber} on ${created.branchName} and appended SHA-256 receipt (${created.evidenceSha256.slice(0, 12)}…) to EvidenceChain.`,
                      );
                      await load();
                    });
                  }}
                >
                  <div className="grid2">
                    <label className="field">
                      <span>Control code</span>
                      <input
                        required
                        value={prForm.controlCode}
                        onChange={(e) => setPrForm({ ...prForm, controlCode: e.target.value })}
                      />
                    </label>
                    <label className="field">
                      <span>Target resource</span>
                      <input
                        required
                        value={prForm.targetResource}
                        onChange={(e) => setPrForm({ ...prForm, targetResource: e.target.value })}
                      />
                    </label>
                    <label className="field">
                      <span>Git repository</span>
                      <input
                        required
                        value={prForm.repository}
                        onChange={(e) => setPrForm({ ...prForm, repository: e.target.value })}
                      />
                    </label>
                    <label className="field">
                      <span>Artifact type</span>
                      <select
                        value={prForm.artifactType}
                        onChange={(e) => setPrForm({ ...prForm, artifactType: e.target.value })}
                      >
                        <option value="Terraform">Terraform HCL</option>
                        <option value="Dockerfile">Dockerfile</option>
                        <option value="GitHubWorkflow">GitHub Workflow</option>
                        <option value="IamPolicy">IAM Policy</option>
                      </select>
                    </label>
                  </div>
                  <button className="btn primary" type="submit" disabled={busy}>
                    Generate 1-Click Remediation PR & Sign to EvidenceChain
                  </button>
                </form>
              )}

              {data.remediationPrs.map((pr) => (
                <article key={pr.id} className="card">
                  <div className="head">
                    <div>
                      <b>
                        PR #{pr.prNumber}: {pr.title}
                      </b>
                      <div className="sub">
                        Branch <code>{pr.branchName}</code> · {pr.repository} · Mode:{" "}
                        <code>{pr.mode}</code> · Evidence SHA-256:{" "}
                        <code>{pr.evidenceSha256.slice(0, 16)}…</code>
                      </div>
                    </div>
                    <Pill status="pass">{pr.artifactType}</Pill>
                  </div>
                  <pre className="code">{pr.unifiedDiff}</pre>
                </article>
              ))}
            </>
          )}

          {tab === "timemachine" && (
            <>
              <section className="card">
                <h3>Audit Time Machine — Point-in-Time Posture & EvidenceChain Diff</h3>
                <p className="sub">
                  Compare your historical compliance test pass rate, readiness estimate, and SHA-256
                  EvidenceChain growth between any two audit dates.
                </p>
                <form
                  className="grid2"
                  onSubmit={(e) => {
                    e.preventDefault();
                    void runAction(async () => {
                      const res = await api<TimeMachineComparison>(
                        `/api/v1/governance/time-machine?asOf=${encodeURIComponent(tmDates.asOf)}&compareTo=${encodeURIComponent(tmDates.compareTo)}`,
                        { tenantId: tenantId! },
                      );
                      setTmResult(res);
                    });
                  }}
                >
                  <label className="field">
                    <span>Baseline date (asOf)</span>
                    <input
                      type="date"
                      value={tmDates.asOf}
                      onChange={(e) => setTmDates({ ...tmDates, asOf: e.target.value })}
                    />
                  </label>
                  <label className="field">
                    <span>Comparison date (compareTo)</span>
                    <input
                      type="date"
                      value={tmDates.compareTo}
                      onChange={(e) => setTmDates({ ...tmDates, compareTo: e.target.value })}
                    />
                  </label>
                  <div>
                    <button className="btn primary" type="submit" disabled={busy}>
                      Compare point-in-time posture
                    </button>
                  </div>
                </form>

                {tmResult && (
                  <div style={{ marginTop: 14 }}>
                    <div className="notice">{tmResult.summary}</div>
                    <div className="kpis" style={{ marginTop: 10 }}>
                      <div className="kpi">
                        <small>Baseline ({tmResult.asOf.date})</small>
                        <b>{tmResult.asOf.estimatedReadinessPercent}%</b>
                        <div className="sub">
                          {tmResult.asOf.passingTests} pass / {tmResult.asOf.failingTests} fail ·{" "}
                          {tmResult.asOf.chainEntryCount} chain entries
                        </div>
                      </div>
                      <div className="kpi">
                        <small>Target ({tmResult.compareTo.date})</small>
                        <b>{tmResult.compareTo.estimatedReadinessPercent}%</b>
                        <div className="sub">
                          {tmResult.compareTo.passingTests} pass / {tmResult.compareTo.failingTests}{" "}
                          fail · {tmResult.compareTo.chainEntryCount} chain entries
                        </div>
                      </div>
                      <div className="kpi">
                        <small>Readiness Delta</small>
                        <b>
                          {tmResult.readinessDelta >= 0 ? "+" : ""}
                          {tmResult.readinessDelta}%
                        </b>
                      </div>
                      <div className="kpi">
                        <small>EvidenceChain Growth</small>
                        <b>
                          {tmResult.chainEntriesDelta >= 0 ? "+" : ""}
                          {tmResult.chainEntriesDelta}
                        </b>
                      </div>
                    </div>
                  </div>
                )}
              </section>

              <h3 style={{ marginTop: 18 }}>Verified Auditor Network Directory</h3>
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>Firm</th>
                      <th>Accreditation</th>
                      <th>Specialties</th>
                      <th>Regions</th>
                      <th>Action</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.auditorNetwork.map((firm) => (
                      <tr key={firm.id}>
                        <td>
                          <b>{firm.firmName}</b>
                          <div className="sub">{firm.leadAuditorEmail}</div>
                        </td>
                        <td>{firm.accreditation}</td>
                        <td>{firm.specialties}</td>
                        <td>{firm.regions}</td>
                        <td>
                          {firm.invited ? (
                            <Pill status="pass">Invited to /auditor</Pill>
                          ) : (
                            workspace?.canWrite && (
                              <button
                                className="btn"
                                type="button"
                                disabled={busy}
                                onClick={() =>
                                  void runAction(async () => {
                                    await api(
                                      `/api/v1/governance/auditor-network/${firm.id}/invite`,
                                      {
                                        method: "POST",
                                        tenantId: tenantId!,
                                      },
                                    );
                                    setNotice(`Invited ${firm.firmName} to /auditor.`);
                                    await load();
                                  })
                                }
                              >
                                Invite to /auditor
                              </button>
                            )
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          )}
        </>
      )}
    </>
  );
}
