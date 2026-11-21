import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext, useSearchParams } from "react-router-dom";
import { ApiError, api } from "../../shared/api/client";
import { formatDate, Pill, safeHref, Skeleton } from "../../shared/ui/ui";
import type {
  BillingOverview,
  ChangePlanResult,
  GateDecision,
  PlanEntitlements,
} from "../onboarding/types";
import { UpgradePrompt } from "./UpgradePrompt";

type Context = { workspace?: { role?: string } | null; tenantId: string | null };

export function usagePercent(decision: GateDecision) {
  if (decision.limit === null || decision.limit === 0) return 0;
  return Math.min(100, Math.round((100 * decision.used) / decision.limit));
}

function usd(value: number) {
  return new Intl.NumberFormat(undefined, {
    style: "currency",
    currency: "USD",
    maximumFractionDigits: 0,
  }).format(value);
}

export function BillingPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<Context>();
  const [params] = useSearchParams();
  const [data, setData] = useState<BillingOverview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const isAdmin = workspace?.role === "Admin";
  const checkout = params.get("checkout");

  const load = useCallback(() => {
    if (!tenantId) return;
    void api<BillingOverview>("/api/v1/billing", { tenantId })
      .then(setData)
      .catch((caught) => setError(caught instanceof ApiError ? caught.code : "load"));
  }, [tenantId]);

  useEffect(load, [load]);

  async function change(plan: string) {
    if (!tenantId) return;
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const result = await api<ChangePlanResult>("/api/v1/billing/plan", {
        method: "POST",
        tenantId,
        body: { plan },
      });
      const href = safeHref(result.redirectUrl);
      if (href) {
        window.location.assign(href);
        return;
      }
      setNotice(t("billingPage.changed", { plan: result.plan }));
      load();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  async function portal() {
    if (!tenantId) return;
    setBusy(true);
    setError(null);
    try {
      const result = await api<{ url: string }>("/api/v1/billing/portal", {
        method: "POST",
        tenantId,
        body: {},
      });
      const href = safeHref(result.url);
      if (href) window.location.assign(href);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setBusy(false);
    }
  }

  function limitText(value: number | null) {
    return value === null ? t("billingPage.unlimited") : String(value);
  }

  function priceText(plan: PlanEntitlements) {
    return plan.monthlyPriceUsd === null
      ? t("billingPage.contactSales")
      : t("billingPage.perMonth", { price: usd(plan.monthlyPriceUsd) });
  }

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("billingPage.title")}</h1>
          <div className="sub">{t("billingPage.sub")}</div>
        </div>
        {isAdmin && data?.providerConfigured && (
          <div className="act">
            <button className="btn" type="button" disabled={busy} onClick={() => void portal()}>
              {t("billingPage.portal")}
            </button>
          </div>
        )}
      </div>
      {checkout === "success" && (
        <div className="notice" role="status">
          {t("billingPage.checkoutSuccess")}
        </div>
      )}
      {checkout === "cancelled" && (
        <div className="notice" role="status">
          {t("billingPage.checkoutCancelled")}
        </div>
      )}
      {notice && (
        <div className="notice" role="status">
          {notice}
        </div>
      )}
      {error && (
        <div className="error" role="alert">
          {t(`billingPage.errors.${error}`, { defaultValue: t("errors.load") })}
        </div>
      )}
      {!data ? (
        !error && <Skeleton className="lg" />
      ) : (
        <div className="grid">
          <section className="card" aria-labelledby="billing-current">
            <h3 id="billing-current">
              {t("billingPage.current")}
              <span className="r">
                {data.provider === "mock" || !data.providerConfigured
                  ? t("billingPage.mockProvider")
                  : t("billingPage.provider", { provider: data.provider })}
              </span>
            </h3>
            <div className="row">
              <b style={{ fontSize: 20 }}>{t(`billingPage.plans.${data.plan}`)}</b>
              <Pill
                status={data.status === "Active" || data.status === "Trialing" ? "pass" : "warn"}
              >
                {t(`billingPage.statuses.${data.status}`, { defaultValue: data.status })}
              </Pill>
              <span className="id">{priceText(data.entitlements)}</span>
            </div>
            {data.currentPeriodEnd && (
              <div className="hint">
                {t("billingPage.renews", { date: formatDate(data.currentPeriodEnd) })}
              </div>
            )}
          </section>

          <section className="card stack" aria-labelledby="billing-usage">
            <h3 id="billing-usage">{t("billingPage.usage")}</h3>
            {data.usage.map((item) => (
              <div key={item.feature} className="stack" style={{ gap: 4 }}>
                <div className="row">
                  <span>{t(`billingPage.features.${item.feature}`)}</span>
                  <span className="id" style={{ marginInlineStart: "auto" }}>
                    {t("billingPage.usedOf", { used: item.used, limit: limitText(item.limit) })}
                  </span>
                </div>
                <div
                  className="bar"
                  role="meter"
                  aria-label={t(`billingPage.features.${item.feature}`)}
                  aria-valuenow={item.used}
                  aria-valuemin={0}
                  aria-valuemax={item.limit ?? item.used}
                >
                  <i style={{ width: `${usagePercent(item)}%` }} />
                </div>
                <UpgradePrompt decision={item} />
              </div>
            ))}
            <div className="row">
              <span>{t("billingPage.features.trust_center")}</span>
              <Pill status={data.entitlements.trustCenter ? "pass" : "neutral"}>
                {data.entitlements.trustCenter
                  ? t("billingPage.included")
                  : t("billingPage.notIncluded")}
              </Pill>
              <span>{t("billingPage.features.api_access")}</span>
              <Pill status={data.entitlements.apiAccess ? "pass" : "neutral"}>
                {data.entitlements.apiAccess
                  ? t("billingPage.included")
                  : t("billingPage.notIncluded")}
              </Pill>
            </div>
          </section>

          <section className="card" aria-labelledby="billing-plans">
            <h3 id="billing-plans">{t("billingPage.changePlan")}</h3>
            <div className="cardgrid">
              {data.plans.map((plan) => (
                <div key={plan.plan} className="card stack">
                  <b>{t(`billingPage.plans.${plan.plan}`)}</b>
                  <span>{priceText(plan)}</span>
                  <ul>
                    <li>
                      {t("billingPage.limits.frameworks", { value: limitText(plan.frameworks) })}
                    </li>
                    <li>{t("billingPage.limits.seats", { value: limitText(plan.seats) })}</li>
                    <li>
                      {t("billingPage.limits.integrations", {
                        value: limitText(plan.integrations),
                      })}
                    </li>
                    <li>
                      {plan.trustCenter
                        ? t("billingPage.limits.trustCenter")
                        : t("billingPage.limits.noTrustCenter")}
                    </li>
                    <li>
                      {plan.apiAccess ? t("billingPage.limits.api") : t("billingPage.limits.noApi")}
                    </li>
                  </ul>
                  {plan.plan === data.plan ? (
                    <Pill status="info">{t("billingPage.currentPlan")}</Pill>
                  ) : (
                    isAdmin && (
                      <button
                        className="btn primary"
                        type="button"
                        disabled={busy}
                        onClick={() => void change(plan.plan)}
                      >
                        {t("billingPage.switchTo", { plan: t(`billingPage.plans.${plan.plan}`) })}
                      </button>
                    )
                  )}
                </div>
              ))}
            </div>
            {!isAdmin && <div className="hint">{t("billingPage.adminOnly")}</div>}
          </section>

          <section className="card" aria-labelledby="billing-invoices">
            <h3 id="billing-invoices">{t("billingPage.invoices")}</h3>
            {data.invoices.length === 0 ? (
              <div className="empty">{t("billingPage.noInvoices")}</div>
            ) : (
              <table>
                <thead>
                  <tr>
                    <th>{t("billingPage.invoice")}</th>
                    <th>{t("billingPage.issued")}</th>
                    <th>{t("billingPage.amount")}</th>
                    <th>{t("billingPage.status")}</th>
                  </tr>
                </thead>
                <tbody>
                  {data.invoices.map((invoice) => {
                    const href = safeHref(invoice.url);
                    return (
                      <tr key={invoice.id}>
                        <td>
                          {href ? (
                            <a href={href} target="_blank" rel="noreferrer">
                              {invoice.number}
                            </a>
                          ) : (
                            invoice.number
                          )}
                        </td>
                        <td>{formatDate(invoice.issuedOn)}</td>
                        <td>{usd(invoice.amountUsd)}</td>
                        <td>
                          <Pill status={invoice.status === "paid" ? "pass" : "info"}>
                            {t(`billingPage.invoiceStatuses.${invoice.status}`, {
                              defaultValue: invoice.status,
                            })}
                          </Pill>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            )}
          </section>
        </div>
      )}
    </>
  );
}
