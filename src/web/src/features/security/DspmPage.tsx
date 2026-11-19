import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useOutletContext } from "react-router-dom";
import { ApiError, api, type Workspace } from "../../shared/api/client";
import { formatWhen, Pill } from "../../shared/ui/ui";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

type DataStore = {
  id: string;
  storeName: string;
  provider: string;
  engine: string;
  region: string;
  classification: string;
  sensitiveDataTypes: string;
  recordCountEstimate: number;
  encrypted: boolean;
  publicAccess: boolean;
  retentionCompliant: boolean;
  controlCode: string;
  riskScore: number;
  riskLevel: string;
  ownerEmail: string;
  lastScannedAt: string;
};

type DspmBoard = {
  totalStores: number;
  restrictedStores: number;
  publicSensitiveStores: number;
  unencryptedStores: number;
  stores: DataStore[];
};

function tone(level: string) {
  if (level === "Critical" || level === "High") return "fail";
  if (level === "Medium") return "warn";
  return "pass";
}

export function DspmPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [board, setBoard] = useState<DspmBoard | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [form, setForm] = useState({
    storeName: "",
    provider: "aws",
    engine: "AWS S3",
    region: "us-east-1",
    classification: "Restricted",
    sensitiveDataTypes: "PII,Email",
    recordCountEstimate: 100000,
    encrypted: true,
    publicAccess: false,
    retentionCompliant: true,
    controlCode: "CR-02",
    ownerEmail: "",
  });

  function load() {
    if (!tenantId) return;
    void api<DspmBoard>("/api/v1/security/dspm", { tenantId })
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

  async function scanStores() {
    if (!tenantId) return;
    try {
      const updated = await api<DspmBoard>("/api/v1/security/dspm/scan", {
        method: "POST",
        tenantId,
      });
      setBoard(updated);
      setNotice(t("dspmPage.scanned", { count: updated.totalStores }));
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.code : "save");
    }
  }

  async function saveStore(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId) return;
    try {
      await api("/api/v1/security/dspm", {
        method: "POST",
        tenantId,
        body: form,
      });
      setForm({ ...form, storeName: "", ownerEmail: "" });
      setNotice(t("dspmPage.saved"));
      load();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.code : "save");
    }
  }

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("dspmPage.title")}</h1>
          <div className="sub">{t("dspmPage.sub")}</div>
        </div>
        {workspace?.canWrite && (
          <button className="btn primary" type="button" onClick={() => void scanStores()}>
            {t("dspmPage.scanNow")}
          </button>
        )}
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
            <span>{t("dspmPage.totalStores")}</span>
            <b>{board.totalStores}</b>
          </div>
          <div className="kpi">
            <span>{t("dspmPage.restrictedStores")}</span>
            <b>{board.restrictedStores}</b>
          </div>
          <div className="kpi">
            <span>{t("dspmPage.publicSensitive")}</span>
            <b>{board.publicSensitiveStores}</b>
          </div>
          <div className="kpi">
            <span>{t("dspmPage.unencrypted")}</span>
            <b>{board.unencryptedStores}</b>
          </div>
        </div>
      )}

      <div className="card">
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{t("dspmPage.store")}</th>
                <th>{t("dspmPage.classification")}</th>
                <th>{t("dspmPage.sensitiveData")}</th>
                <th>{t("dspmPage.posture")}</th>
                <th>{t("dspmPage.risk")}</th>
                <th>{t("dspmPage.control")}</th>
              </tr>
            </thead>
            <tbody>
              {(board?.stores ?? []).map((row) => (
                <tr key={row.id}>
                  <td>
                    <b>{row.storeName}</b>
                    <div className="id">
                      {row.provider.toUpperCase()} · {row.engine} · {row.region}
                    </div>
                  </td>
                  <td>
                    <Pill status={row.classification === "Restricted" ? "warn" : "info"}>
                      {row.classification}
                    </Pill>
                    <div className="id">~{row.recordCountEstimate.toLocaleString()} records</div>
                  </td>
                  <td>
                    <div style={{ display: "flex", flexWrap: "wrap", gap: 4 }}>
                      {row.sensitiveDataTypes
                        .split(",")
                        .filter(Boolean)
                        .map((tag) => (
                          <span key={tag} className="chip">
                            {tag.trim()}
                          </span>
                        ))}
                    </div>
                  </td>
                  <td>
                    <Pill status={row.encrypted ? "pass" : "fail"}>
                      {row.encrypted ? t("dspmPage.encrypted") : t("dspmPage.unencryptedBadge")}
                    </Pill>{" "}
                    <Pill status={row.publicAccess ? "fail" : "pass"}>
                      {row.publicAccess ? t("dspmPage.publicBadge") : t("dspmPage.privateBadge")}
                    </Pill>
                    <div className="id">
                      {row.retentionCompliant
                        ? t("dspmPage.retentionOk")
                        : t("dspmPage.retentionGap")}{" "}
                      · {formatWhen(row.lastScannedAt)}
                    </div>
                  </td>
                  <td>
                    <Pill
                      status={tone(row.riskLevel)}
                    >{`${row.riskLevel} (${row.riskScore})`}</Pill>
                  </td>
                  <td>
                    <Link to={`/controls/${encodeURIComponent(row.controlCode)}`}>
                      {row.controlCode}
                    </Link>
                    {row.ownerEmail && <div className="id">{row.ownerEmail}</div>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {workspace?.canWrite && (
        <form className="card" onSubmit={(event) => void saveStore(event)}>
          <h3>{t("dspmPage.registerTitle")}</h3>
          <div className="split-fields">
            <label className="field">
              <span>{t("dspmPage.storeName")}</span>
              <input
                value={form.storeName}
                onChange={(event) => setForm({ ...form, storeName: event.target.value })}
                required
              />
            </label>
            <label className="field">
              <span>{t("dspmPage.provider")}</span>
              <select
                value={form.provider}
                onChange={(event) => setForm({ ...form, provider: event.target.value })}
              >
                <option value="aws">AWS</option>
                <option value="azure">Azure</option>
                <option value="gcp">GCP</option>
                <option value="snowflake">Snowflake</option>
              </select>
            </label>
            <label className="field">
              <span>{t("dspmPage.engine")}</span>
              <input
                value={form.engine}
                onChange={(event) => setForm({ ...form, engine: event.target.value })}
                required
              />
            </label>
            <label className="field">
              <span>{t("dspmPage.region")}</span>
              <input
                value={form.region}
                onChange={(event) => setForm({ ...form, region: event.target.value })}
                required
              />
            </label>
            <label className="field">
              <span>{t("dspmPage.classification")}</span>
              <select
                value={form.classification}
                onChange={(event) => setForm({ ...form, classification: event.target.value })}
              >
                <option value="Restricted">Restricted</option>
                <option value="Confidential">Confidential</option>
                <option value="Internal">Internal</option>
                <option value="Public">Public</option>
              </select>
            </label>
            <label className="field">
              <span>{t("dspmPage.sensitiveData")}</span>
              <input
                value={form.sensitiveDataTypes}
                onChange={(event) => setForm({ ...form, sensitiveDataTypes: event.target.value })}
                placeholder="PII,SSN,PCI,PHI"
              />
            </label>
            <label className="field">
              <span>{t("dspmPage.control")}</span>
              <input
                value={form.controlCode}
                onChange={(event) => setForm({ ...form, controlCode: event.target.value })}
              />
            </label>
            <label className="field">
              <span>{t("dspmPage.ownerEmail")}</span>
              <input
                value={form.ownerEmail}
                onChange={(event) => setForm({ ...form, ownerEmail: event.target.value })}
              />
            </label>
          </div>
          <div style={{ display: "flex", gap: 16, flexWrap: "wrap", marginBottom: 12 }}>
            <label>
              <input
                type="checkbox"
                checked={form.encrypted}
                onChange={(event) => setForm({ ...form, encrypted: event.target.checked })}
              />{" "}
              {t("dspmPage.encrypted")}
            </label>
            <label>
              <input
                type="checkbox"
                checked={form.publicAccess}
                onChange={(event) => setForm({ ...form, publicAccess: event.target.checked })}
              />{" "}
              {t("dspmPage.publicBadge")}
            </label>
            <label>
              <input
                type="checkbox"
                checked={form.retentionCompliant}
                onChange={(event) => setForm({ ...form, retentionCompliant: event.target.checked })}
              />{" "}
              {t("dspmPage.retentionOk")}
            </label>
          </div>
          <button className="btn primary" type="submit">
            {t("dspmPage.saveStore")}
          </button>
        </form>
      )}
    </>
  );
}
