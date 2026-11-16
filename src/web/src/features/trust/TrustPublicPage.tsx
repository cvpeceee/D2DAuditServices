import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useParams } from "react-router-dom";
import { ApiError, api } from "../../shared/api/client";
import { formatDate } from "../../shared/ui/ui";

type Document = {
  id: string;
  title: string;
  summary: string;
  access: string;
  fileName: string;
  updatedOn: string;
};

type Page = {
  slug: string;
  tenantName: string;
  headline: string;
  summary: string;
  contactEmail: string;
  frameworks: { key: string; name: string; controls: number; passing: number }[];
  documents: Document[];
};

export function TrustPublicPage() {
  const { t } = useTranslation();
  const { slug = "" } = useParams();
  const [page, setPage] = useState<Page | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);
  const [form, setForm] = useState({
    name: "",
    email: "",
    company: "",
    purpose: "",
    documentId: "",
  });

  useEffect(() => {
    void api<Page>(`/api/v1/public/trust/${encodeURIComponent(slug)}`)
      .then(setPage)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  }, [slug]);

  async function requestAccess(event: React.FormEvent) {
    event.preventDefault();
    await api(`/api/v1/public/trust/${encodeURIComponent(slug)}/requests`, {
      method: "POST",
      body: {
        name: form.name,
        email: form.email,
        company: form.company,
        purpose: form.purpose,
        documentId: form.documentId || null,
      },
    });
    setSent(true);
  }

  return (
    <main className="content" id="content">
      {!page ? (
        <div className="card">
          <h1>{t("trustPublic.title")}</h1>
          {error && (
            <div className="error" role="alert">
              {t("trustPublic.missing")}
            </div>
          )}
        </div>
      ) : (
        <>
          <div className="head">
            <div>
              <h1>{page.headline}</h1>
              <div className="sub">
                {page.tenantName} · {page.contactEmail}
              </div>
            </div>
          </div>
          <p>{page.summary}</p>
          <div className="card">
            <h3>{t("trustPublic.frameworks")}</h3>
            <ul>
              {page.frameworks.map((item) => (
                <li key={item.key}>
                  <b>{item.name}</b> ·{" "}
                  {t("trustPublic.controls", { passing: item.passing, total: item.controls })}
                </li>
              ))}
            </ul>
            <h3>{t("trustPublic.documents")}</h3>
            <ul>
              {page.documents.map((item) => (
                <li key={item.id}>
                  <b>{item.title}</b> · {t(`trustPage.access.${item.access}`)}
                  <div className="id">
                    {item.summary}
                    {item.fileName ? ` · ${item.fileName}` : ""} · {formatDate(item.updatedOn)}
                  </div>
                </li>
              ))}
            </ul>
          </div>
          <form className="card" onSubmit={(event) => void requestAccess(event)}>
            <h3>{t("trustPublic.request")}</h3>
            {sent ? (
              <div className="notice" role="status">
                {t("trustPublic.sent")}
              </div>
            ) : (
              <>
                <div className="split-fields">
                  <input
                    aria-label={t("trustPublic.name")}
                    placeholder={t("trustPublic.name")}
                    value={form.name}
                    onChange={(event) => setForm({ ...form, name: event.target.value })}
                    required
                  />
                  <input
                    aria-label={t("trustPublic.email")}
                    placeholder={t("trustPublic.email")}
                    value={form.email}
                    onChange={(event) => setForm({ ...form, email: event.target.value })}
                    required
                  />
                  <input
                    aria-label={t("trustPublic.company")}
                    placeholder={t("trustPublic.company")}
                    value={form.company}
                    onChange={(event) => setForm({ ...form, company: event.target.value })}
                    required
                  />
                  <select
                    aria-label={t("trustPublic.document")}
                    value={form.documentId}
                    onChange={(event) => setForm({ ...form, documentId: event.target.value })}
                  >
                    <option value="">{t("trustPublic.allDocuments")}</option>
                    {page.documents
                      .filter((item) => item.access === "Nda")
                      .map((item) => (
                        <option key={item.id} value={item.id}>
                          {item.title}
                        </option>
                      ))}
                  </select>
                </div>
                <label className="field">
                  <span>{t("trustPublic.purpose")}</span>
                  <input
                    value={form.purpose}
                    onChange={(event) => setForm({ ...form, purpose: event.target.value })}
                    required
                  />
                </label>
                <button className="btn primary" type="submit">
                  {t("trustPublic.send")}
                </button>
              </>
            )}
          </form>
        </>
      )}
    </main>
  );
}
