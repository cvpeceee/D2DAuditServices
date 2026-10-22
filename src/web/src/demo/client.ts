import harborline from "../../../seed/harborline.json";

export const company = harborline.company;

type SeedResource = {
  provider: string;
  kind: string;
  externalId: string;
  name: string;
  attributes: Record<string, string>;
};

const resources = harborline.resources as unknown as SeedResource[];

export function decisionFor(index: number) {
  if (index % 11 === 0) return "Revoked";
  if (index % 5 === 0) return "Approved";
  return "Pending";
}

function flag(resource: SeedResource, name: string) {
  return resource.attributes[name] ?? "";
}

function evaluateUsers(
  include: (resource: SeedResource) => boolean,
  attribute: string,
  expected: string,
  empty: string,
  failure: string,
) {
  const users = resources.filter((item) => item.kind === "user" && include(item));
  if (users.length === 0) return { outcome: "notRun" as const, detail: empty };
  const missing = users
    .filter((item) => flag(item, attribute) !== expected)
    .map((item) => item.name);
  return missing.length === 0
    ? { outcome: "pass" as const, detail: `${users.length} active users match.` }
    : { outcome: "fail" as const, detail: `${failure}: ${missing.join(", ")}.` };
}

function evaluateAll(
  kind: string,
  attribute: string,
  expected: string,
  empty: string,
  failure: string,
) {
  const rows = resources.filter((item) => item.kind === kind);
  if (rows.length === 0) return { outcome: "notRun" as const, detail: empty };
  const bad = rows.filter((item) => flag(item, attribute) !== expected).map((item) => item.name);
  return bad.length === 0
    ? { outcome: "pass" as const, detail: `${rows.length} ${kind} resources match.` }
    : { outcome: "fail" as const, detail: `${failure}: ${bad.join(", ")}.` };
}

export function evaluateCheck(key: string) {
  switch (key) {
    case "mfa-all-users":
      return evaluateUsers(
        (user) => flag(user, "status") !== "suspended" && flag(user, "offboarded") !== "true",
        "mfa",
        "true",
        "No active users were collected.",
        "These active users have no MFA",
      );
    case "root-mfa":
      return evaluateAll(
        "root",
        "mfa",
        "true",
        "No AWS root account was collected.",
        "Root MFA is off",
      );
    case "s3-public-access":
      return evaluateAll(
        "bucket",
        "public",
        "false",
        "No buckets were collected.",
        "Public access is not blocked",
      );
    case "cloudtrail-enabled": {
      const rows = resources.filter((item) => item.kind === "trail");
      if (rows.length === 0) {
        return { outcome: "notRun" as const, detail: "No CloudTrail trails were collected." };
      }
      return rows.some((item) => flag(item, "enabled") === "true")
        ? { outcome: "pass" as const, detail: "CloudTrail is logging." }
        : { outcome: "fail" as const, detail: "No trail has enabled true." };
    }
    case "encryption-at-rest":
      return evaluateAll(
        "bucket",
        "encrypted",
        "true",
        "No buckets were collected.",
        "Encryption at rest is off",
      );
    case "branch-protection":
      return evaluateAll(
        "repository",
        "protected",
        "true",
        "No repositories were collected.",
        "The default branch is not protected",
      );
    case "offboarded-removed":
      return evaluateLeavers();
    default:
      return { outcome: "notRun" as const, detail: "" };
  }
}

function evaluateLeavers() {
  const users = resources.filter((item) => item.kind === "user");
  if (users.length === 0) return { outcome: "notRun" as const, detail: "No users were collected." };
  const leaverEmails = new Set(
    users
      .filter((item) => flag(item, "offboarded") === "true")
      .map((item) => flag(item, "email").toLowerCase())
      .filter((email) => email.length > 0),
  );
  if (leaverEmails.size === 0) {
    return { outcome: "pass" as const, detail: "The directory has no leavers." };
  }
  const stillActive = [
    ...new Set(
      users
        .filter(
          (item) =>
            flag(item, "offboarded") !== "true" &&
            flag(item, "status") !== "suspended" &&
            leaverEmails.has(flag(item, "email").toLowerCase()),
        )
        .map((item) => item.name),
    ),
  ];
  return stillActive.length === 0
    ? {
        outcome: "pass" as const,
        detail: `${leaverEmails.size} leavers are suspended or removed.`,
      }
    : {
        outcome: "fail" as const,
        detail: `Leavers still have an active account: ${stillActive.join(", ")}.`,
      };
}

function sourceOf(providers: string[]) {
  const distinct = [...new Set(providers.map((item) => item.toLowerCase()))];
  if (distinct.length === 1 && distinct[0] === "google") return "Google";
  if (distinct.length === 1 && distinct[0] === "microsoft") return "Microsoft";
  return distinct.length === 0 ? "Manual" : "Mixed";
}

export function seedPeople(onDay: (offset: number) => string) {
  const profiles = new Map(harborline.people.map((person) => [person.email.toLowerCase(), person]));
  const grouped = new Map<
    string,
    {
      name: string;
      accounts: { provider: string; accountName: string; active: boolean }[];
      offboarded: boolean;
    }
  >();
  for (const resource of resources.filter((item) => item.kind === "user")) {
    const email = flag(resource, "email").toLowerCase();
    if (!email) continue;
    const row = grouped.get(email) ?? {
      name: resource.name,
      accounts: [],
      offboarded: false,
    };
    if (!row.name) row.name = resource.name;
    row.offboarded = row.offboarded || flag(resource, "offboarded") === "true";
    row.accounts.push({
      provider: resource.provider,
      accountName: resource.externalId,
      active: flag(resource, "status") === "active",
    });
    grouped.set(email, row);
  }
  return [...grouped.entries()]
    .map(([email, row]) => {
      const profile = profiles.get(email);
      return {
        email,
        name: row.name,
        department: profile?.department ?? "Workforce",
        role: profile?.role ?? "",
        hireOn: profile ? onDay(profile.hireOffset) : null,
        leftOn: profile?.leaveOffset != null ? onDay(profile.leaveOffset) : null,
        source: sourceOf(row.accounts.map((account) => account.provider)),
        state: row.offboarded ? "Offboarded" : "Active",
        training: profile?.training ?? "Assigned",
        trainingDueOn: onDay(profile?.trainingDueOffset ?? 20),
        accounts: [...row.accounts].sort((a, b) => a.provider.localeCompare(b.provider)),
      };
    })
    .sort((a, b) => a.name.localeCompare(b.name));
}

export function reviewAccounts() {
  return resources
    .filter((item) => item.kind === "user" && flag(item, "email"))
    .map((item) => ({
      personName: item.name,
      personEmail: flag(item, "email").toLowerCase(),
      provider: item.provider,
      accountName: item.externalId,
    }))
    .sort(
      (a, b) => a.personEmail.localeCompare(b.personEmail) || a.provider.localeCompare(b.provider),
    )
    .map((item, index) => ({ ...item, decision: decisionFor(index) }));
}

function attributeDetail(attributes: Record<string, string>) {
  const parts: string[] = [];
  for (const key of [
    "email",
    "status",
    "mfa",
    "public",
    "encrypted",
    "protected",
    "enabled",
    "offboarded",
  ]) {
    const value = attributes[key];
    if (value) parts.push(`${key} ${value}`);
  }
  return parts.join(", ");
}

export function resourceRows() {
  return [...resources]
    .map((item) => ({
      provider: item.provider,
      kind: item.kind,
      name: item.name,
      detail: attributeDetail(item.attributes),
    }))
    .sort(
      (a, b) =>
        a.provider.localeCompare(b.provider) ||
        a.kind.localeCompare(b.kind) ||
        a.name.localeCompare(b.name),
    );
}

export const seedRisks = harborline.risks;
export const seedVendors = harborline.vendors;
export const seedHistory = harborline.history;
export const seedNotifications = harborline.notifications;
export const seedDevices = harborline.devices;
export const seedActivity = harborline.activity;
export const seedFindings = harborline.findings;
export const seedRequests = harborline.requests;
export const seedEvidence = harborline.evidence;
export const seedControls = harborline.controls;
export const seedAccess = harborline.accessReview;
export const seedDirectory = harborline.people;
