import { useTranslation } from "react-i18next";
import { useLocation } from "react-router-dom";

export function PlaceholderPage() {
  const { t } = useTranslation();
  const location = useLocation();
  const key = location.pathname.replace(/^\//, "").split("/")[0] || "dashboard";
  const name = t(`nav.${key}`, { defaultValue: key });
  return (
    <div className="card">
      <h1>{t("placeholder.title")}</h1>
      <p className="sub">{t("placeholder.body", { name })}</p>
    </div>
  );
}
