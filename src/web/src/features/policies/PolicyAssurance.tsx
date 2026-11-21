import { useEffect, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { ApiError, api } from "../../shared/api/client";
import { Pill } from "../../shared/ui/ui";

type DiffLine = {
  kind: "same" | "added" | "removed";
  text: string;
  oldNumber: number | null;
  newNumber: number | null;
};
type Diff = {
  code: string;
  from: number;
  to: number;
  added: number;
  removed: number;
  lines: DiffLine[];
};
type Campaign = {
  id: string;
  policyCode: string;
  versionNumber: number;
  group: string;
  startedOn: string;
  dueOn: string;
  renewOn: string;
  status: "current" | "renewalDue" | "overdue" | "renewed";
  attested: number;
  total: number;
  people: { personName: string; personEmail: string; attestedAt: string | null }[];
};

const tone: Record<string, string> = {
  current: "pass",
  renewalDue: "warn",
  overdue: "fail",
  renewed: "info",
};

export function PolicyAssurance({
  tenantId,
  code,
  versions,
  canWrite,
  userEmail,
}: {
  tenantId: string | null;
  code: string;
  versions: number[];
  canWrite: boolean;
  userEmail: string | null;
}) {
  const { t } = useTranslation();
  const sorted = [...versions].sort((a, b) => a - b);
  const [from, setFrom] = useState(sorted.at(-2) ?? sorted[0] ?? 1);
  const [to, setTo] = useState(sorted.at(-1) ?? 1);
  const [diff, setDiff] = useState<Diff | null>(null);
  const [campaigns, setCampaigns] = useState<Campaign[]>([]);
  const [group, setGroup] = useState("Everyone");
  const [dueOn, setDueOn] = useState(
    new Date(Date.now() + 30 * 86_400_000).toISOString().slice(0, 10),
  );
  const [error, setError] = useState<string | null>(null);
  const base = `/api/v1/policies/${encodeURIComponent(code)}`;

  const loadCampaigns = () => {
    if (!tenantId) return;
    void api<Campaign[]>(`${base}/attestations`, { tenantId })
      .then(setCampaigns)
      .catch(() => setCampaigns([]));
  };

  useEffect(() => {
    setFrom(sorted.at(-2) ?? sorted[0] ?? 1);
    setTo(sorted.at(-1) ?? 1);
    setDiff(null);
    setError(null);
    loadCampaigns();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId, code, versions.length]);

  const run = async (action: () => Promise<void>) => {
    setError(null);
    try {
      await action();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.code : "save");
    }
  };

  const compare = () =>
    void run(async () => {
      setDiff(await api<Diff>(`${base}/diff?from=${from}&to=${to}`, { tenantId }));
    });

  const start = (event: FormEvent) => {
    event.preventDefault();
    void run(async () => {
      await api(`${base}/attestations`, { method: "POST", tenantId, body: { group, dueOn } });
      loadCampaigns();
    });
  };

  const renew = (id: string) =>
    void run(async () => {
      await api(`${base}/attestations/${id}/renew`, { method: "POST", tenantId, body: {} });
      loadCampaigns();
    });

  const attest = () =>
    void run(async () => {
      await api(`${base}/attest`, { method: "POST", tenantId, body: {} });
      loadCampaigns();
    });

  const pending = campaigns.some((campaign) =>
    campaign.people.some(
      (person) =>
        !person.attestedAt && person.personEmail.toLowerCase() === userEmail?.toLowerCase(),
    ),
  );

  return (
    <section className="stack">
      {sorted.length > 1 && (
        <>
          <h3 style={{ marginTop: 16 }}>{t("assurance.policies.compare")}</h3>
          <div className="act">
            <select
              aria-label={t("assurance.policies.from")}
              value={from}
              onChange={(event) => setFrom(Number(event.target.value))}
            >
              {sorted.map((number) => (
                <option key={number} value={number}>
                  v{number}
                </option>
              ))}
            </select>
            <select
              aria-label={t("assurance.policies.to")}
              value={to}
              onChange={(event) => setTo(Number(event.target.value))}
            >
              {sorted.map((number) => (
                <option key={number} value={number}>
                  v{number}
                </option>
              ))}
            </select>
            <button className="btn" type="button" onClick={compare}>
              {t("assurance.policies.showDiff")}
            </button>
          </div>
          {diff && (
            <div aria-label={t("assurance.policies.diff")}>
              <div className="m">
                {t("assurance.policies.diffSummary", {
                  from: diff.from,
                  to: diff.to,
                  added: diff.added,
                  removed: diff.removed,
                })}
              </div>
              <pre
                style={{
                  whiteSpace: "pre-wrap",
                  maxHeight: 320,
                  overflow: "auto",
                  fontSize: 12,
                  margin: 0,
                }}
              >
                {diff.lines.map((line, index) => (
                  <div
                    key={index}
                    data-kind={line.kind}
                    style={{
                      background:
                        line.kind === "added"
                          ? "rgba(46, 160, 67, 0.15)"
                          : line.kind === "removed"
                            ? "rgba(248, 81, 73, 0.15)"
                            : undefined,
                    }}
                  >
                    {line.kind === "added" ? "+ " : line.kind === "removed" ? "- " : "  "}
                    {line.text}
                  </div>
                ))}
              </pre>
            </div>
          )}
        </>
      )}
      <h3 style={{ marginTop: 16 }}>{t("assurance.policies.attestations")}</h3>
      {pending && (
        <div>
          <button className="btn primary" type="button" onClick={attest}>
            {t("assurance.policies.attest")}
          </button>
        </div>
      )}
      {campaigns.length === 0 && <div className="m">{t("assurance.policies.noCampaigns")}</div>}
      {campaigns.map((campaign) => (
        <div key={campaign.id} className="card" style={{ padding: 10 }}>
          <div>
            <strong>{campaign.group}</strong> · v{campaign.versionNumber} ·{" "}
            <Pill status={tone[campaign.status]}>
              {t(`assurance.policies.status.${campaign.status}`)}
            </Pill>
          </div>
          <div className="m">
            {t("assurance.policies.progress", {
              attested: campaign.attested,
              total: campaign.total,
              due: campaign.dueOn,
              renew: campaign.renewOn,
            })}
          </div>
          {canWrite && campaign.status !== "renewed" && (
            <button className="btn" type="button" onClick={() => renew(campaign.id)}>
              {t("assurance.policies.renew")}
            </button>
          )}
        </div>
      ))}
      {canWrite && (
        <form className="act" onSubmit={start}>
          <input
            aria-label={t("assurance.policies.group")}
            placeholder={t("assurance.policies.group")}
            value={group}
            onChange={(event) => setGroup(event.target.value)}
          />
          <input
            aria-label={t("assurance.policies.dueOn")}
            type="date"
            value={dueOn}
            onChange={(event) => setDueOn(event.target.value)}
          />
          <button className="btn" type="submit">
            {t("assurance.policies.start")}
          </button>
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
