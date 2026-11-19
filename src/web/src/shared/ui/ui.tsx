export function Ring({ value, tone }: { value: number; tone: string }) {
  const radius = 40;
  const circumference = 2 * Math.PI * radius;
  const dash = (circumference * Math.max(0, Math.min(100, value))) / 100;
  return (
    <div className="ring" role="img" aria-label={`${value}%`}>
      <svg width="92" height="92" aria-hidden="true">
        <circle cx="46" cy="46" r={radius} fill="none" stroke="var(--bg2)" strokeWidth="9" />
        <circle
          cx="46"
          cy="46"
          r={radius}
          fill="none"
          stroke={tone}
          strokeWidth="9"
          strokeLinecap="round"
          strokeDasharray={`${dash} ${circumference}`}
        />
      </svg>
      <b>{value}%</b>
    </div>
  );
}

export function Pill({ status, children }: { status?: string; children: string }) {
  const tone =
    status === "passing" || status === "pass" || status === "succeeded"
      ? "ok"
      : status === "failing" || status === "fail" || status === "failed"
        ? "bad"
        : status === "dueSoon" || status === "needsEvidence" || status === "warn"
          ? "warn"
          : status === "manual" || status === "notRun" || status === "info"
            ? "info"
            : "neutral";
  return <span className={`pill ${tone}`}>{children}</span>;
}

export function Skeleton({ className = "" }: { className?: string }) {
  return <div className={`skeleton ${className}`} />;
}

export function initials(name: string) {
  const parts = name.split(/\s+/).filter(Boolean);
  return ((parts[0]?.[0] ?? "") + (parts[1]?.[0] ?? "")).toUpperCase() || "•";
}

export function formatDate(value: string) {
  const date = new Date(`${value}T00:00:00`);
  return new Intl.DateTimeFormat(undefined, { dateStyle: "medium" }).format(date);
}

export function formatWhen(value: string) {
  return new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(
    new Date(value),
  );
}

export function daysUntil(value: string) {
  const target = new Date(`${value}T00:00:00`);
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  return Math.round((target.getTime() - today.getTime()) / 86_400_000);
}

export function frameworkTag(key: string) {
  if (key === "iso27001") return "ISO";
  if (key === "iso27017") return "27017";
  if (key === "iso27018") return "27018";
  if (key === "soc2") return "SOC 2";
  if (key === "hipaa") return "HIPAA";
  if (key === "gdpr") return "GDPR";
  if (key === "pci-dss") return "PCI 4.0";
  if (key === "nist-csf") return "NIST 2.0";
  if (key === "dora") return "DORA";
  if (key === "iso42001") return "ISO 42001";
  if (key === "nist-ai-rmf") return "AI RMF";
  if (key === "eu-ai-act") return "EU AI";
  if (key === "iec62443") return "IEC 62443";
  return key;
}

export function safeHref(value: string | null | undefined): string | null {
  if (!value) return null;
  try {
    const parsed = new URL(value.trim());
    if (parsed.protocol === "http:" || parsed.protocol === "https:") {
      return parsed.toString();
    }
    return null;
  } catch {
    return null;
  }
}
