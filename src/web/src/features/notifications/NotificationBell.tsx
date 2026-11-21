import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useNavigate } from "react-router-dom";
import { api } from "../../shared/api/client";
import { Icon } from "../../shared/ui/icons";
import { formatWhen } from "../../shared/ui/ui";
import type { NotificationFeed } from "../insights/types";

export const notificationEvent = "d2d-notification";

export function NotificationBell({ tenantId }: { tenantId: string | null }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const [feed, setFeed] = useState<NotificationFeed>({ unread: 0, items: [] });
  const wrap = useRef<HTMLDivElement>(null);

  const load = useCallback(() => {
    if (!tenantId) return;
    void api<NotificationFeed>("/api/v1/notifications/feed?take=15", { tenantId })
      .then(setFeed)
      .catch(() => undefined);
  }, [tenantId]);

  useEffect(() => {
    load();
    const timer = window.setInterval(load, 60_000);
    window.addEventListener(notificationEvent, load);
    return () => {
      window.clearInterval(timer);
      window.removeEventListener(notificationEvent, load);
    };
  }, [load]);

  useEffect(() => {
    if (!open) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") setOpen(false);
    };
    const onClick = (event: MouseEvent) => {
      if (wrap.current && !wrap.current.contains(event.target as Node)) setOpen(false);
    };
    window.addEventListener("keydown", onKey);
    window.addEventListener("mousedown", onClick);
    return () => {
      window.removeEventListener("keydown", onKey);
      window.removeEventListener("mousedown", onClick);
    };
  }, [open]);

  async function markRead(id: string) {
    setFeed((current) => ({
      unread: Math.max(
        0,
        current.unread - (current.items.find((i) => i.id === id && !i.read) ? 1 : 0),
      ),
      items: current.items.map((item) => (item.id === id ? { ...item, read: true } : item)),
    }));
    await api(`/api/v1/notifications/${encodeURIComponent(id)}/read`, {
      method: "POST",
      tenantId,
    }).catch(() => undefined);
  }

  async function markAll() {
    setFeed((current) => ({
      unread: 0,
      items: current.items.map((item) => ({ ...item, read: true })),
    }));
    await api("/api/v1/notifications/read-all", { method: "POST", tenantId }).catch(
      () => undefined,
    );
  }

  const label =
    feed.unread > 0
      ? t("notifications.bellUnread", { count: feed.unread })
      : t("shell.notifications");

  return (
    <div className="note-wrap" ref={wrap}>
      <button
        className="btn icon-btn"
        type="button"
        aria-label={label}
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={() => {
          setOpen((value) => !value);
          if (!open) load();
        }}
        style={{ position: "relative" }}
      >
        <Icon name="bell" size={16} />
        {feed.unread > 0 && (
          <span className="badge-count" aria-hidden="true">
            {feed.unread > 99 ? "99+" : feed.unread}
          </span>
        )}
      </button>
      {open && (
        <div className="note-pop" role="dialog" aria-label={t("shell.notifications")}>
          <div className="note-head">
            <b>{t("shell.notifications")}</b>
            {feed.unread > 0 && (
              <button type="button" className="linkish" onClick={() => void markAll()}>
                {t("notifications.markAll")}
              </button>
            )}
          </div>
          {feed.items.length === 0 ? (
            <div className="m" style={{ padding: 8 }}>
              {t("shell.notificationsEmpty")}
            </div>
          ) : (
            <ul className="note-list">
              {feed.items.map((note) => (
                <li key={note.id}>
                  <button
                    className={`note-item ${note.read ? "" : "unread"}`}
                    type="button"
                    onClick={() => {
                      setOpen(false);
                      void markRead(note.id);
                      if (note.href.startsWith("/")) void navigate(note.href);
                    }}
                  >
                    <b>
                      {!note.read && <span className="sr-only">{t("notifications.unread")} </span>}
                      {note.title}
                    </b>
                    <span>{note.body}</span>
                    <small className="m">{formatWhen(note.at)}</small>
                  </button>
                </li>
              ))}
            </ul>
          )}
          <div className="note-foot">
            <Link to="/notifications" onClick={() => setOpen(false)}>
              {t("notifications.viewAll")}
            </Link>
          </div>
        </div>
      )}
    </div>
  );
}
