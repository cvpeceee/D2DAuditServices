import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { ApiError, api, type FrameworkListItem } from "../../shared/api/client";
import { useAuth } from "../auth/AuthContext";
import { Ring, Skeleton } from "../../shared/ui/ui";

export function FrameworksPage() {
  const { t } = useTranslation();
  const { tenantId } = useAuth();
  const [items, setItems] = useState<FrameworkListItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!tenantId) return;
    let cancelled = false;
    void api<FrameworkListItem[]>("/api/v1/frameworks", { tenantId })
      .then((value) => {
        if (!cancelled) setItems(value);
      })
      .catch((caught: unknown) => {
        if (!cancelled) setError(caught instanceof ApiError ? caught.code : "load");
      });
    return () => {
      cancelled = true;
    };
  }, [tenantId]);

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("frameworks.title")}</h1>
          <div className="sub">{t("frameworks.sub")}</div>
        </div>
      </div>
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
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
