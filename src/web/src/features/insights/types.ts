export type FrameworkPoint = {
  key: string;
  name: string;
  passing: number;
  total: number;
  percent: number;
};

export type TrendPoint = {
  day: string;
  controlsPassing: number;
  controlsTotal: number;
  percent: number;
  failingTests: number;
  vulnCritical: number;
  vulnHigh: number;
  vulnMedium: number;
  vulnLow: number;
  openTasks: number;
  overdueItems: number;
  frameworks: FrameworkPoint[];
};

export type TrendsResponse = { days: number; points: TrendPoint[] };

export type NotificationItem = {
  id: string;
  kind: string;
  title: string;
  body: string;
  href: string;
  at: string;
  read: boolean;
};

export type NotificationFeed = { unread: number; items: NotificationItem[] };

export type NotificationPreference = {
  kind: string;
  inApp: boolean;
  email: boolean;
  slack: boolean;
};

export type SearchResult = {
  kind: string;
  key: string;
  title: string;
  subtitle: string;
  href: string;
};

export type SearchResponse = { query: string; results: SearchResult[] };

export type ReportSchedule = {
  reportKey: string;
  frequency: string;
  format: string;
  recipients: string;
  enabled: boolean;
  nextRunOn: string | null;
  lastSentAt: string | null;
  lastDeliveryId: string | null;
};

export type DigestResult = { recipients: number; notifications: number; deliveryIds: string[] };

export const notificationKinds = [
  "test_failing",
  "task_assigned",
  "policy_ack",
  "review_due",
  "auditor_request",
  "vendor_overdue",
] as const;

export const reportKeys = ["board", "framework", "risk", "vendor", "personnel"] as const;
