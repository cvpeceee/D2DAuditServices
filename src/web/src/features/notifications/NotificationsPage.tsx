import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { ApiError, api } from "../../shared/api/client";
import { EmptyState, ErrorState, LoadingState } from "../../shared/ui/states";
import { formatWhen } from "../../shared/ui/ui";
import { useAuth } from "../auth/AuthContext";
import {
  notificationKinds,
  type DigestResult,
  type NotificationFeed,
  type NotificationPreference,
} from "../insights/types";
import { notificationEvent } from "./NotificationBell";

export function NotificationsPage() {
  const { t } = useTranslation();
  const { tenantId, tenants } = useAuth();
  const role = tenants.find((item) => item.tenantId === tenantId)?.role;
  const [feed, setFeed] = useState<NotificationFeed | null>(null);
  const [prefs, setPrefs] = useState<NotificationPreference[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => {
    if (!tenantId) return;
    setError(null);
    void api<NotificationFeed>("/api/v1/notifications/feed?take=100", { tenantId })
      .then(setFeed)
      .catch((caught: unknown) => setError(caught instanceof ApiError ? caught.code : "load"));
    void api<NotificationPreference[]>("/api/v1/notifications/preferences", { tenantId })
      .then(setPrefs)
      .catch(() => setPrefs([]));
  }, [tenantId]);

  useEffect(() => {
    load();
  }, [load]);

  async function markRead(id: string) {
    await api(`/api/v1/notifications/${encodeURIComponent(id)}/read`, {
      method: "POST",
      tenantId,
    });
    window.dispatchEvent(new Event(notificationEvent));
    load();
  }

  async function markAll() {
    await api("/api/v1/notifications/read-all", { method: "POST", tenantId });
    window.dispatchEvent(new Event(notificationEvent));
    load();
  }

  function toggle(kind: string, channel: "inApp" | "email" | "slack") {
    setPrefs((current) =>
      (current ?? []).map((item) =>
        item.kind === kind ? { ...item, [channel]: !item[channel] } : item,
      ),
    );
  }

  async function savePrefs() {
    if (!prefs) return;
    setBusy(true);
    setNotice(null);
    try {
      const saved = await api<NotificationPreference[]>("/api/v1/notifications/preferences", {
        method: "PUT",
        tenantId,
        body: { preferences: prefs },
      });
      setPrefs(saved);
      setNotice(t("notifications.saved"));
      window.dispatchEvent(new Event(notificationEvent));
      load();
    } catch {
      setNotice(t("errors.load"));
    } finally {
      setBusy(false);
    }
  }

  async function sendDigest() {
    setBusy(true);
    setNotice(null);
    try {
      const result = await api<DigestResult>("/api/v1/notifications/digest", {
        method: "POST",
        tenantId,
      });
      setNotice(
        t("notifications.digestSent", {
          recipients: result.recipients,
          count: result.notifications,
        }),
      );
    } catch {
      setNotice(t("errors.load"));
    } finally {
      setBusy(false);
    }
  }

  const rows =
    prefs && prefs.length > 0
      ? prefs
      : notificationKinds.map((kind) => ({ kind, inApp: true, email: false, slack: false }));

  return (
    <>
      <div className="head">
        <div>
          <h1>{t("notifications.title")}</h1>
          <div className="sub">{t("notifications.subtitle")}</div>
        </div>
        <div className="act">
          {feed && feed.unread > 0 && (
            <button className="btn" type="button" onClick={() => void markAll()}>
              {t("notifications.markAll")}
            </button>
          )}
          {role === "Admin" && (
            <button
              className="btn primary"
              type="button"
              disabled={busy}
              onClick={() => void sendDigest()}
            >
              {t("notifications.sendDigest")}
            </button>
          )}
        </div>
      </div>
      {notice && (
        <div className="local-banner" role="status">
          {notice}
        </div>
      )}
      <div className="card">
        <h3>{t("notifications.inbox")}</h3>
        {error ? (
          <ErrorState code={error} onRetry={load} />
        ) : !feed ? (
          <LoadingState rows={2} />
        ) : feed.items.length === 0 ? (
          <EmptyState>{t("shell.notificationsEmpty")}</EmptyState>
        ) : (
          <ul className="note-list page">
            {feed.items.map((item) => (
              <li key={item.id} className={item.read ? "" : "unread"}>
                <div>
                  <b>
                    {!item.read && <span className="sr-only">{t("notifications.unread")} </span>}
                    {item.href.startsWith("/") ? (
                      <Link to={item.href} onClick={() => void markRead(item.id)}>
                        {item.title}
                      </Link>
                    ) : (
                      item.title
                    )}
                  </b>
                  <div className="m">{item.body}</div>
                  <small className="m">
                    {t(`notifications.kinds.${item.kind}`, { defaultValue: item.kind })} ·{" "}
                    {formatWhen(item.at)}
                  </small>
                </div>
                {!item.read && (
                  <button className="btn" type="button" onClick={() => void markRead(item.id)}>
                    {t("notifications.markRead")}
                  </button>
                )}
              </li>
            ))}
          </ul>
        )}
      </div>
      <div className="card" style={{ marginTop: 14 }}>
        <h3>{t("notifications.preferences")}</h3>
        <p className="m">{t("notifications.preferencesHint")}</p>
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">{t("notifications.event")}</th>
                <th scope="col">{t("notifications.inApp")}</th>
                <th scope="col">{t("notifications.email")}</th>
                <th scope="col">{t("notifications.slack")}</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((item) => {
                const name = t(`notifications.kinds.${item.kind}`, { defaultValue: item.kind });
                return (
                  <tr key={item.kind}>
                    <th scope="row">{name}</th>
                    {(["inApp", "email", "slack"] as const).map((channel) => (
                      <td key={channel}>
                        <input
                          type="checkbox"
                          aria-label={`${name}: ${t(`notifications.${channel}`)}`}
                          checked={item[channel]}
                          disabled={!prefs}
                          onChange={() => toggle(item.kind, channel)}
                        />
                      </td>
                    ))}
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        <div style={{ marginTop: 10 }}>
          <button
            className="btn primary"
            type="button"
            disabled={busy || !prefs}
            onClick={() => void savePrefs()}
          >
            {t("notifications.savePreferences")}
          </button>
        </div>
      </div>
    </>
  );
}
