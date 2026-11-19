import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useParams } from "react-router-dom";
import { ApiError, api, download } from "../../shared/api/client";
import { formatDate, formatWhen, Pill } from "../../shared/ui/ui";

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

type Page = {
  slug: string;
  tenantName: string;
  headline: string;
  summary: string;
  contactEmail: string;
  frameworks: { key: string; name: string; controls: number; passing: number }[];
  documents: Document[];
};

type TrustReceipt = {
  slug: string;
  tenantName: string;
  region: string;
  retentionYears: number;
  issuedAtUtc: string;
  frameworks: { key: string; name: string; passing: number; total: number; percent: number }[];
  chainEntryCount: number;
  latestChainSequence: number;
  chainHeadHash: string;
  chainStatus: string;
  canonicalPayload: string;
  receiptSha256: string;
};

type TrustReply = {
  answer: string;
  mode: string;
  citations: { kind: string; code: string; title: string; href: string }[];
};

async function sha256Hex(value: string) {
  const bytes = new TextEncoder().encode(value);
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

export function TrustPublicPage() {
  const { t } = useTranslation();
  const { slug = "" } = useParams();
  const [page, setPage] = useState<Page | null>(null);
  const [receipt, setReceipt] = useState<TrustReceipt | null>(null);
  const [verified, setVerified] = useState<"idle" | "valid" | "invalid">("idle");
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);
  const [requestError, setRequestError] = useState<string | null>(null);
  const [ndaToken, setNdaToken] = useState("");
  const [downloadError, setDownloadError] = useState<string | null>(null);
  const [question, setQuestion] = useState("");
  const [reply, setReply] = useState<TrustReply | null>(null);
  const [aiError, setAiError] = useState<string | null>(null);
  const [asking, setAsking] = useState(false);
  const [form, setForm] = useState({
    name: "",
    email: "",
    company: "",
    purpose: "",
    documentId: "",
    ndaAccepted: false,
  });

  useEffect(() => {
    void api<Page>(`/api/v1/public/trust/${encodeURIComponent(slug)}`)
      .then(setPage)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
    void api<TrustReceipt>(`/api/v1/public/trust/${encodeURIComponent(slug)}/receipt`)
      .then((value) => {
        setReceipt(value);
        setVerified("idle");
      })
      .catch(() => setReceipt(null));
  }, [slug]);

  async function requestAccess(event: React.FormEvent) {
    event.preventDefault();
    setRequestError(null);
    try {
      await api(`/api/v1/public/trust/${encodeURIComponent(slug)}/requests`, {
        method: "POST",
        body: {
          name: form.name,
          email: form.email,
          company: form.company,
          purpose: form.purpose,
          documentId: form.documentId || null,
          ndaAccepted: form.ndaAccepted,
        },
      });
      setSent(true);
    } catch (caught) {
      setRequestError(
        caught instanceof ApiError && caught.code === "nda_required"
          ? t("trustPublic.ndaRequiredError")
          : t("errors.load"),
      );
    }
  }

  async function verifyReceiptLocally() {
    if (!receipt) return;
    const computed = await sha256Hex(receipt.canonicalPayload);
    setVerified(computed === receipt.receiptSha256 ? "valid" : "invalid");
  }

  function downloadReceiptJson() {
    if (!receipt) return;
    const blob = new Blob([JSON.stringify(receipt, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `${receipt.slug}-trust-receipt.json`;
    a.click();
    URL.revokeObjectURL(url);
  }

  async function downloadDoc(doc: Document) {
    setDownloadError(null);
    const headers: Record<string, string> = {};
    if (doc.access === "Nda" && ndaToken.trim()) {
      headers["X-Trust-Token"] = ndaToken.trim();
    }
    try {
      await download(
        `/api/v1/public/trust/${encodeURIComponent(slug)}/documents/${doc.id}/download`,
        null,
        {
          headers,
          fallbackFileName: doc.fileName || `${doc.title.toLowerCase().replace(/\s+/g, "-")}.pdf`,
        },
      );
    } catch (caught) {
      setDownloadError(
        caught instanceof ApiError && caught.code === "nda_required"
          ? t("trustPublic.ndaRequiredError")
          : t("errors.load"),
      );
    }
  }

  async function askTrustAi(event: React.FormEvent) {
    event.preventDefault();
    if (!question.trim()) return;
    setAsking(true);
    setAiError(null);
    try {
      const answer = await api<TrustReply>(`/api/v1/public/trust/${encodeURIComponent(slug)}/ask`, {
        method: "POST",
        body: { question },
      });
      setReply(answer);
    } catch {
      setAiError(t("errors.load"));
    } finally {
      setAsking(false);
    }
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

          {receipt && (
            <section className="card">
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  flexWrap: "wrap",
                  gap: 8,
                }}
              >
                <div>
                  <h3>{t("trustPublic.receiptTitle")}</h3>
                  <div className="m">{t("trustPublic.receiptSub")}</div>
                </div>
                <Pill status={receipt.chainStatus === "Verified" ? "pass" : "warn"}>
                  {t("trustPublic.chainEntries", {
                    count: receipt.chainEntryCount,
                    years: receipt.retentionYears,
                  })}
                </Pill>
              </div>
              <div style={{ marginTop: 10, fontSize: 13 }}>
                <div>
                  <b>{t("trustPublic.issuedAt")}:</b> {formatWhen(receipt.issuedAtUtc)} ·{" "}
                  <b>Region:</b> {receipt.region}
                </div>
                <div style={{ wordBreak: "break-all", marginTop: 4 }}>
                  <b>{t("trustPublic.chainHead")}:</b> <code>{receipt.chainHeadHash || "—"}</code>
                </div>
                <div style={{ wordBreak: "break-all", marginTop: 4 }}>
                  <b>{t("trustPublic.receiptSha256")}:</b> <code>{receipt.receiptSha256}</code>
                </div>
              </div>
              <div style={{ display: "flex", gap: 8, flexWrap: "wrap", marginTop: 12 }}>
                <button
                  type="button"
                  className="btn primary"
                  onClick={() => void verifyReceiptLocally()}
                >
                  {t("trustPublic.verifyLocally")}
                </button>
                <button type="button" className="btn" onClick={downloadReceiptJson}>
                  {t("trustPublic.downloadReceipt")}
                </button>
                {verified === "valid" && <Pill status="pass">{t("trustPublic.receiptValid")}</Pill>}
                {verified === "invalid" && (
                  <Pill status="fail">{t("trustPublic.receiptInvalid")}</Pill>
                )}
              </div>
            </section>
          )}

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
            {downloadError && (
              <div className="error" role="alert">
                {downloadError}
              </div>
            )}
            <label className="field" style={{ maxWidth: 420 }}>
              <span>{t("trustPublic.ndaTokenLabel")}</span>
              <input
                value={ndaToken}
                onChange={(event) => setNdaToken(event.target.value)}
                placeholder={t("trustPublic.ndaTokenPlaceholder")}
              />
            </label>
            <ul>
              {page.documents.map((item) => (
                <li key={item.id} style={{ marginBottom: 8 }}>
                  <b>{item.title}</b> · {t(`trustPage.access.${item.access}`)}{" "}
                  <button type="button" className="linkish" onClick={() => void downloadDoc(item)}>
                    {t("trustPublic.downloadDoc")}
                  </button>
                  <div className="id">
                    {item.summary}
                    {item.fileName ? ` · ${item.fileName}` : ""} · {formatDate(item.updatedOn)}
                  </div>
                </li>
              ))}
            </ul>
          </div>

          <section className="card">
            <h3>{t("trustPublic.aiTitle")}</h3>
            <p className="m">{t("trustPublic.aiSub")}</p>
            <form
              style={{ display: "flex", gap: 8, flexWrap: "wrap", marginTop: 8 }}
              onSubmit={(event) => void askTrustAi(event)}
            >
              <input
                style={{ flex: 1, minWidth: 240 }}
                aria-label={t("trustPublic.aiPlaceholder")}
                placeholder={t("trustPublic.aiPlaceholder")}
                value={question}
                onChange={(event) => setQuestion(event.target.value)}
                required
              />
              <button className="btn primary" type="submit" disabled={asking}>
                {t("trustPublic.aiAsk")}
              </button>
            </form>
            {aiError && (
              <div className="error" role="alert" style={{ marginTop: 10 }}>
                {aiError}
              </div>
            )}
            {reply && (
              <div className="notice" style={{ marginTop: 12, whiteSpace: "pre-line" }}>
                <div>{reply.answer}</div>
                <div className="id" style={{ marginTop: 6 }}>
                  {t("trustPublic.aiMode", { mode: reply.mode })}
                </div>
              </div>
            )}
          </section>

          <form className="card" onSubmit={(event) => void requestAccess(event)}>
            <h3>{t("trustPublic.request")}</h3>
            {requestError && (
              <div className="error" role="alert">
                {requestError}
              </div>
            )}
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
                <label className="check-row" style={{ marginBottom: 12 }}>
                  <input
                    type="checkbox"
                    checked={form.ndaAccepted}
                    onChange={(event) => setForm({ ...form, ndaAccepted: event.target.checked })}
                    required
                  />
                  {t("trustPublic.ndaClickwrap")}
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
