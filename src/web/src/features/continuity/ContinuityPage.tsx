import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import { ApiError, api, type Workspace } from "../../shared/api/client";
import { formatWhen, Pill } from "../../shared/ui/ui";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

type Board = {
  services: {
    id: string;
    name: string;
    criticality: string;
    rtoHours: number;
    rpoHours: number;
    ownerName: string;
    controlCode: string;
    notes: string;
  }[];
  plans: {
    id: string;
    name: string;
    kind: string;
    body: string;
    controlCode: string;
    updatedAt: string;
  }[];
  exercises: {
    id: string;
    name: string;
    planId: string | null;
    result: string;
    notes: string;
    controlCode: string;
    ranAt: string;
  }[];
};

const criticalities = ["Critical", "High", "Medium", "Low"] as const;
const kinds = ["Continuity", "DisasterRecovery"] as const;

export function ContinuityPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [board, setBoard] = useState<Board | null>(null);
  const [service, setService] = useState({
    name: "",
    criticality: "High",
    rtoHours: 8,
    rpoHours: 4,
    ownerName: "",
    controlCode: "BC-01",
    notes: "",
  });
  const [plan, setPlan] = useState({
    name: "",
    kind: "Continuity",
    body: "",
    controlCode: "BC-01",
  });
  const [exercise, setExercise] = useState({
    name: "",
    planId: "",
    result: "Pass",
    notes: "",
    controlCode: "BC-01",
  });
  const [error, setError] = useState<string | null>(null);

  function load() {
    if (!tenantId) return;
    void api<Board>("/api/v1/continuity", { tenantId })
      .then(setBoard)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  async function saveService(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId) return;
    try {
      await api("/api/v1/continuity/services", { method: "POST", tenantId, body: service });
      setService({ ...service, name: "", notes: "" });
      setError(null);
      load();
    } catch {
      setError("save");
    }
  }

  async function savePlan(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId) return;
    try {
      await api("/api/v1/continuity/plans", { method: "POST", tenantId, body: plan });
      setPlan({ ...plan, name: "", body: "" });
      setError(null);
      load();
    } catch {
      setError("save");
    }
  }

  async function saveExercise(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId) return;
    try {
      await api("/api/v1/continuity/exercises", {
        method: "POST",
        tenantId,
        body: { ...exercise, planId: exercise.planId || null },
      });
      setExercise({ ...exercise, name: "", notes: "" });
      setError(null);
      load();
    } catch {
      setError("save");
    }
  }

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("continuityPage.title")}</h1>
          <div className="sub">{t("continuityPage.sub")}</div>
        </div>
      </div>
      {error && (
        <div className="error" role="alert">
          {t(error === "save" ? "errors.save" : "errors.load")}
        </div>
      )}
      <h2>{t("continuityPage.services")}</h2>
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>{t("continuityPage.service")}</th>
              <th>{t("continuityPage.criticality")}</th>
              <th>{t("continuityPage.rto")}</th>
              <th>{t("continuityPage.rpo")}</th>
              <th>{t("continuityPage.owner")}</th>
              <th>{t("continuityPage.control")}</th>
            </tr>
          </thead>
          <tbody>
            {board?.services.map((row) => (
              <tr key={row.id}>
                <td>
                  <b>{row.name}</b>
                  <div>{row.notes}</div>
                </td>
                <td>
                  <Pill
                    status={
                      row.criticality === "Critical" || row.criticality === "High" ? "fail" : "warn"
                    }
                  >
                    {t(`continuityPage.criticalities.${row.criticality}`)}
                  </Pill>
                </td>
                <td>{t("continuityPage.hours", { count: row.rtoHours })}</td>
                <td>{t("continuityPage.hours", { count: row.rpoHours })}</td>
                <td>{row.ownerName}</td>
                <td>{row.controlCode}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <h2>{t("continuityPage.plans")}</h2>
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>{t("continuityPage.plan")}</th>
              <th>{t("continuityPage.kind")}</th>
              <th>{t("continuityPage.control")}</th>
            </tr>
          </thead>
          <tbody>
            {board?.plans.map((row) => (
              <tr key={row.id}>
                <td>
                  <b>{row.name}</b>
                  <div>{row.body}</div>
                  <div>{formatWhen(row.updatedAt)}</div>
                </td>
                <td>{t(`continuityPage.kinds.${row.kind}`)}</td>
                <td>{row.controlCode}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <h2>{t("continuityPage.exercises")}</h2>
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>{t("continuityPage.exercise")}</th>
              <th>{t("continuityPage.result")}</th>
              <th>{t("continuityPage.ran")}</th>
            </tr>
          </thead>
          <tbody>
            {board?.exercises.map((row) => (
              <tr key={row.id}>
                <td>
                  <b>{row.name}</b>
                  <div>{row.notes}</div>
                </td>
                <td>
                  <Pill status={row.result === "Pass" ? "pass" : "fail"}>
                    {t(`continuityPage.results.${row.result}`)}
                  </Pill>
                </td>
                <td>{formatWhen(row.ranAt)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {workspace?.canWrite && (
        <>
          <form className="card" onSubmit={(event) => void saveService(event)}>
            <h3>{t("continuityPage.addService")}</h3>
            <label className="field">
              <span>{t("continuityPage.service")}</span>
              <input
                aria-label={t("continuityPage.service")}
                value={service.name}
                onChange={(event) => setService({ ...service, name: event.target.value })}
              />
            </label>
            <label className="field">
              <span>{t("continuityPage.criticality")}</span>
              <select
                aria-label={t("continuityPage.criticality")}
                value={service.criticality}
                onChange={(event) => setService({ ...service, criticality: event.target.value })}
              >
                {criticalities.map((item) => (
                  <option key={item} value={item}>
                    {t(`continuityPage.criticalities.${item}`)}
                  </option>
                ))}
              </select>
            </label>
            <button className="btn primary" type="submit">
              {t("continuityPage.addService")}
            </button>
          </form>
          <form className="card" onSubmit={(event) => void savePlan(event)}>
            <h3>{t("continuityPage.addPlan")}</h3>
            <label className="field">
              <span>{t("continuityPage.plan")}</span>
              <input
                aria-label={t("continuityPage.plan")}
                value={plan.name}
                onChange={(event) => setPlan({ ...plan, name: event.target.value })}
              />
            </label>
            <label className="field">
              <span>{t("continuityPage.kind")}</span>
              <select
                aria-label={t("continuityPage.kind")}
                value={plan.kind}
                onChange={(event) => setPlan({ ...plan, kind: event.target.value })}
              >
                {kinds.map((item) => (
                  <option key={item} value={item}>
                    {t(`continuityPage.kinds.${item}`)}
                  </option>
                ))}
              </select>
            </label>
            <label className="field">
              <span>{t("continuityPage.body")}</span>
              <textarea
                aria-label={t("continuityPage.body")}
                value={plan.body}
                onChange={(event) => setPlan({ ...plan, body: event.target.value })}
              />
            </label>
            <button className="btn primary" type="submit">
              {t("continuityPage.addPlan")}
            </button>
          </form>
          <form className="card" onSubmit={(event) => void saveExercise(event)}>
            <h3>{t("continuityPage.addExercise")}</h3>
            <label className="field">
              <span>{t("continuityPage.exercise")}</span>
              <input
                aria-label={t("continuityPage.exercise")}
                value={exercise.name}
                onChange={(event) => setExercise({ ...exercise, name: event.target.value })}
              />
            </label>
            <label className="field">
              <span>{t("continuityPage.plan")}</span>
              <select
                aria-label={t("continuityPage.plan")}
                value={exercise.planId}
                onChange={(event) => setExercise({ ...exercise, planId: event.target.value })}
              >
                <option value="">{t("continuityPage.noPlan")}</option>
                {board?.plans.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.name}
                  </option>
                ))}
              </select>
            </label>
            <label className="field">
              <span>{t("continuityPage.result")}</span>
              <select
                aria-label={t("continuityPage.result")}
                value={exercise.result}
                onChange={(event) => setExercise({ ...exercise, result: event.target.value })}
              >
                <option value="Pass">{t("continuityPage.results.Pass")}</option>
                <option value="Fail">{t("continuityPage.results.Fail")}</option>
              </select>
            </label>
            <label className="field">
              <span>{t("continuityPage.notes")}</span>
              <input
                aria-label={t("continuityPage.notes")}
                value={exercise.notes}
                onChange={(event) => setExercise({ ...exercise, notes: event.target.value })}
              />
            </label>
            <button className="btn primary" type="submit">
              {t("continuityPage.addExercise")}
            </button>
          </form>
        </>
      )}
    </>
  );
}
