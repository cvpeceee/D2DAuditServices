import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import {
  ApiError,
  api,
  type ControlDetail,
  type ControlListItem,
  type Workspace,
} from "../../shared/api/client";
import { useAuth } from "../auth/AuthContext";
import { frameworkTag, Pill, Skeleton } from "../../shared/ui/ui";
import { AssignTask } from "../program/AssignTask";

type Tab = "all" | "failing" | "needsEvidence" | "dueSoon" | "mine";

export function ControlsPage() {
  const { t } = useTranslation();
  const { tenantId, user } = useAuth();
  const { workspace } = useOutletContext<{ workspace: Workspace | null }>();
  const [tab, setTab] = useState<Tab>("all");
  const [query, setQuery] = useState("");
  const [items, setItems] = useState<ControlListItem[] | null>(null);
  const [allCount, setAllCount] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const [detail, setDetail] = useState<ControlDetail | null>(null);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [ownerName, setOwnerName] = useState("");
  const [ownerEmail, setOwnerEmail] = useState("");
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!tenantId) return;
    const params = new URLSearchParams();
    if (tab === "failing" || tab === "needsEvidence" || tab === "dueSoon")
      params.set("status", tab);
    if (tab === "mine" && user?.email) params.set("owner", user.email);
    if (query.trim()) params.set("q", query.trim());
    const suffix = params.size ? `?${params.toString()}` : "";
    let cancelled = false;
    setItems(null);
    setError(null);
    void api<ControlListItem[]>(`/api/v1/controls${suffix}`, { tenantId })
      .then((value) => {
        if (cancelled) return;
        setItems(value);
        if (tab === "all" && !query.trim()) setAllCount(value.length);
      })
      .catch((caught: unknown) => {
        if (!cancelled) setError(caught instanceof ApiError ? caught.code : "load");
      });
    return () => {
      cancelled = true;
    };
  }, [query, tab, tenantId, user?.email]);

  useEffect(() => {
    if (!tenantId || !selected) {
      setDetail(null);
      return;
    }
    let cancelled = false;
    setDetail(null);
    setDetailError(null);
    void api<ControlDetail>(`/api/v1/controls/${encodeURIComponent(selected)}`, { tenantId })
      .then((value) => {
        if (cancelled) return;
        setDetail(value);
        setOwnerName(value.ownerName ?? "");
        setOwnerEmail(value.ownerEmail ?? "");
      })
      .catch((caught: unknown) => {
        if (!cancelled) setDetailError(caught instanceof ApiError ? caught.code : "load");
      });
    return () => {
      cancelled = true;
    };
  }, [selected, tenantId]);

  async function saveOwner(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId || !detail || !workspace?.canWrite) return;
    setSaving(true);
    try {
      const updated = await api<ControlDetail>(
        `/api/v1/controls/${encodeURIComponent(detail.code)}/owner`,
        {
          method: "PUT",
          tenantId,
          body: { ownerName, ownerEmail },
        },
      );
      setDetail(updated);
      setItems(
        (current) =>
          current?.map((item) =>
            item.code === updated.code
              ? { ...item, ownerName: updated.ownerName, ownerEmail: updated.ownerEmail }
              : item,
          ) ?? current,
      );
    } catch (caught) {
      setDetailError(caught instanceof ApiError ? caught.code : "load");
    } finally {
      setSaving(false);
    }
  }

  const tabs: { id: Tab; label: string }[] = [
    { id: "all", label: t("controls.all", { count: allCount ?? "…" }) },
    { id: "failing", label: t("controls.failing") },
    { id: "needsEvidence", label: t("controls.needsEvidence") },
    { id: "dueSoon", label: t("controls.dueSoon") },
    { id: "mine", label: t("controls.mine") },
  ];

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("controls.title")}</h1>
          <div className="sub">{t("controls.sub")}</div>
        </div>
      </div>
      <div className="tabs" role="tablist">
        {tabs.map((item) => (
          <button
            key={item.id}
            type="button"
            role="tab"
            aria-selected={tab === item.id}
            className={tab === item.id ? "on" : ""}
            onClick={() => setTab(item.id)}
          >
            {item.label}
          </button>
        ))}
      </div>
      <div className="field" style={{ maxWidth: 320, marginTop: 0 }}>
        <label htmlFor="control-search" className="m">
          {t("controls.search")}
        </label>
        <input
          id="control-search"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
        />
      </div>
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}
      {!items && !error && <Skeleton className="lg" />}
      {items && (
        <div className="card" style={{ padding: "4px 8px", marginTop: 12 }}>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>{t("controls.id")}</th>
                  <th>{t("controls.control")}</th>
                  <th>{t("controls.mapped")}</th>
                  <th>{t("controls.tests")}</th>
                  <th>{t("controls.owner")}</th>
                  <th>{t("controls.status")}</th>
                </tr>
              </thead>
              <tbody>
                {items.length === 0 && (
                  <tr>
                    <td colSpan={6} className="empty">
                      {t("controls.empty")}
                    </td>
                  </tr>
                )}
                {items.map((item) => (
                  <tr key={item.code} className={`click ${selected === item.code ? "sel" : ""}`}>
                    <td className="id">{item.code}</td>
                    <td className="t">
                      <button
                        type="button"
                        className="row-hit"
                        aria-pressed={selected === item.code}
                        onClick={() => setSelected(item.code)}
                      >
                        {item.title}
                      </button>
                    </td>
                    <td>
                      {item.frameworks.map((key) => (
                        <span className="tag" key={key}>
                          {frameworkTag(key)}
                        </span>
                      ))}
                    </td>
                    <td className="m">
                      {item.testsTotal === 0 ? "—" : `${item.testsPassing}/${item.testsTotal}`}
                    </td>
                    <td className="m">{item.ownerName || "—"}</td>
                    <td>
                      <Pill status={item.status}>
                        {t(`status.${item.status}`, { defaultValue: item.status })}
                      </Pill>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
      {selected && (
        <aside className="drawer dock" aria-label={detail?.title ?? selected}>
          <div className="row">
            <span className="id">{selected}</span>
            {detail && (
              <Pill status={detail.status}>
                {t(`status.${detail.status}`, { defaultValue: detail.status })}
              </Pill>
            )}
            <button
              className="btn icon-btn"
              type="button"
              style={{ marginInlineStart: "auto" }}
              aria-label={t("controls.close")}
              onClick={() => setSelected(null)}
            >
              ✕
            </button>
          </div>
          {!detail && !detailError && <Skeleton className="lg" />}
          {detailError && (
            <div className="error" role="alert">
              {t(detailError === "not_found" ? "controls.empty" : "errors.load")}
            </div>
          )}
          {detail && (
            <>
              <div>
                <h1 style={{ fontSize: 19 }}>{detail.title}</h1>
                <div className="sub">{detail.description}</div>
              </div>
              <div className="kv">
                <div>
                  <small>{t("controls.owner")}</small>
                  {detail.ownerName || "—"}
                </div>
                <div>
                  <small>{t("controls.frequency")}</small>
                  {detail.frequency || "—"}
                </div>
                <div>
                  <small>{t("controls.lastTests")}</small>
                  {detail.tests.length}
                </div>
              </div>
              <div>
                <div className="sec" style={{ paddingInlineStart: 0 }}>
                  {t("controls.mappings")}
                </div>
                <div className="stack">
                  {detail.mappings.map((mapping) => (
                    <div className="row" key={`${mapping.frameworkKey}-${mapping.requirementCode}`}>
                      <span className="tag">{frameworkTag(mapping.frameworkKey)}</span>
                      <span className="t">{mapping.requirementCode}</span>
                      <span className="m">{mapping.requirementTitle}</span>
                      <span className="tag">
                        {mapping.coverage === "full"
                          ? t("frameworks.coverageFull")
                          : t("frameworks.coveragePartial")}
                      </span>
                    </div>
                  ))}
                </div>
              </div>
              <div>
                <div className="sec" style={{ paddingInlineStart: 0 }}>
                  {t("controls.automated")}
                </div>
                {detail.tests.length === 0 ? (
                  <div className="empty">{t("controls.empty")}</div>
                ) : (
                  detail.tests.map((test) => (
                    <div
                      className="row"
                      key={test.id}
                      style={{ padding: "8px 0", borderBottom: "1px solid var(--border)" }}
                    >
                      <span>
                        {test.name}
                        {test.detail ? <span className="m"> · {test.detail}</span> : null}
                      </span>
                      <span style={{ marginInlineStart: "auto" }}>
                        <Pill status={test.outcome}>
                          {t(`status.${test.outcome}`, { defaultValue: test.outcome })}
                        </Pill>
                      </span>
                    </div>
                  ))
                )}
              </div>
              <div>
                <div className="sec" style={{ paddingInlineStart: 0 }}>
                  {t("controls.evidence")}
                </div>
                <div className="m">{t("controls.evidenceLater")}</div>
                {detail.evidence.map((item) => (
                  <div className="row" key={item.id} style={{ marginTop: 8 }}>
                    <span className="t">{item.title}</span>
                    <span className="m">{item.source}</span>
                    {item.sha256 ? (
                      <span className="m">
                        sha256 {item.sha256.slice(0, 8)}…{item.sha256.slice(-4)}
                      </span>
                    ) : null}
                  </div>
                ))}
              </div>
              {workspace?.canWrite && (
                <form onSubmit={(event) => void saveOwner(event)} className="stack">
                  <label className="field" style={{ marginTop: 0 }}>
                    <span>{t("controls.ownerName")}</span>
                    <input
                      value={ownerName}
                      onChange={(event) => setOwnerName(event.target.value)}
                    />
                  </label>
                  <label className="field" style={{ marginTop: 0 }}>
                    <span>{t("controls.ownerEmail")}</span>
                    <input
                      type="email"
                      value={ownerEmail}
                      onChange={(event) => setOwnerEmail(event.target.value)}
                    />
                  </label>
                  <button
                    className="btn primary"
                    type="submit"
                    disabled={saving}
                    aria-busy={saving}
                  >
                    {t("controls.saveOwner")}
                  </button>
                  {saving && (
                    <div className="busy" role="progressbar">
                      <i />
                    </div>
                  )}
                </form>
              )}
              <AssignTask
                tenantId={tenantId}
                linkedKind="Control"
                linkedCode={selected}
                canWrite={Boolean(workspace?.canWrite)}
              />
            </>
          )}
        </aside>
      )}
    </>
  );
}
