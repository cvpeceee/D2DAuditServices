import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useParams } from "react-router-dom";
import { ApiError, api, type FrameworkDetail, type RequirementNode } from "../../shared/api/client";
import { useAuth } from "../auth/AuthContext";
import { Pill, Skeleton } from "../../shared/ui/ui";

function Tree({ node }: { node: RequirementNode }) {
  const { t } = useTranslation();
  return (
    <div className="req">
      <div className="t">
        {node.code} · {node.title}
      </div>
      {node.summary && <div className="m">{node.summary}</div>}
      {node.controls.length === 0 && node.children.length === 0 && (
        <div className="m">{t("frameworks.noControls")}</div>
      )}
      {node.controls.map((control) => (
        <div className="row" key={control.code} style={{ marginTop: 6 }}>
          <Link to="/controls">{control.code}</Link>
          <span>{control.title}</span>
          <span className="tag">
            {control.coverage === "full"
              ? t("frameworks.coverageFull")
              : t("frameworks.coveragePartial")}
          </span>
          <Pill status={control.status}>
            {t(`status.${control.status}`, { defaultValue: control.status })}
          </Pill>
        </div>
      ))}
      {node.children.map((child) => (
        <Tree key={child.code} node={child} />
      ))}
    </div>
  );
}

export function FrameworkDetailPage() {
  const { t } = useTranslation();
  const { key = "" } = useParams();
  const { tenantId } = useAuth();
  const [detail, setDetail] = useState<FrameworkDetail | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!tenantId || !key) return;
    let cancelled = false;
    setDetail(null);
    setError(null);
    void api<FrameworkDetail>(`/api/v1/frameworks/${encodeURIComponent(key)}`, { tenantId })
      .then((value) => {
        if (!cancelled) setDetail(value);
      })
      .catch((caught: unknown) => {
        if (!cancelled) setError(caught instanceof ApiError ? caught.code : "load");
      });
    return () => {
      cancelled = true;
    };
  }, [key, tenantId]);

  return (
    <>
      <div className="head">
        <div>
          <Link to="/frameworks">{t("frameworks.back")}</Link>
          <h1>{detail?.name ?? t("frameworks.title")}</h1>
          <div className="sub">{detail ? `${detail.version} · ${detail.description}` : ""}</div>
        </div>
      </div>
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}
      {!detail && !error && <Skeleton className="lg" />}
      <div className="card">
        {detail?.requirements.map((node) => (
          <Tree key={node.code} node={node} />
        ))}
      </div>
    </>
  );
}
