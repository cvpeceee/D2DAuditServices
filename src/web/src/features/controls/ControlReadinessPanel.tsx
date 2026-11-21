import { useEffect, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { ApiError, api } from "../../shared/api/client";
import { Pill } from "../../shared/ui/ui";

type Profile = {
  implementationGuidance: string;
  evidenceRequirement: string;
  requiredEvidenceCount: number;
  requiresPassingTest: boolean;
};
type Readiness = {
  controlCode: string;
  ready: boolean;
  percent: number;
  checks: { key: string; met: boolean; detail: string }[];
  profile: Profile | null;
  libraryEvidence: { code: string; title: string; status: string; validUntil: string | null }[];
};
type Comment = {
  id: string;
  authorName: string;
  authorEmail: string;
  body: string;
  createdAt: string;
  editedAt: string | null;
  history: { body: string; replacedAt: string }[];
};

const emptyProfile: Profile = {
  implementationGuidance: "",
  evidenceRequirement: "",
  requiredEvidenceCount: 1,
  requiresPassingTest: false,
};

export function ControlReadinessPanel({
  tenantId,
  code,
  canWrite,
}: {
  tenantId: string | null;
  code: string;
  canWrite: boolean;
}) {
  const { t } = useTranslation();
  const [readiness, setReadiness] = useState<Readiness | null>(null);
  const [comments, setComments] = useState<Comment[]>([]);
  const [profile, setProfile] = useState<Profile>(emptyProfile);
  const [editingProfile, setEditingProfile] = useState(false);
  const [draft, setDraft] = useState("");
  const [editing, setEditing] = useState<string | null>(null);
  const [editBody, setEditBody] = useState("");
  const [error, setError] = useState<string | null>(null);
  const base = `/api/v1/controls/${encodeURIComponent(code)}`;

  const load = () => {
    if (!tenantId) return;
    void api<Readiness>(`${base}/readiness`, { tenantId })
      .then((value) => {
        if (!value || !Array.isArray(value.checks)) {
          setReadiness(null);
          return;
        }
        setReadiness({
          ...value,
          libraryEvidence: Array.isArray(value.libraryEvidence) ? value.libraryEvidence : [],
        });
        setProfile(value.profile ?? emptyProfile);
      })
      .catch(() => setReadiness(null));
    void api<Comment[]>(`${base}/comments`, { tenantId })
      .then((rows) => setComments(Array.isArray(rows) ? rows : []))
      .catch(() => setComments([]));
  };

  useEffect(() => {
    setEditingProfile(false);
    setEditing(null);
    setError(null);
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId, code]);

  const run = async (action: () => Promise<void>) => {
    setError(null);
    try {
      await action();
      load();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.code : "save");
    }
  };

  const saveProfile = (event: FormEvent) => {
    event.preventDefault();
    void run(async () => {
      await api(`${base}/profile`, { method: "PUT", tenantId, body: profile });
      setEditingProfile(false);
    });
  };

  const addComment = (event: FormEvent) => {
    event.preventDefault();
    void run(async () => {
      await api(`${base}/comments`, { method: "POST", tenantId, body: { body: draft } });
      setDraft("");
    });
  };

  const saveEdit = (event: FormEvent, id: string) => {
    event.preventDefault();
    void run(async () => {
      await api(`${base}/comments/${id}`, { method: "PUT", tenantId, body: { body: editBody } });
      setEditing(null);
    });
  };

  return (
    <section className="stack" aria-label={t("assurance.controls.readiness")}>
      <h3 style={{ marginTop: 16 }}>
        {t("assurance.controls.readiness")}
        {readiness && (
          <>
            {" "}
            <Pill status={readiness.ready ? "pass" : "warn"}>
              {readiness.ready
                ? t("assurance.controls.ready")
                : t("assurance.controls.notReady", { percent: readiness.percent })}
            </Pill>
          </>
        )}
      </h3>
      {readiness && (
        <ul style={{ margin: 0, paddingLeft: 18 }}>
          {readiness.checks.map((check) => (
            <li key={check.key}>
              <span aria-hidden="true">{check.met ? "✓ " : "✗ "}</span>
              <strong>{t(`assurance.controls.checks.${check.key}`)}</strong> · {check.detail}
            </li>
          ))}
        </ul>
      )}
      {readiness && readiness.libraryEvidence.length > 0 && (
        <div className="m">
          {t("assurance.controls.library")}:{" "}
          {readiness.libraryEvidence
            .map((item) => `${item.code} (${t(`assurance.expiry.${item.status}`)})`)
            .join(", ")}
        </div>
      )}
      {readiness?.profile && !editingProfile && (
        <div className="m">
          {readiness.profile.implementationGuidance && (
            <div>
              <strong>{t("assurance.controls.guidance")}:</strong>{" "}
              {readiness.profile.implementationGuidance}
            </div>
          )}
          {readiness.profile.evidenceRequirement && (
            <div>
              <strong>{t("assurance.controls.evidenceRequirement")}:</strong>{" "}
              {readiness.profile.evidenceRequirement}
            </div>
          )}
        </div>
      )}
      {canWrite && !editingProfile && (
        <div>
          <button className="btn" type="button" onClick={() => setEditingProfile(true)}>
            {t("assurance.controls.editProfile")}
          </button>
        </div>
      )}
      {canWrite && editingProfile && (
        <form className="stack" onSubmit={saveProfile}>
          <label className="field">
            <span>{t("assurance.controls.guidance")}</span>
            <textarea
              value={profile.implementationGuidance}
              onChange={(event) =>
                setProfile({ ...profile, implementationGuidance: event.target.value })
              }
            />
          </label>
          <label className="field">
            <span>{t("assurance.controls.evidenceRequirement")}</span>
            <input
              value={profile.evidenceRequirement}
              onChange={(event) =>
                setProfile({ ...profile, evidenceRequirement: event.target.value })
              }
            />
          </label>
          <label className="field">
            <span>{t("assurance.controls.requiredEvidence")}</span>
            <input
              type="number"
              min={0}
              max={20}
              value={profile.requiredEvidenceCount}
              onChange={(event) =>
                setProfile({ ...profile, requiredEvidenceCount: Number(event.target.value) })
              }
            />
          </label>
          <label>
            <input
              type="checkbox"
              checked={profile.requiresPassingTest}
              onChange={(event) =>
                setProfile({ ...profile, requiresPassingTest: event.target.checked })
              }
            />{" "}
            {t("assurance.controls.requiresTest")}
          </label>
          <div className="act">
            <button className="btn primary" type="submit">
              {t("assurance.save")}
            </button>
            <button className="btn" type="button" onClick={() => setEditingProfile(false)}>
              {t("assurance.cancel")}
            </button>
          </div>
        </form>
      )}
      <h3 style={{ marginTop: 16 }}>{t("assurance.controls.comments")}</h3>
      {comments.length === 0 && <div className="m">{t("assurance.controls.noComments")}</div>}
      {comments.map((comment) => (
        <div key={comment.id} className="card" style={{ padding: 10 }}>
          <div className="m">
            {comment.authorName} · {comment.createdAt.slice(0, 10)}
            {comment.editedAt ? ` · ${t("assurance.controls.edited")}` : ""}
          </div>
          {editing === comment.id ? (
            <form className="stack" onSubmit={(event) => saveEdit(event, comment.id)}>
              <textarea
                aria-label={t("assurance.controls.editComment")}
                value={editBody}
                onChange={(event) => setEditBody(event.target.value)}
              />
              <div className="act">
                <button className="btn primary" type="submit">
                  {t("assurance.save")}
                </button>
                <button className="btn" type="button" onClick={() => setEditing(null)}>
                  {t("assurance.cancel")}
                </button>
              </div>
            </form>
          ) : (
            <div style={{ whiteSpace: "pre-wrap" }}>{comment.body}</div>
          )}
          {comment.history.length > 0 && (
            <details>
              <summary className="m">
                {t("assurance.controls.history", { count: comment.history.length })}
              </summary>
              {comment.history.map((entry) => (
                <div className="m" key={entry.replacedAt}>
                  {entry.replacedAt.slice(0, 10)}: {entry.body}
                </div>
              ))}
            </details>
          )}
          {canWrite && editing !== comment.id && (
            <button
              className="linkish"
              type="button"
              onClick={() => {
                setEditing(comment.id);
                setEditBody(comment.body);
              }}
            >
              {t("assurance.tests.edit")}
            </button>
          )}
        </div>
      ))}
      {canWrite && (
        <form className="stack" onSubmit={addComment}>
          <textarea
            aria-label={t("assurance.controls.newComment")}
            placeholder={t("assurance.controls.newComment")}
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
          />
          <div>
            <button className="btn" type="submit" disabled={!draft.trim()}>
              {t("assurance.controls.addComment")}
            </button>
          </div>
        </form>
      )}
      {error && (
        <div className="error" role="alert">
          {t(`assurance.errors.${error}`, { defaultValue: error })}
        </div>
      )}
    </section>
  );
}
