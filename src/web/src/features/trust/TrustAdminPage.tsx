import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useOutletContext } from "react-router-dom";
import { ApiError, api, type Workspace } from "../../shared/api/client";
import { formatDate, formatWhen, Pill } from "../../shared/ui/ui";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

type Document = {
  id: string;
  title: string;
  summary: string;
  access: string;
  fileName: string;
  updatedOn: string;
  hasFile?: boolean;
  sha256?: string;
};

type RequestRow = {
  id: string;
  documentTitle: string;
  requesterName: string;
  requesterEmail: string;
  company: string;
  purpose: string;
  status: string;
  requestedAt: string;
  ndaAccepted?: boolean;
  ndaAcceptedAt?: string | null;
  accessToken?: string;
};

type Page = {
  slug: string;
  published: boolean;
  headline: string;
  summary: string;
  contactEmail: string;
  documents: Document[];
  requests: RequestRow[];
};

export function TrustAdminPage() {
  const { t } = useTranslation();
  const { workspace, tenantId } = useOutletContext<ShellContext>();
  const [page, setPage] = useState<Page | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [doc, setDoc] = useState({
    title: "",
    summary: "",
    access: "Public",
    fileName: "",
    contentBase64: "",
    contentType: "application/pdf",
  });

  function load() {
    if (!tenantId) return;
    void api<Page>("/api/v1/trust", { tenantId })
      .then(setPage)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  async function save(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId || !page) return;
    setError(null);
    try {
      const next = await api<Page>("/api/v1/trust", { method: "POST", tenantId, body: page });
      setPage(next);
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  function handleFileChange(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      const result = typeof reader.result === "string" ? reader.result : "";
      const base64 = result.includes(",") ? result.split(",")[1] : result;
      setDoc((current) => ({
        ...current,
        fileName: current.fileName || file.name,
        contentBase64: base64,
        contentType: file.type || "application/pdf",
      }));
    };
    reader.readAsDataURL(file);
  }

  async function addDocument(event: React.FormEvent) {
    event.preventDefault();
    if (!tenantId) return;
    setError(null);
    try {
      await api("/api/v1/trust/documents", {
        method: "POST",
        tenantId,
        body: {
          title: doc.title,
          summary: doc.summary,
          access: doc.access,
          fileName: doc.fileName,
          contentBase64: doc.contentBase64 || null,
          contentType: doc.contentType || null,
        },
      });
      setDoc({
        title: "",
        summary: "",
        access: "Public",
        fileName: "",
        contentBase64: "",
        contentType: "application/pdf",
      });
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  async function decide(id: string, status: string) {
    if (!tenantId) return;
    setError(null);
    try {
      await api(`/api/v1/trust/requests/${id}`, { method: "POST", tenantId, body: { status } });
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("trustPage.title")}</h1>
          <div className="sub">{t("trustPage.sub")}</div>
        </div>
        {page && (
          <Link className="btn" to={`/trust/${page.slug}`}>
            {t("trustPage.viewPublic")}
          </Link>
        )}
      </div>
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}
      {page && (
        <form className="card" onSubmit={(event) => void save(event)}>
          <label className="check-row">
            <input
              type="checkbox"
              checked={page.published}
              disabled={!workspace?.canWrite}
              onChange={(event) => setPage({ ...page, published: event.target.checked })}
            />
            {t("trustPage.published")}
          </label>
          <label className="field">
            <span>{t("trustPage.headline")}</span>
            <input
              value={page.headline}
              disabled={!workspace?.canWrite}
              onChange={(event) => setPage({ ...page, headline: event.target.value })}
            />
          </label>
          <label className="field">
            <span>{t("trustPage.summary")}</span>
            <textarea
              value={page.summary}
              disabled={!workspace?.canWrite}
              onChange={(event) => setPage({ ...page, summary: event.target.value })}
            />
          </label>
          <label className="field">
            <span>{t("trustPage.contact")}</span>
            <input
              value={page.contactEmail}
              disabled={!workspace?.canWrite}
              onChange={(event) => setPage({ ...page, contactEmail: event.target.value })}
            />
          </label>
          {workspace?.canWrite && (
            <button className="btn primary" type="submit">
              {t("trustPage.save")}
            </button>
          )}
          <h3>{t("trustPage.documents")}</h3>
          <ul>
            {page.documents.map((item) => (
              <li key={item.id}>
                <b>{item.title}</b> · {t(`trustPage.access.${item.access}`)} · {item.fileName}
                {item.hasFile && <Pill status="pass">{t("trustPage.fileUploaded")}</Pill>}
                <div className="id">
                  {item.summary} · {formatDate(item.updatedOn)}
                </div>
              </li>
            ))}
          </ul>
          <h3>{t("trustPage.requests")}</h3>
          <ul>
            {page.requests.map((item) => (
              <li key={item.id}>
                <b>{item.requesterName}</b> · {item.company} ·{" "}
                {item.documentTitle || t("trustPage.allDocuments")}
                <div className="id">
                  {item.purpose} · {formatWhen(item.requestedAt)}
                  {item.ndaAccepted && ` · ${t("trustPage.ndaSigned")}`}
                </div>
                <Pill
                  status={
                    item.status === "Approved" ? "pass" : item.status === "Denied" ? "fail" : "warn"
                  }
                >
                  {t(`trustPage.statuses.${item.status}`)}
                </Pill>
                {item.status === "Approved" && item.accessToken && (
                  <div className="id" style={{ marginTop: 4 }}>
                    {t("trustPage.accessToken")}: <code>{item.accessToken}</code>
                  </div>
                )}
                {workspace?.canWrite && item.status === "Pending" && (
                  <div className="act">
                    <button
                      className="btn"
                      type="button"
                      onClick={() => void decide(item.id, "Approved")}
                    >
                      {t("trustPage.approve")}
                    </button>
                    <button
                      className="btn"
                      type="button"
                      onClick={() => void decide(item.id, "Denied")}
                    >
                      {t("trustPage.deny")}
                    </button>
                  </div>
                )}
              </li>
            ))}
          </ul>
        </form>
      )}
      {workspace?.canWrite && (
        <form className="card" onSubmit={(event) => void addDocument(event)}>
          <h3>{t("trustPage.addDocument")}</h3>
          <div className="split-fields">
            <input
              aria-label={t("trustPage.docTitle")}
              placeholder={t("trustPage.docTitle")}
              value={doc.title}
              onChange={(event) => setDoc({ ...doc, title: event.target.value })}
              required
            />
            <input
              aria-label={t("trustPage.fileName")}
              placeholder={t("trustPage.fileName")}
              value={doc.fileName}
              onChange={(event) => setDoc({ ...doc, fileName: event.target.value })}
              required
            />
            <select
              aria-label={t("trustPage.accessLabel")}
              value={doc.access}
              onChange={(event) => setDoc({ ...doc, access: event.target.value })}
            >
              <option value="Public">{t("trustPage.access.Public")}</option>
              <option value="Nda">{t("trustPage.access.Nda")}</option>
            </select>
          </div>
          <label className="field">
            <span>{t("trustPage.summary")}</span>
            <input
              value={doc.summary}
              onChange={(event) => setDoc({ ...doc, summary: event.target.value })}
              required
            />
          </label>
          <label className="field">
            <span>{t("trustPage.attachFile")}</span>
            <input type="file" onChange={handleFileChange} />
          </label>
          <button className="btn" type="submit">
            {t("trustPage.addDocument")}
          </button>
        </form>
      )}
    </>
  );
}
