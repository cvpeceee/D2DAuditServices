import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useOutletContext } from "react-router-dom";
import { ApiError, api, type Workspace } from "../../shared/api/client";
import { formatDate, Pill } from "../../shared/ui/ui";
import type { MeCard } from "./types";

type ShellContext = { workspace: Workspace | null; tenantId: string | null };

function tone(status: string) {
  if (status === "Complete") return "pass";
  if (status === "Overdue") return "fail";
  if (status === "Due" || status === "Pending") return "warn";
  return "info";
}

export function MePage() {
  const { t } = useTranslation();
  const { tenantId } = useOutletContext<ShellContext>();
  const [cards, setCards] = useState<MeCard[] | null>(null);
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  function load() {
    if (!tenantId) return;
    void api<MeCard[]>("/api/v1/me/cards", { tenantId })
      .then(setCards)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);

  async function complete(kind: string) {
    if (!tenantId) return;
    setMessage(null);
    try {
      await api("/api/v1/me/cards", { method: "POST", tenantId, body: { kind, note, body: note } });
      setNote("");
      setMessage(t("mePage.saved"));
      load();
    } catch (caught: unknown) {
      setError(caught instanceof ApiError ? caught.code : "load");
    }
  }

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("mePage.title")}</h1>
          <div className="sub">{t("mePage.sub")}</div>
        </div>
      </div>
      {error && (
        <div className="error" role="alert">
          {t("errors.load")}
        </div>
      )}
      {message && (
        <div className="notice" role="status">
          {message}
        </div>
      )}
      <div className="cardgrid">
        {(cards ?? []).map((card) => (
          <article className="card" key={card.kind}>
            <h3>
              {card.title}
              <span className="r">
                <Pill status={tone(card.status)}>
                  {t(`mePage.status.${card.status}`, { defaultValue: card.status })}
                </Pill>
              </span>
            </h3>
            <div className="m">{card.dueOn ? formatDate(card.dueOn) : t("mePage.noDue")}</div>
            {card.evidenceSha256 && <div className="id">{card.evidenceSha256.slice(0, 16)}</div>}
            {card.action === "open" && (
              <Link className="btn" to="/training">
                {t("mePage.openTraining")}
              </Link>
            )}
            {card.action !== "done" && card.action !== "none" && card.action !== "open" && (
              <div className="stack">
                <textarea
                  value={note}
                  onChange={(event) => setNote(event.target.value)}
                  placeholder={t("mePage.note")}
                />
                <button
                  className="btn primary"
                  type="button"
                  onClick={() => void complete(card.kind)}
                >
                  {t(`mePage.actions.${card.action}`)}
                </button>
              </div>
            )}
          </article>
        ))}
      </div>
    </>
  );
}
