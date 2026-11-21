import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";

const sectionKeys: Record<string, string> = {
  "trust-center": "trust",
  "attack-paths": "attackPaths",
  "governance-hub": "governanceHub",
  "access-reviews": "accessReviews",
  "system-description": "description",
};

export type Crumb = { label: string; to?: string };

export function crumbsFor(
  pathname: string,
  label: (key: string, fallback: string) => string,
): Crumb[] {
  const parts = pathname.split("/").filter(Boolean).map(decodeURIComponent);
  if (parts.length === 0) return [{ label: label("dashboard", "Dashboard") }];
  const crumbs: Crumb[] = [];
  const first = parts[0];
  const firstKey = sectionKeys[first] ?? first;
  crumbs.push({ label: label(firstKey, first), to: `/${parts[0]}` });
  if (first === "people" && parts[1] === "computers") {
    crumbs.push({ label: label("computers", "computers"), to: "/people/computers" });
  } else if (first === "people" && parts[1] === "lifecycle") {
    crumbs.push({ label: label("lifecycle", "lifecycle"), to: "/people/lifecycle" });
  } else if (first === "settings" && parts[1] === "billing") {
    crumbs.push({ label: label("billing", "billing"), to: "/settings/billing" });
  } else if (parts[1]) {
    crumbs.push({ label: parts[1], to: `/${parts[0]}/${encodeURIComponent(parts[1])}` });
  }
  const last = crumbs[crumbs.length - 1];
  crumbs[crumbs.length - 1] = { label: last.label };
  return crumbs;
}

export function Breadcrumbs({ tenant, pathname }: { tenant?: string; pathname: string }) {
  const { t } = useTranslation();
  const crumbs = crumbsFor(pathname, (key, fallback) =>
    t(`nav.${key}`, { defaultValue: fallback }),
  );
  return (
    <nav className="crumb" aria-label={t("shell.breadcrumb")}>
      <ol className="crumbs">
        {tenant && (
          <li>
            <Link to="/">{tenant}</Link>
          </li>
        )}
        {crumbs.map((crumb, index) => (
          <li key={`${crumb.label}-${index}`}>
            {crumb.to ? (
              <Link to={crumb.to}>{crumb.label}</Link>
            ) : (
              <b aria-current="page">{crumb.label}</b>
            )}
          </li>
        ))}
      </ol>
    </nav>
  );
}
