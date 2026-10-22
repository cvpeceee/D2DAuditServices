import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import {
  ApiError,
  api,
  type AutomatedTest,
  type Job,
  type Workspace,
} from "../../shared/api/client";
import { Pill } from "../../shared/ui/ui";
import { AssignTask } from "../program/AssignTask";

type ShellContext = { workspace: Workspace | null; tenantId: string | null; jobs: Job[] };

export function TestsPage() {
  const { t } = useTranslation();
  const { tenantId, workspace } = useOutletContext<ShellContext>();
  const [rows, setRows] = useState<AutomatedTest[] | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!tenantId) return;
    let cancelled = false;
    void api<AutomatedTest[]>("/api/v1/tests", { tenantId })
      .then((value) => {
        if (!cancelled) setRows(value);
      })
      .catch((caught: unknown) => {
        if (!cancelled) setError(caught instanceof ApiError ? caught.code : "load");
      });
    return () => {
      cancelled = true;
    };
  }, [tenantId]);

  if (error)
    return (
      <div className="error" role="alert">
        {t("errors.load")}
      </div>
    );
  if (!rows) return <div className="skeleton lg" />;

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("testsPage.title")}</h1>
          <div className="sub">{t("testsPage.sub")}</div>
        </div>
      </div>
      {rows.length === 0 ? (
        <div className="empty">{t("testsPage.empty")}</div>
      ) : (
        <div className="card" style={{ padding: "4px 8px" }}>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>{t("testsPage.name")}</th>
                  <th>{t("testsPage.control")}</th>
                  <th>{t("testsPage.requirements")}</th>
                  <th>{t("testsPage.outcome")}</th>
                  <th>{t("testsPage.detail")}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.id} className={selected === row.name ? "sel" : ""}>
                    <td>
                      <button
                        className="linkish"
                        type="button"
                        onClick={() => setSelected(row.name)}
                      >
                        <div className="t">{row.name}</div>
                      </button>
                      {row.remediation && <div className="m">{row.remediation}</div>}
                    </td>
                    <td className="id">{row.controlCode}</td>
                    <td>
                      {row.requirements.map((requirement) => (
                        <span className="tag" key={requirement}>
                          {requirement}
                        </span>
                      ))}
                    </td>
                    <td>
                      <Pill status={row.outcome}>
                        {t(`status.${row.outcome}`, { defaultValue: row.outcome })}
                      </Pill>
                    </td>
                    <td className="m">{row.detail}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
      {selected && (
        <div className="card" style={{ marginTop: 14 }}>
          <h3>{selected}</h3>
          <AssignTask
            tenantId={tenantId}
            linkedKind="Test"
            linkedCode={selected}
            canWrite={Boolean(workspace?.canWrite)}
          />
        </div>
      )}
    </>
  );
}
