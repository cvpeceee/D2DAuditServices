import { useTranslation } from "react-i18next";

export function Pager({
  page,
  pageSize,
  total,
  onPage,
}: {
  page: number;
  pageSize: number;
  total: number;
  onPage: (page: number) => void;
}) {
  const { t } = useTranslation();
  if (total === 0) return null;
  const pages = Math.max(1, Math.ceil(total / pageSize));
  const from = (page - 1) * pageSize + 1;
  const to = Math.min(total, page * pageSize);
  return (
    <div className="pager">
      <span className="m">{t("pager.range", { from, to, total })}</span>
      <div className="act">
        <button className="btn" type="button" disabled={page <= 1} onClick={() => onPage(page - 1)}>
          {t("pager.previous")}
        </button>
        <button
          className="btn"
          type="button"
          disabled={page >= pages}
          onClick={() => onPage(page + 1)}
        >
          {t("pager.next")}
        </button>
      </div>
    </div>
  );
}
