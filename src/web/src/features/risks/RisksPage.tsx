import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import {
  ApiError,
  api,
  download,
  type RiskListItem,
  type Workspace,
} from "../../shared/api/client";
import { formatDate, Pill } from "../../shared/ui/ui";
import { AssignTask } from "../program/AssignTask";
import {
  annualizedLoss,
  buildHeatmap,
  coordinates,
  criticalCount,
  type HeatmapView,
} from "./heatmap";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

type RichRisk = RiskListItem & {
  approved?: boolean;
  approvedBy?: string;
  approvedOn?: string | null;
  libraryCode?: string;
  reviewCadence?: string;
  acceptedBy?: string;
  acceptedOn?: string | null;
  acceptanceExpiresOn?: string | null;
  singleLossExpectancy?: number | null;
  annualRateOfOccurrence?: number | null;
  annualizedLossExpectancy?: number | null;
};

type LibraryItem = {
  code: string;
  title: string;
  statement: string;
  likelihood: number;
  impact: number;
  treatment: string;
  added: boolean;
  suggestedControls?: string[] | null;
  category?: string;
};

const treatments = ["Mitigate", "Accept", "Transfer", "Avoid"] as const;
const cadences = ["Monthly", "Quarterly", "SemiAnnual", "Annual"] as const;

function inDays(days: number) {
  const date = new Date();
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

function money(value: number | null | undefined) {
  return value == null
    ? "—"
    : new Intl.NumberFormat(undefined, {
        style: "currency",
        currency: "USD",
        maximumFractionDigits: 0,
      }).format(value);
}

function optionalNumber(value: string) {
  return value.trim() === "" ? null : Number(value);
}

function bandStatus(band: string) {
  if (band === "critical") return "fail";
  if (band === "high" || band === "medium") return "warn";
  if (band === "low") return "pass";
  return undefined;
}

const emptyRisk = {
  code: "",
  title: "",
  statement: "",
  likelihood: 3,
  impact: 3,
  ownerName: "",
  ownerEmail: "",
  treatment: "Mitigate",
  treatmentPlan: "",
  reviewOn: new Date().toISOString().slice(0, 10),
  residualLikelihood: 2,
  residualImpact: 2,
  controlCodes: "",
  approved: false,
  reviewCadence: "Quarterly",
  singleLossExpectancy: "",
  annualRateOfOccurrence: "",
};

export function RisksPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [rows, setRows] = useState<RichRisk[] | null>(null);
  const [library, setLibrary] = useState<LibraryItem[] | null>(null);
  const [form, setForm] = useState(emptyRisk);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [filter, setFilter] = useState<{ likelihood: number; impact: number } | null>(null);
  const [view, setView] = useState<HeatmapView>("inherent");
  const [acceptance, setAcceptance] = useState({
    approver: "",
    expiresOn: inDays(180),
    rationale: "",
  });
  const [notice, setNotice] = useState<string | null>(null);

  const load = () => {
    if (!tenantId) return;
    void api<RichRisk[]>("/api/v1/risks", { tenantId })
      .then(setRows)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
    void api<LibraryItem[]>("/api/v1/risks/library", { tenantId })
      .then(setLibrary)
      .catch(() => setLibrary([]));
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  const shown = (rows ?? []).filter((row) => {
    if (!filter) return true;
    const at = coordinates(row, view);
    return at.likelihood === filter.likelihood && at.impact === filter.impact;
  });
  const cells = buildHeatmap(rows ?? [], view);
  const totalLoss = (rows ?? []).reduce(
    (sum, row) => sum + (annualizedLoss(row.singleLossExpectancy, row.annualRateOfOccurrence) ?? 0),
    0,
  );
  const formLoss = annualizedLoss(
    optionalNumber(form.singleLossExpectancy),
    optionalNumber(form.annualRateOfOccurrence),
  );

  async function acceptRisk(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId || !form.code) return;
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await api(`/api/v1/risks/${encodeURIComponent(form.code)}/accept`, {
        method: "POST",
        tenantId,
        body: {
          approver: acceptance.approver || null,
          expiresOn: acceptance.expiresOn,
          rationale: acceptance.rationale || null,
        },
      });
      setNotice(t("risksPage.acceptance.done", { code: form.code }));
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  function select(row: RichRisk) {
    setForm({
      code: row.code,
      title: row.title,
      statement: row.statement,
      likelihood: row.likelihood,
      impact: row.impact,
      ownerName: row.ownerName,
      ownerEmail: row.ownerEmail,
      treatment: row.treatment,
      treatmentPlan: row.treatmentPlan,
      reviewOn: row.reviewOn,
      residualLikelihood: row.residualLikelihood,
      residualImpact: row.residualImpact,
      controlCodes: row.controlCodes.join(", "),
      approved: Boolean(row.approved),
      reviewCadence: row.reviewCadence ?? "Quarterly",
      singleLossExpectancy:
        row.singleLossExpectancy == null ? "" : String(row.singleLossExpectancy),
      annualRateOfOccurrence:
        row.annualRateOfOccurrence == null ? "" : String(row.annualRateOfOccurrence),
    });
  }

  async function adopt(code: string) {
    if (!tenantId) return;
    setBusy(true);
    setError(null);
    try {
      await api(`/api/v1/risks/library/${encodeURIComponent(code)}`, {
        method: "POST",
        tenantId,
        body: {},
      });
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  async function save(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId) return;
    setBusy(true);
    setError(null);
    try {
      await api("/api/v1/risks", {
        method: "POST",
        tenantId,
        body: {
          ...form,
          code: form.code || null,
          controlCodes: form.controlCodes
            .split(",")
            .map((code) => code.trim())
            .filter(Boolean),
          approved: form.approved,
          singleLossExpectancy: optionalNumber(form.singleLossExpectancy),
          annualRateOfOccurrence: optionalNumber(form.annualRateOfOccurrence),
        },
      });
      load();
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
          <h1>{t("risksPage.title")}</h1>
          <div className="sub">{t("risksPage.sub")}</div>
        </div>
        <div className="act">
          <button
            className="btn"
            type="button"
            onClick={() => void download("/api/v1/risks/export?format=csv", tenantId)}
          >
            {t("risksPage.exportCsv")}
          </button>
          <button
            className="btn"
            type="button"
            onClick={() => void download("/api/v1/risks/export?format=pdf", tenantId)}
          >
            {t("risksPage.exportPdf")}
          </button>
        </div>
      </div>
      {error && (
        <div className="error" role="alert">
          {t(`risksPage.errors.${error}`, { defaultValue: t("errors.load") })}
        </div>
      )}
      {notice && (
        <div className="notice" role="status">
          {notice}
        </div>
      )}
      <div className="card">
        <div className="label-row">
          <h3>{t("risksPage.heat")}</h3>
          <div className="toggle" role="group" aria-label={t("risksPage.heatView")}>
            {(["inherent", "residual"] as const).map((item) => (
              <button
                key={item}
                type="button"
                className={view === item ? "on" : ""}
                aria-pressed={view === item}
                onClick={() => {
                  setView(item);
                  setFilter(null);
                }}
              >
                {t(`risksPage.views.${item}`)}
              </button>
            ))}
          </div>
        </div>
        <div className="heat" role="grid" aria-label={t("risksPage.heat")}>
          <span />
          {[1, 2, 3, 4, 5].map((likelihood) => (
            <span className="axis" key={likelihood}>
              {likelihood}
            </span>
          ))}
          {[5, 4, 3, 2, 1].map((impact) => (
            <span key={impact} style={{ display: "contents" }}>
              <span className="axis">{impact}</span>
              {cells
                .filter((cell) => cell.impact === impact)
                .map(({ likelihood, band, codes }) => {
                  const count = codes.length;
                  return (
                    <button
                      key={`${likelihood}-${impact}`}
                      type="button"
                      className={`cell ${band}`}
                      title={codes.join(", ")}
                      aria-pressed={filter?.likelihood === likelihood && filter.impact === impact}
                      aria-label={t("risksPage.cell", { likelihood, impact, count })}
                      onClick={() =>
                        setFilter((current) =>
                          current?.likelihood === likelihood && current.impact === impact
                            ? null
                            : { likelihood, impact },
                        )
                      }
                    >
                      {count}
                    </button>
                  );
                })}
            </span>
          ))}
        </div>
        <div className="hint">
          {t("risksPage.likelihood")} → · {t("risksPage.impact")} ↑
        </div>
        <div className="hint">
          {t("risksPage.criticalSummary", {
            inherent: criticalCount(rows ?? [], "inherent"),
            residual: criticalCount(rows ?? [], "residual"),
          })}
          {totalLoss > 0 && ` · ${t("risksPage.totalAle", { amount: money(totalLoss) })}`}
        </div>
      </div>
      {library && library.length > 0 && (
        <div className="card" style={{ marginTop: 14 }}>
          <h3>{t("risksPage.library")}</h3>
          <div className="hint">{t("risksPage.librarySub", { count: library.length })}</div>
          <ul>
            {library.map((item) => (
              <li key={item.code}>
                <b>{item.code}</b> {item.title}
                {item.category && <span className="m"> · {item.category}</span>}
                <div className="id">{item.statement}</div>
                {item.suggestedControls && item.suggestedControls.length > 0 && (
                  <div className="id">
                    {t("risksPage.suggestedControls", {
                      codes: item.suggestedControls.join(", "),
                    })}
                  </div>
                )}
                {workspace?.canWrite &&
                  (item.added ? (
                    <span className="m">{t("risksPage.added")}</span>
                  ) : (
                    <button
                      className="btn"
                      type="button"
                      disabled={busy}
                      onClick={() => void adopt(item.code)}
                    >
                      {t("risksPage.adopt")}
                    </button>
                  ))}
              </li>
            ))}
          </ul>
        </div>
      )}
      <div className="card" style={{ marginTop: 14 }}>
        {!rows ? null : shown.length === 0 ? (
          <div className="empty">{t("risksPage.empty")}</div>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>{t("risksPage.titleField")}</th>
                  <th>{t("risksPage.score")}</th>
                  <th>{t("risksPage.residual")}</th>
                  <th>{t("risksPage.owner")}</th>
                  <th>{t("risksPage.treatment")}</th>
                  <th>{t("risksPage.review")}</th>
                  <th>{t("risksPage.ale")}</th>
                  <th>{t("risksPage.approval")}</th>
                </tr>
              </thead>
              <tbody>
                {shown.map((row) => (
                  <tr key={row.code}>
                    <td>
                      <button className="linkish" type="button" onClick={() => select(row)}>
                        <b>{row.code}</b> {row.title}
                      </button>
                      <div className="id">{row.controlCodes.join(" · ")}</div>
                    </td>
                    <td>
                      <Pill status={bandStatus(row.band)}>
                        {`${t(`risksPage.bands.${row.band}`)} ${row.score}`}
                      </Pill>
                    </td>
                    <td className="m">
                      {row.residualScore} · {t(`risksPage.bands.${row.residualBand}`)}
                    </td>
                    <td>{row.ownerName}</td>
                    <td>
                      {t(`risksPage.treatments.${row.treatment}`)}
                      {row.acceptanceExpiresOn && (
                        <div className="id">
                          {t("risksPage.acceptance.until", {
                            name: row.acceptedBy,
                            date: formatDate(row.acceptanceExpiresOn),
                          })}
                        </div>
                      )}
                    </td>
                    <td className="m">
                      {formatDate(row.reviewOn)}
                      {row.reviewCadence && (
                        <div className="id">{t(`risksPage.cadences.${row.reviewCadence}`)}</div>
                      )}
                    </td>
                    <td className="m">
                      {money(
                        row.annualizedLossExpectancy ??
                          annualizedLoss(row.singleLossExpectancy, row.annualRateOfOccurrence),
                      )}
                    </td>
                    <td>
                      <Pill status={row.approved ? "pass" : "warn"}>
                        {row.approved ? t("risksPage.approved") : t("risksPage.unapproved")}
                      </Pill>
                      {row.approvedBy && <div className="id">{row.approvedBy}</div>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
      {workspace?.canWrite && (
        <div className="card" style={{ marginTop: 14 }}>
          <form onSubmit={(event) => void save(event)}>
            <h3>{form.code || t("risksPage.new")}</h3>
            <div className="field">
              <label htmlFor="risk-title">{t("risksPage.titleField")}</label>
              <input
                id="risk-title"
                value={form.title}
                onChange={(event) => setForm({ ...form, title: event.target.value })}
                required
              />
            </div>
            <div className="field">
              <label htmlFor="risk-statement">{t("risksPage.statement")}</label>
              <textarea
                id="risk-statement"
                value={form.statement}
                onChange={(event) => setForm({ ...form, statement: event.target.value })}
              />
            </div>
            <div className="split-fields">
              <div className="field">
                <label htmlFor="risk-l">{t("risksPage.likelihood")}</label>
                <input
                  id="risk-l"
                  type="number"
                  min={1}
                  max={5}
                  value={form.likelihood}
                  onChange={(event) => setForm({ ...form, likelihood: Number(event.target.value) })}
                />
              </div>
              <div className="field">
                <label htmlFor="risk-i">{t("risksPage.impact")}</label>
                <input
                  id="risk-i"
                  type="number"
                  min={1}
                  max={5}
                  value={form.impact}
                  onChange={(event) => setForm({ ...form, impact: Number(event.target.value) })}
                />
              </div>
              <div className="field">
                <label htmlFor="risk-treatment">{t("risksPage.treatment")}</label>
                <select
                  id="risk-treatment"
                  value={form.treatment}
                  onChange={(event) => setForm({ ...form, treatment: event.target.value })}
                >
                  {treatments.map((treatment) => (
                    <option key={treatment} value={treatment}>
                      {t(`risksPage.treatments.${treatment}`)}
                    </option>
                  ))}
                </select>
              </div>
              <div className="field">
                <label htmlFor="risk-review">{t("risksPage.review")}</label>
                <input
                  id="risk-review"
                  type="date"
                  value={form.reviewOn}
                  onChange={(event) => setForm({ ...form, reviewOn: event.target.value })}
                />
              </div>
            </div>
            <div className="field">
              <label htmlFor="risk-plan">{t("risksPage.plan")}</label>
              <textarea
                id="risk-plan"
                value={form.treatmentPlan}
                onChange={(event) => setForm({ ...form, treatmentPlan: event.target.value })}
              />
            </div>
            <div className="split-fields">
              <div className="field">
                <label htmlFor="risk-owner">{t("risksPage.owner")}</label>
                <input
                  id="risk-owner"
                  value={form.ownerName}
                  onChange={(event) => setForm({ ...form, ownerName: event.target.value })}
                  required
                />
              </div>
              <div className="field">
                <label htmlFor="risk-email">{t("login.email")}</label>
                <input
                  id="risk-email"
                  type="email"
                  value={form.ownerEmail}
                  onChange={(event) => setForm({ ...form, ownerEmail: event.target.value })}
                  required
                />
              </div>
              <div className="field">
                <label htmlFor="risk-controls">{t("risksPage.controls")}</label>
                <input
                  id="risk-controls"
                  value={form.controlCodes}
                  onChange={(event) => setForm({ ...form, controlCodes: event.target.value })}
                />
              </div>
            </div>
            <div className="split-fields">
              <div className="field">
                <label htmlFor="risk-cadence">{t("risksPage.cadence")}</label>
                <select
                  id="risk-cadence"
                  value={form.reviewCadence}
                  onChange={(event) => setForm({ ...form, reviewCadence: event.target.value })}
                >
                  {cadences.map((cadence) => (
                    <option key={cadence} value={cadence}>
                      {t(`risksPage.cadences.${cadence}`)}
                    </option>
                  ))}
                </select>
              </div>
              <div className="field">
                <label htmlFor="risk-rl">{t("risksPage.residualLikelihood")}</label>
                <input
                  id="risk-rl"
                  type="number"
                  min={1}
                  max={5}
                  value={form.residualLikelihood}
                  onChange={(event) =>
                    setForm({ ...form, residualLikelihood: Number(event.target.value) })
                  }
                />
              </div>
              <div className="field">
                <label htmlFor="risk-ri">{t("risksPage.residualImpact")}</label>
                <input
                  id="risk-ri"
                  type="number"
                  min={1}
                  max={5}
                  value={form.residualImpact}
                  onChange={(event) =>
                    setForm({ ...form, residualImpact: Number(event.target.value) })
                  }
                />
              </div>
            </div>
            <div className="split-fields">
              <div className="field">
                <label htmlFor="risk-sle">{t("risksPage.sle")}</label>
                <input
                  id="risk-sle"
                  type="number"
                  min={0}
                  step="any"
                  value={form.singleLossExpectancy}
                  onChange={(event) =>
                    setForm({ ...form, singleLossExpectancy: event.target.value })
                  }
                />
              </div>
              <div className="field">
                <label htmlFor="risk-aro">{t("risksPage.aro")}</label>
                <input
                  id="risk-aro"
                  type="number"
                  min={0}
                  step="any"
                  value={form.annualRateOfOccurrence}
                  onChange={(event) =>
                    setForm({ ...form, annualRateOfOccurrence: event.target.value })
                  }
                />
              </div>
              <div className="field">
                <span>{t("risksPage.ale")}</span>
                <div className="m" aria-live="polite">
                  {money(formLoss)}
                </div>
              </div>
            </div>
            <div className="hint">{t("risksPage.quantifyHint")}</div>
            <label className="check-row">
              <input
                type="checkbox"
                checked={form.approved}
                onChange={(event) => setForm({ ...form, approved: event.target.checked })}
              />
              {t("risksPage.approve")}
            </label>
            <button className="btn primary" type="submit" disabled={busy}>
              {t("risksPage.save")}
            </button>
          </form>
          {form.code && (
            <form
              style={{ marginTop: 12 }}
              aria-label={t("risksPage.acceptance.title")}
              onSubmit={(event) => void acceptRisk(event)}
            >
              <h3>{t("risksPage.acceptance.title")}</h3>
              <div className="hint">{t("risksPage.acceptance.sub")}</div>
              <div className="split-fields">
                <div className="field">
                  <label htmlFor="accept-approver">{t("risksPage.acceptance.approver")}</label>
                  <input
                    id="accept-approver"
                    value={acceptance.approver}
                    placeholder={workspace?.displayName ?? ""}
                    onChange={(event) =>
                      setAcceptance({ ...acceptance, approver: event.target.value })
                    }
                  />
                </div>
                <div className="field">
                  <label htmlFor="accept-expires">{t("risksPage.acceptance.expires")}</label>
                  <input
                    id="accept-expires"
                    type="date"
                    value={acceptance.expiresOn}
                    min={inDays(1)}
                    max={inDays(365)}
                    onChange={(event) =>
                      setAcceptance({ ...acceptance, expiresOn: event.target.value })
                    }
                    required
                  />
                </div>
              </div>
              <div className="field">
                <label htmlFor="accept-rationale">{t("risksPage.acceptance.rationale")}</label>
                <textarea
                  id="accept-rationale"
                  value={acceptance.rationale}
                  onChange={(event) =>
                    setAcceptance({ ...acceptance, rationale: event.target.value })
                  }
                />
              </div>
              <button className="btn" type="submit" disabled={busy}>
                {t("risksPage.acceptance.submit")}
              </button>
            </form>
          )}
          {form.code && (
            <div style={{ marginTop: 12 }}>
              <AssignTask
                tenantId={tenantId}
                linkedKind="Risk"
                linkedCode={form.code}
                canWrite={Boolean(workspace?.canWrite)}
              />
            </div>
          )}
        </div>
      )}
    </>
  );
}
