import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useOutletContext } from "react-router-dom";
import { ApiError, api, type Workspace } from "../../shared/api/client";
import { formatWhen, Pill } from "../../shared/ui/ui";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

type Computer = {
  id: string;
  serial: string;
  operatingSystem: string;
  ownerName: string;
  ownerEmail: string;
  encrypted: boolean;
  screenLock: boolean;
  antivirus: boolean;
  passwordManager: boolean;
  autoUpdate: boolean;
  lastCheckIn: string;
  source: string;
  screenshotName: string | null;
};

const blank = {
  serial: "",
  operatingSystem: "",
  ownerName: "",
  ownerEmail: "",
  encrypted: true,
  screenLock: true,
  antivirus: true,
  passwordManager: true,
  autoUpdate: true,
  source: "Manual",
};

function flag(on: boolean, yes: string, no: string) {
  return <Pill status={on ? "pass" : "fail"}>{on ? yes : no}</Pill>;
}

export function ComputersPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [rows, setRows] = useState<Computer[] | null>(null);
  const [form, setForm] = useState(blank);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  function load() {
    if (!tenantId) return;
    void api<Computer[]>("/api/v1/computers", { tenantId })
      .then(setRows)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  async function save(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId) return;
    setError(null);
    try {
      const path = form.source === "Agent" ? "/api/v1/computers/check-in" : "/api/v1/computers";
      await api(path, { method: "POST", tenantId, body: form });
      setForm(blank);
      setNotice(t("computersPage.saved"));
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  async function upload(id: string, file: File) {
    if (!tenantId) return;
    const bytes = new Uint8Array(await file.arrayBuffer());
    let binary = "";
    bytes.forEach((byte) => {
      binary += String.fromCharCode(byte);
    });
    await api(`/api/v1/computers/${id}/screenshot`, {
      method: "POST",
      tenantId,
      body: { fileName: file.name, contentBase64: btoa(binary) },
    });
    setNotice(t("computersPage.uploaded"));
    load();
  }

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("computersPage.title")}</h1>
          <div className="sub">{t("computersPage.sub")}</div>
        </div>
        {workspace?.canWrite && (
          <button
            className="btn"
            type="button"
            onClick={() => {
              if (!tenantId) return;
              void api<{ imported: number }>("/api/v1/computers/mdm-sync", {
                method: "POST",
                tenantId,
              }).then((result) => {
                setNotice(t("computersPage.mdmSynced", { count: result.imported }));
                load();
              });
            }}
          >
            {t("computersPage.mdmSync")}
          </button>
        )}
      </div>
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}
      {notice && (
        <div className="notice" role="status">
          {notice}
        </div>
      )}
      <div className="card">
        {!rows ? null : rows.length === 0 ? (
          <div className="empty">{t("computersPage.empty")}</div>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>{t("computersPage.serial")}</th>
                  <th>{t("computersPage.os")}</th>
                  <th>{t("computersPage.owner")}</th>
                  <th>{t("computersPage.encryption")}</th>
                  <th>{t("computersPage.screen")}</th>
                  <th>{t("computersPage.antivirus")}</th>
                  <th>{t("computersPage.passwords")}</th>
                  <th>{t("computersPage.updates")}</th>
                  <th>{t("computersPage.checkIn")}</th>
                  <th>{t("computersPage.source")}</th>
                  <th>{t("computersPage.screenshot")}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.id}>
                    <td>
                      <b>{row.serial}</b>
                    </td>
                    <td>{row.operatingSystem}</td>
                    <td>
                      {row.ownerName}
                      <div className="id">{row.ownerEmail}</div>
                    </td>
                    <td>{flag(row.encrypted, t("computersPage.on"), t("computersPage.off"))}</td>
                    <td>{flag(row.screenLock, t("computersPage.on"), t("computersPage.off"))}</td>
                    <td>{flag(row.antivirus, t("computersPage.on"), t("computersPage.off"))}</td>
                    <td>
                      {flag(row.passwordManager, t("computersPage.on"), t("computersPage.off"))}
                    </td>
                    <td>{flag(row.autoUpdate, t("computersPage.on"), t("computersPage.off"))}</td>
                    <td>{formatWhen(row.lastCheckIn)}</td>
                    <td>{t(`computersPage.sources.${row.source}`)}</td>
                    <td>
                      {row.screenshotName && <div className="id">{row.screenshotName}</div>}
                      {workspace?.canWrite && (
                        <label className="linkish">
                          {t("computersPage.upload")}
                          <input
                            type="file"
                            accept="image/*,.png,.jpg,.pdf"
                            hidden
                            onChange={(event) => {
                              const file = event.target.files?.[0];
                              if (file) void upload(row.id, file);
                            }}
                          />
                        </label>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
      {workspace?.canWrite && (
        <form className="card" onSubmit={(event) => void save(event)}>
          <h3>{t("computersPage.add")}</h3>
          <div className="split-fields">
            <input
              aria-label={t("computersPage.serial")}
              placeholder={t("computersPage.serial")}
              value={form.serial}
              onChange={(event) => setForm({ ...form, serial: event.target.value })}
              required
            />
            <input
              aria-label={t("computersPage.os")}
              placeholder={t("computersPage.os")}
              value={form.operatingSystem}
              onChange={(event) => setForm({ ...form, operatingSystem: event.target.value })}
              required
            />
            <input
              aria-label={t("computersPage.owner")}
              placeholder={t("computersPage.owner")}
              value={form.ownerName}
              onChange={(event) => setForm({ ...form, ownerName: event.target.value })}
              required
            />
            <input
              aria-label={t("computersPage.email")}
              placeholder={t("computersPage.email")}
              value={form.ownerEmail}
              onChange={(event) => setForm({ ...form, ownerEmail: event.target.value })}
              required
            />
            <select
              aria-label={t("computersPage.source")}
              value={form.source}
              onChange={(event) => setForm({ ...form, source: event.target.value })}
            >
              <option value="Manual">{t("computersPage.sources.Manual")}</option>
              <option value="Agent">{t("computersPage.sources.Agent")}</option>
              <option value="Mdm">{t("computersPage.sources.Mdm")}</option>
            </select>
          </div>
          {(
            [
              ["encrypted", "encryption"],
              ["screenLock", "screen"],
              ["antivirus", "antivirus"],
              ["passwordManager", "passwords"],
              ["autoUpdate", "updates"],
            ] as const
          ).map(([key, label]) => (
            <label key={key} className="check-row">
              <input
                type="checkbox"
                checked={form[key]}
                onChange={(event) => setForm({ ...form, [key]: event.target.checked })}
              />
              {t(`computersPage.${label}`)}
            </label>
          ))}
          <button className="btn primary" type="submit">
            {t("computersPage.save")}
          </button>
        </form>
      )}
    </>
  );
}
