import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Skeleton } from "./ui";

export function LoadingState({ rows = 2 }: { rows?: number }) {
  const { t } = useTranslation();
  return (
    <div className="stack" role="status" aria-busy="true" aria-label={t("states.loading")}>
      <Skeleton className="lg" />
      {Array.from({ length: rows }, (_, index) => (
        <Skeleton key={index} />
      ))}
    </div>
  );
}

export function EmptyState({ children, action }: { children: ReactNode; action?: ReactNode }) {
  return (
    <div className="empty state-empty">
      <span>{children}</span>
      {action}
    </div>
  );
}

export function ErrorState({ code, onRetry }: { code?: string | null; onRetry?: () => void }) {
  const { t } = useTranslation();
  const key =
    code === "tenant_forbidden" || code === "forbidden" ? "errors.forbidden" : "errors.load";
  return (
    <div className="error state-error" role="alert">
      <span>{t(key)}</span>
      {onRetry && (
        <button className="btn" type="button" onClick={onRetry}>
          {t("states.retry")}
        </button>
      )}
    </div>
  );
}
