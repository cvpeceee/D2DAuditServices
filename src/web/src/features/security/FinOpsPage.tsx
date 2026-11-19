import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useOutletContext } from "react-router-dom";
import { ApiError, api, type Workspace } from "../../shared/api/client";
import { Pill } from "../../shared/ui/ui";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

type CostRec = {
  id: string;
  provider: string;
  resourceName: string;
  resourceType: string;
  region: string;
  category: string;
  monthlySavingsUsd: number;
  annualSavingsUsd: number;
  linkedControlCode: string;
  complianceImpact: string;
  remediationSummary: string;
  status: string;
  detectedAt: string;
  actionedAt: string | null;
};

type FinOpsBoard = {
  summary: {
    openMonthlyUsd: number;
    openAnnualUsd: number;
    realizedMonthlyUsd: number;
    realizedAnnualUsd: number;
    complianceLinkedCount: number;
    complianceLinkedMonthlyUsd: number;
  };
  recommendations: CostRec[];
};

function usd(amount: number) {
  return `$${Math.round(amount).toLocaleString()}`;
}

export function FinOpsPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [board, setBoard] = useState<FinOpsBoard | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  function load() {
    if (!tenantId) return;
    void api<FinOpsBoard>("/api/v1/security/finops", { tenantId })
      .then((value) => {
        setBoard(value);
        setError(null);
      })
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  async function setStatus(id: string, status: "Applied" | "Dismissed" | "Open") {
    if (!tenantId) return;
    try {
      await api(`/api/v1/security/finops/${id}/status`, {
        method: "POST",
        tenantId,
        body: { status },
      });
      setNotice(t("finopsPage.updatedNotice", { status }));
      load();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.code : "save");
    }
  }

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("finopsPage.title")}</h1>
          <div className="sub">{t("finopsPage.sub")}</div>
        </div>
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

      {board && (
        <div className="kpis">
          <div className="kpi">
            <span>{t("finopsPage.openMonthly")}</span>
            <b>{usd(board.summary.openMonthlyUsd)}/mo</b>
          </div>
          <div className="kpi">
            <span>{t("finopsPage.openAnnual")}</span>
            <b>{usd(board.summary.openAnnualUsd)}/yr</b>
          </div>
          <div className="kpi">
            <span>{t("finopsPage.complianceSavings")}</span>
            <b>
              {usd(board.summary.complianceLinkedMonthlyUsd)}/mo (
              {board.summary.complianceLinkedCount})
            </b>
          </div>
          <div className="kpi">
            <span>{t("finopsPage.realized")}</span>
            <b>{usd(board.summary.realizedAnnualUsd)}/yr</b>
          </div>
        </div>
      )}

      <div className="card">
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{t("finopsPage.resource")}</th>
                <th>{t("finopsPage.category")}</th>
                <th>{t("finopsPage.savings")}</th>
                <th>{t("finopsPage.complianceLink")}</th>
                <th>{t("finopsPage.status")}</th>
                {workspace?.canWrite && <th>{t("finopsPage.actions")}</th>}
              </tr>
            </thead>
            <tbody>
              {(board?.recommendations ?? []).map((row) => (
                <tr key={row.id}>
                  <td>
                    <b>{row.resourceName}</b>
                    <div className="id">
                      {row.provider.toUpperCase()} · {row.resourceType} · {row.region}
                    </div>
                    <div className="m">{row.remediationSummary}</div>
                  </td>
                  <td>
                    <span className="chip">{row.category}</span>
                  </td>
                  <td>
                    <b>{usd(row.monthlySavingsUsd)}/mo</b>
                    <div className="id">{usd(row.annualSavingsUsd)}/yr</div>
                  </td>
                  <td>
                    {row.linkedControlCode ? (
                      <Link to={`/controls/${encodeURIComponent(row.linkedControlCode)}`}>
                        {row.linkedControlCode}
                      </Link>
                    ) : (
                      <span className="m">—</span>
                    )}
                    <div className="id">{row.complianceImpact}</div>
                  </td>
                  <td>
                    <Pill
                      status={
                        row.status === "Applied"
                          ? "pass"
                          : row.status === "Dismissed"
                            ? "info"
                            : "warn"
                      }
                    >
                      {row.status}
                    </Pill>
                  </td>
                  {workspace?.canWrite && (
                    <td>
                      {row.status === "Open" ? (
                        <div style={{ display: "flex", gap: 6 }}>
                          <button
                            type="button"
                            className="btn primary"
                            onClick={() => void setStatus(row.id, "Applied")}
                          >
                            {t("finopsPage.apply")}
                          </button>
                          <button
                            type="button"
                            className="btn"
                            onClick={() => void setStatus(row.id, "Dismissed")}
                          >
                            {t("finopsPage.dismiss")}
                          </button>
                        </div>
                      ) : (
                        <button
                          type="button"
                          className="linkish"
                          onClick={() => void setStatus(row.id, "Open")}
                        >
                          {t("finopsPage.reopen")}
                        </button>
                      )}
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </>
  );
}
