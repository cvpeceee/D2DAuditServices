import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useOutletContext } from "react-router-dom";
import { ApiError, api, type Workspace } from "../../shared/api/client";
import { Pill } from "../../shared/ui/ui";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

type AttackHop = {
  stage: string;
  kind: string;
  label: string;
  detail: string;
  href: string;
  severity: string;
};

type AttackPath = {
  id: string;
  title: string;
  blastRadiusScore: number;
  severity: string;
  hops: AttackHop[];
  controlCodes: string[];
  chokepointRemediation: string;
};

type AttackPathsBoard = {
  totalPaths: number;
  criticalPaths: number;
  highestBlastRadius: number;
  paths: AttackPath[];
};

function tone(severity: string) {
  if (severity === "Critical" || severity === "High") return "fail";
  if (severity === "Medium") return "warn";
  return "info";
}

export function AttackPathsPage() {
  const { t } = useTranslation();
  const { tenantId } = useOutletContext<ShellContext>();
  const [board, setBoard] = useState<AttackPathsBoard | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!tenantId) return;
    void api<AttackPathsBoard>("/api/v1/security/attack-paths", { tenantId })
      .then((value) => {
        setBoard(value);
        setError(null);
      })
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  }, [tenantId]);

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("attackPathsPage.title")}</h1>
          <div className="sub">{t("attackPathsPage.sub")}</div>
        </div>
      </div>

      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}

      {board && (
        <div className="kpis">
          <div className="kpi">
            <span>{t("attackPathsPage.totalPaths")}</span>
            <b>{board.totalPaths}</b>
          </div>
          <div className="kpi">
            <span>{t("attackPathsPage.criticalPaths")}</span>
            <b>{board.criticalPaths}</b>
          </div>
          <div className="kpi">
            <span>{t("attackPathsPage.highestBlast")}</span>
            <b>{board.highestBlastRadius} / 100</b>
          </div>
        </div>
      )}

      {(board?.paths ?? []).length === 0 && !error && (
        <div className="card">
          <p className="m">{t("attackPathsPage.empty")}</p>
        </div>
      )}

      {(board?.paths ?? []).map((path) => (
        <section className="card" key={path.id}>
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              flexWrap: "wrap",
              gap: 8,
            }}
          >
            <div>
              <b>
                {path.id} · {path.title}
              </b>
              <div className="m" style={{ marginTop: 4 }}>
                {t("attackPathsPage.chokepoint")}: {path.chokepointRemediation}
              </div>
            </div>
            <Pill status={tone(path.severity)}>
              {t("attackPathsPage.blastScore", { score: path.blastRadiusScore })}
            </Pill>
          </div>

          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
              gap: 12,
              marginTop: 14,
            }}
          >
            {path.hops.map((hop, idx) => (
              <Link
                key={`${path.id}-${idx}-${hop.stage}`}
                to={hop.href}
                style={{
                  display: "block",
                  padding: 12,
                  borderRadius: 10,
                  border: "1px solid var(--line)",
                  textDecoration: "none",
                  color: "inherit",
                }}
              >
                <div
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    alignItems: "center",
                    marginBottom: 6,
                  }}
                >
                  <span className="id">
                    {idx + 1}. {hop.stage} · {hop.kind}
                  </span>
                  <Pill status={tone(hop.severity)}>{hop.severity}</Pill>
                </div>
                <b style={{ display: "block", marginBottom: 4 }}>{hop.label}</b>
                <div className="m">{hop.detail}</div>
              </Link>
            ))}
          </div>

          <div style={{ display: "flex", gap: 6, flexWrap: "wrap", marginTop: 12 }}>
            <span className="id">{t("attackPathsPage.controlsImpacted")}:</span>
            {path.controlCodes.map((code) => (
              <Link key={code} className="chip" to={`/controls/${encodeURIComponent(code)}`}>
                {code}
              </Link>
            ))}
          </div>
        </section>
      ))}
    </>
  );
}
