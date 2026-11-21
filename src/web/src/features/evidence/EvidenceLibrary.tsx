import { useEffect, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { ApiError, api } from "../../shared/api/client";
import { Pill } from "../../shared/ui/ui";

type EvidenceRequest = {
  id: string;
  documentCode: string;
  documentTitle: string;
  assigneeName: string;
  assigneeEmail: string;
  dueOn: string;
  note: string | null;
  status: string;
  overdue: boolean;
  requestedByName: string;
  requestedAt: string;
  fulfilledAt: string | null;
};
type EvidenceDocument = {
  id: string;
  code: string;
  title: string;
  description: string;
  ownerName: string;
  ownerEmail: string;
  cadence: string;
  validFrom: string | null;
  validUntil: string | null;
  status: "missing" | "current" | "expiringSoon" | "expired";
  currentEvidenceId: string | null;
  currentSha256: string | null;
  controlCodes: string[];
  requests: EvidenceRequest[];
};
type LibraryPage = {
  summary: {
    total: number;
    current: number;
    expiringSoon: number;
    expired: number;
    missing: number;
    openRequests: number;
  };
  documents: EvidenceDocument[];
  myRequests: EvidenceRequest[];
};

const cadences = ["OneTime", "Monthly", "Quarterly", "Semiannual", "Annual"];
const tone: Record<string, string> = {
  current: "pass",
  expiringSoon: "warn",
  expired: "fail",
  missing: "info",
};
const today = () => new Date().toISOString().slice(0, 10);
const codes = (value: string) =>
  value
    .split(",")
    .map((item) => item.trim())
    .filter(Boolean);

function toBase64(file: File) {
  return new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result).split(",")[1] ?? "");
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });
}

function UploadForm({
  tenantId,
  code,
  onDone,
}: {
  tenantId: string | null;
  code: string;
  onDone: (error: string | null) => void;
}) {
  const { t } = useTranslation();
  const [file, setFile] = useState<File | null>(null);
  const [validFrom, setValidFrom] = useState(today());
  const [busy, setBusy] = useState(false);
  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!file) return;
    setBusy(true);
    try {
      await api(`/api/v1/evidence/library/${encodeURIComponent(code)}/upload`, {
        method: "POST",
        tenantId,
        body: { fileName: file.name, contentBase64: await toBase64(file), validFrom },
      });
      setFile(null);
      onDone(null);
    } catch (caught) {
      onDone(caught instanceof ApiError ? caught.code : "save");
    } finally {
      setBusy(false);
    }
  };
  return (
    <form className="act" onSubmit={(event) => void submit(event)}>
      <input
        aria-label={t("assurance.evidence.file", { code })}
        type="file"
        onChange={(event) => setFile(event.target.files?.[0] ?? null)}
      />
      <input
        aria-label={t("assurance.evidence.validFrom")}
        type="date"
        value={validFrom}
        onChange={(event) => setValidFrom(event.target.value)}
      />
      <button className="btn primary" type="submit" disabled={busy || !file}>
        {t("assurance.evidence.upload")}
      </button>
    </form>
  );
}

export function EvidenceLibrary({
  tenantId,
  canWrite,
}: {
  tenantId: string | null;
  canWrite: boolean;
}) {
  const { t } = useTranslation();
  const [page, setPage] = useState<LibraryPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [selected, setSelected] = useState<string[]>([]);
  const [linkControls, setLinkControls] = useState("");
  const [creating, setCreating] = useState(false);
  const [requesting, setRequesting] = useState<string | null>(null);
  const [uploading, setUploading] = useState<string | null>(null);
  const [form, setForm] = useState({
    title: "",
    description: "",
    ownerName: "",
    ownerEmail: "",
    cadence: "Annual",
    controlCodes: "",
  });
  const [request, setRequest] = useState({
    assigneeName: "",
    assigneeEmail: "",
    dueOn: new Date(Date.now() + 14 * 86_400_000).toISOString().slice(0, 10),
    note: "",
  });

  const load = () => {
    if (!tenantId) return;
    void api<LibraryPage>("/api/v1/evidence/library", { tenantId })
      .then(setPage)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  const run = async (action: () => Promise<string | null>) => {
    setError(null);
    setNotice(null);
    try {
      setNotice(await action());
      load();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.code : "save");
    }
  };

  const create = (event: FormEvent) => {
    event.preventDefault();
    void run(async () => {
      await api("/api/v1/evidence/library", {
        method: "POST",
        tenantId,
        body: { ...form, controlCodes: codes(form.controlCodes) },
      });
      setCreating(false);
      setForm({ ...form, title: "", description: "", controlCodes: "" });
      return null;
    });
  };

  const link = () =>
    void run(async () => {
      const added = await api<number>("/api/v1/evidence/library/links", {
        method: "POST",
        tenantId,
        body: { documentCodes: selected, controlCodes: codes(linkControls) },
      });
      setSelected([]);
      setLinkControls("");
      return t("assurance.evidence.linked", { count: added });
    });

  const renew = () =>
    void run(async () => {
      const created = await api<number>("/api/v1/evidence/library/renewals", {
        method: "POST",
        tenantId,
        body: {},
      });
      return t("assurance.evidence.renewalsCreated", { count: created });
    });

  const sendRequest = (event: FormEvent, code: string) => {
    event.preventDefault();
    void run(async () => {
      await api(`/api/v1/evidence/library/${encodeURIComponent(code)}/requests`, {
        method: "POST",
        tenantId,
        body: { ...request, note: request.note || null },
      });
      setRequesting(null);
      return t("assurance.evidence.requestSent");
    });
  };

  const uploaded = (failure: string | null) => {
    if (failure) setError(failure);
    else {
      setUploading(null);
      setNotice(t("assurance.evidence.uploaded"));
      load();
    }
  };

  if (!page)
    return error ? (
      <div className="error" role="alert">
        {t("errors.load")}
      </div>
    ) : (
      <div className="skeleton lg" />
    );

  const cards = ["current", "expiringSoon", "expired", "missing", "openRequests"] as const;

  return (
    <div className="stack">
      {error && (
        <div className="error" role="alert">
          {t(`assurance.errors.${error}`, { defaultValue: error })}
        </div>
      )}
      {notice && (
        <div className="notice" role="status">
          {notice}
        </div>
      )}
      <div className="grid" style={{ gridTemplateColumns: "repeat(auto-fit, minmax(140px, 1fr))" }}>
        {cards.map((card) => (
          <div className="card" key={card}>
            <div className="m">{t(`assurance.evidence.summary.${card}`)}</div>
            <div style={{ fontSize: 24, fontWeight: 800 }}>{page.summary[card]}</div>
          </div>
        ))}
      </div>
      {page.myRequests.length > 0 && (
        <div className="card">
          <h3>{t("assurance.evidence.myRequests")}</h3>
          {page.myRequests.map((item) => (
            <div key={item.id} className="stack" style={{ marginBottom: 8 }}>
              <div>
                <span className="id">{item.documentCode}</span> {item.documentTitle} ·{" "}
                {t("assurance.evidence.due", { date: item.dueOn })}
                {item.overdue && (
                  <>
                    {" "}
                    <Pill status="fail">{t("assurance.evidence.overdue")}</Pill>
                  </>
                )}
                {item.note && <div className="m">{item.note}</div>}
              </div>
              <UploadForm tenantId={tenantId} code={item.documentCode} onDone={uploaded} />
            </div>
          ))}
        </div>
      )}
      {canWrite && (
        <div className="act">
          <button className="btn primary" type="button" onClick={() => setCreating(!creating)}>
            {t("assurance.evidence.new")}
          </button>
          <button className="btn" type="button" onClick={renew}>
            {t("assurance.evidence.renew")}
          </button>
        </div>
      )}
      {creating && canWrite && (
        <form className="card stack" onSubmit={create}>
          <h3>{t("assurance.evidence.new")}</h3>
          <div className="grid" style={{ gridTemplateColumns: "1fr 1fr" }}>
            <label className="field">
              <span>{t("assurance.evidence.title")}</span>
              <input
                required
                value={form.title}
                onChange={(event) => setForm({ ...form, title: event.target.value })}
              />
            </label>
            <label className="field">
              <span>{t("assurance.evidence.cadence")}</span>
              <select
                value={form.cadence}
                onChange={(event) => setForm({ ...form, cadence: event.target.value })}
              >
                {cadences.map((cadence) => (
                  <option key={cadence} value={cadence}>
                    {t(`assurance.cadence.${cadence}`)}
                  </option>
                ))}
              </select>
            </label>
            <label className="field">
              <span>{t("assurance.evidence.ownerName")}</span>
              <input
                required
                value={form.ownerName}
                onChange={(event) => setForm({ ...form, ownerName: event.target.value })}
              />
            </label>
            <label className="field">
              <span>{t("assurance.evidence.ownerEmail")}</span>
              <input
                required
                type="email"
                value={form.ownerEmail}
                onChange={(event) => setForm({ ...form, ownerEmail: event.target.value })}
              />
            </label>
          </div>
          <label className="field">
            <span>{t("assurance.evidence.description")}</span>
            <input
              value={form.description}
              onChange={(event) => setForm({ ...form, description: event.target.value })}
            />
          </label>
          <label className="field">
            <span>{t("assurance.tests.controls")}</span>
            <input
              value={form.controlCodes}
              placeholder="VM-05, CM-14"
              onChange={(event) => setForm({ ...form, controlCodes: event.target.value })}
            />
          </label>
          <div className="act">
            <button className="btn primary" type="submit">
              {t("assurance.save")}
            </button>
          </div>
        </form>
      )}
      {page.documents.length === 0 ? (
        <div className="empty">{t("assurance.evidence.empty")}</div>
      ) : (
        <div className="card" style={{ padding: "4px 8px" }}>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  {canWrite && <th aria-label={t("assurance.evidence.select")} />}
                  <th>{t("assurance.evidence.document")}</th>
                  <th>{t("assurance.evidence.owner")}</th>
                  <th>{t("assurance.evidence.cadence")}</th>
                  <th>{t("assurance.evidence.validUntil")}</th>
                  <th>{t("assurance.evidence.status")}</th>
                  <th>{t("assurance.tests.controls")}</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {page.documents.map((document) => {
                  const open = document.requests.filter((item) => item.status === "Open");
                  return (
                    <tr key={document.id}>
                      {canWrite && (
                        <td>
                          <input
                            type="checkbox"
                            aria-label={t("assurance.evidence.selectDocument", {
                              code: document.code,
                            })}
                            checked={selected.includes(document.code)}
                            onChange={(event) =>
                              setSelected(
                                event.target.checked
                                  ? [...selected, document.code]
                                  : selected.filter((code) => code !== document.code),
                              )
                            }
                          />
                        </td>
                      )}
                      <td>
                        <div className="t">
                          <span className="id">{document.code}</span> {document.title}
                        </div>
                        {document.description && <div className="m">{document.description}</div>}
                        {open.map((item) => (
                          <div className="m" key={item.id}>
                            {t("assurance.evidence.requestedFrom", {
                              name: item.assigneeName,
                              date: item.dueOn,
                            })}
                            {item.overdue ? ` · ${t("assurance.evidence.overdue")}` : ""}
                          </div>
                        ))}
                        {requesting === document.code && (
                          <form
                            className="stack"
                            onSubmit={(event) => sendRequest(event, document.code)}
                          >
                            <input
                              required
                              aria-label={t("assurance.evidence.assigneeName")}
                              placeholder={t("assurance.evidence.assigneeName")}
                              value={request.assigneeName}
                              onChange={(event) =>
                                setRequest({ ...request, assigneeName: event.target.value })
                              }
                            />
                            <input
                              required
                              type="email"
                              aria-label={t("assurance.evidence.assigneeEmail")}
                              placeholder={t("assurance.evidence.assigneeEmail")}
                              value={request.assigneeEmail}
                              onChange={(event) =>
                                setRequest({ ...request, assigneeEmail: event.target.value })
                              }
                            />
                            <input
                              required
                              type="date"
                              aria-label={t("assurance.evidence.dueOn")}
                              value={request.dueOn}
                              onChange={(event) =>
                                setRequest({ ...request, dueOn: event.target.value })
                              }
                            />
                            <input
                              aria-label={t("assurance.evidence.note")}
                              placeholder={t("assurance.evidence.note")}
                              value={request.note}
                              onChange={(event) =>
                                setRequest({ ...request, note: event.target.value })
                              }
                            />
                            <div className="act">
                              <button className="btn primary" type="submit">
                                {t("assurance.evidence.sendRequest")}
                              </button>
                            </div>
                          </form>
                        )}
                        {uploading === document.code && (
                          <UploadForm tenantId={tenantId} code={document.code} onDone={uploaded} />
                        )}
                      </td>
                      <td className="m">{document.ownerName}</td>
                      <td className="m">{t(`assurance.cadence.${document.cadence}`)}</td>
                      <td className="m">{document.validUntil ?? "—"}</td>
                      <td>
                        <Pill status={tone[document.status]}>
                          {t(`assurance.expiry.${document.status}`)}
                        </Pill>
                      </td>
                      <td className="id">{document.controlCodes.join(", ")}</td>
                      <td>
                        {canWrite && (
                          <div className="act">
                            <button
                              className="btn"
                              type="button"
                              onClick={() =>
                                setRequesting(requesting === document.code ? null : document.code)
                              }
                            >
                              {t("assurance.evidence.request")}
                            </button>
                            <button
                              className="btn"
                              type="button"
                              onClick={() =>
                                setUploading(uploading === document.code ? null : document.code)
                              }
                            >
                              {t("assurance.evidence.upload")}
                            </button>
                          </div>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}
      {canWrite && selected.length > 0 && (
        <div className="card act">
          <span>{t("assurance.evidence.selected", { count: selected.length })}</span>
          <input
            aria-label={t("assurance.evidence.linkControls")}
            placeholder={t("assurance.evidence.linkControls")}
            value={linkControls}
            onChange={(event) => setLinkControls(event.target.value)}
          />
          <button
            className="btn primary"
            type="button"
            disabled={codes(linkControls).length === 0}
            onClick={link}
          >
            {t("assurance.evidence.bulkLink")}
          </button>
        </div>
      )}
    </div>
  );
}
