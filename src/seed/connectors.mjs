// Generates connectors.json: the integration catalog and deterministic mock fixtures.
// Run with: node src/seed/connectors.mjs
import { readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = dirname(fileURLToPath(import.meta.url));
const client = JSON.parse(readFileSync(join(root, "client.json"), "utf8"));
const domain = client.company.domain;
const slug = domain.split(".")[0];

const people = client.people;
const actives = people.filter((p) => p.leaveOffset === null || p.leaveOffset === undefined);
const leavers = people.filter((p) => p.leaveOffset !== null && p.leaveOffset !== undefined);
const devices = client.devices;

const take = (list, start, count) => list.slice(start, start + count);
const pad = (n, width = 4) => String(n).padStart(width, "0");
const bool = (v) => (v ? "true" : "false");

function field(key, label, opts = {}) {
  return {
    key,
    label,
    secret: Boolean(opts.secret),
    required: opts.required !== false,
    placeholder: opts.placeholder ?? "",
  };
}
const secret = (key, label, placeholder = "") => field(key, label, { secret: true, placeholder });
const optional = (key, label, placeholder = "") => field(key, label, { required: false, placeholder });

function users(provider, list, opts = {}) {
  return list.map((p, i) => {
    const leaver = p.leaveOffset !== null && p.leaveOffset !== undefined;
    return {
      provider,
      kind: "user",
      externalId: `${provider}:${p.email}`,
      name: p.name,
      attributes: {
        email: p.email,
        mfa: bool(leaver || !(opts.noMfa ?? []).includes(i)),
        status: leaver ? "suspended" : "active",
        offboarded: bool(leaver),
        department: p.department,
        role: p.role,
        ...(opts.extra ? opts.extra(p, i) : {}),
      },
    };
  });
}

function employees(provider, list) {
  return list.map((p) => {
    const leaver = p.leaveOffset !== null && p.leaveOffset !== undefined;
    return {
      provider,
      kind: "employee",
      externalId: `${provider}:emp-${p.email.split("@")[0]}`,
      name: p.name,
      attributes: {
        email: p.email,
        status: leaver ? "terminated" : "active",
        department: p.department,
        title: p.role,
        tenureDays: String(Math.abs(p.hireOffset) - (leaver ? Math.abs(p.leaveOffset) : 0)),
        terminatedDaysAgo: leaver ? String(Math.abs(p.leaveOffset)) : "",
      },
    };
  });
}

function agents(provider, list, unhealthy = [], extra = () => ({})) {
  return list.map((d, i) => ({
    provider,
    kind: "edrAgent",
    externalId: `${provider}:${d.serial}`,
    name: d.serial,
    attributes: {
      email: d.ownerEmail,
      owner: d.ownerName,
      platform: d.platform,
      healthy: bool(!unhealthy.includes(i)),
      prevention: bool(!unhealthy.includes(i)),
      lastSeenDaysAgo: String(Math.abs(d.seenOffset) + (unhealthy.includes(i) ? 21 : 0)),
      ...extra(d, i),
    },
  }));
}

function managedDevices(provider, list, unencrypted = []) {
  return list.map((d, i) => ({
    provider,
    kind: "device",
    externalId: `${provider}:${d.serial}`,
    name: d.serial,
    attributes: {
      email: d.ownerEmail,
      owner: d.ownerName,
      platform: d.platform,
      encrypted: bool(!unencrypted.includes(i)),
      screenLock: "true",
      osUpToDate: bool(i % 7 !== 3),
    },
  }));
}

const sla = { critical: 15, high: 30, medium: 90, low: 180 };
function vulns(provider, rows) {
  return rows.map(([id, title, severity, asset, ageDays, status, cve]) => ({
    provider,
    kind: "vulnerability",
    externalId: `${provider}:${id}`,
    name: title,
    attributes: {
      severity,
      asset,
      status,
      cve: cve ?? "",
      ageDays: String(ageDays),
      slaDays: String(sla[severity]),
      withinSla: bool(status !== "open" || ageDays <= sla[severity]),
    },
  }));
}

const repoNames = [
  "harborline-engine",
  "harborline-coordinator",
  "harborline-ingest",
  "harborline-infra",
  "harborline-docs",
  "harborline-web",
  "harborline-mobile",
  "harborline-billing",
];

function repos(provider, names, unprotected = []) {
  return names.map((name, i) => ({
    provider,
    kind: "repository",
    externalId: `${provider}:${name}`,
    name,
    attributes: {
      protected: bool(!unprotected.includes(i)),
      defaultBranch: "main",
      requiredApprovals: unprotected.includes(i) ? "0" : "1",
      visibility: "private",
    },
  }));
}

function changes(provider, prefix, titles, unapproved = []) {
  return titles.map((title, i) => ({
    provider,
    kind: "change",
    externalId: `${provider}:${prefix}${pad(1001 + i)}`,
    name: `${prefix}${pad(1001 + i)} ${title}`,
    attributes: {
      approved: bool(!unapproved.includes(i)),
      approver: unapproved.includes(i) ? "" : actives[(i * 7) % 40].email,
      risk: ["low", "moderate", "high"][i % 3],
      state: i % 4 === 3 ? "scheduled" : "closed",
    },
  }));
}

function vault(provider, list, noMfa = []) {
  return list.map((p, i) => {
    const leaver = p.leaveOffset !== null && p.leaveOffset !== undefined;
    return {
      provider,
      kind: "vaultMember",
      externalId: `${provider}:${p.email}`,
      name: p.name,
      attributes: {
        email: p.email,
        mfa: bool(!noMfa.includes(i)),
        status: leaver ? "suspended" : "active",
        role: i === 0 ? "owner" : i < 3 ? "admin" : "member",
      },
    };
  });
}

function training(provider, list, course) {
  return list.map((p) => ({
    provider,
    kind: "training",
    externalId: `${provider}:${p.email}`,
    name: p.name,
    attributes: {
      email: p.email,
      course,
      completed: bool(p.training === "Complete"),
      state: p.training,
      status: "active",
    },
  }));
}

const catalog = [];
function add(entry) {
  catalog.push({
    builtin: false,
    managedAt: null,
    envKeys: [],
    fixtures: [],
    harborline: false,
    ...entry,
  });
}

// ----- Built-in providers (existing flows) -----
const builtin = (key, name, category, authType, resources, tests, controls, managedAt, envKeys, docsUrl) =>
  add({
    key,
    name,
    category,
    authType,
    description: `${name} is collected by the existing ${managedAt === "/integrations" ? "integration flow" : "module"}.`,
    fields: [],
    resources,
    tests,
    controls,
    docsUrl,
    builtin: true,
    managedAt,
    envKeys,
  });

builtin("aws", "Amazon Web Services", "cloud", "role", ["root", "bucket", "trail", "iam-user"], ["root-mfa", "s3-public-access", "cloudtrail-enabled", "encryption-at-rest"], ["AC-07", "CFG-01", "LG-03", "CR-02"], "/integrations", ["AWS_COLLECTOR_ROLE_ARN"], "https://docs.aws.amazon.com/IAM/latest/UserGuide/id_roles.html");
builtin("azure", "Microsoft Azure", "cloud", "role", ["bucket", "keyVault", "trail", "network", "user"], ["azure-storage-public", "azure-key-vault", "azure-activity-log", "azure-network-rules", "entra-mfa"], ["CFG-01", "CR-02", "LG-03", "AC-07"], "/integrations", [], "https://learn.microsoft.com/azure/role-based-access-control/");
builtin("gcp", "Google Cloud", "cloud", "role", ["bucket", "kms", "trail", "iam"], ["gcp-bucket-public", "gcp-kms", "gcp-audit-log", "gcp-iam-public"], ["CFG-01", "CR-02", "LG-03", "AC-07"], "/integrations", [], "https://cloud.google.com/iam/docs/service-account-overview");
builtin("google", "Google Workspace", "identity", "oauth", ["user"], ["mfa-all-users", "offboarded-removed"], ["AC-07", "HR-08"], "/integrations", ["GOOGLE_CLIENT_ID", "GOOGLE_CLIENT_SECRET"], "https://developers.google.com/admin-sdk/directory");
builtin("microsoft", "Microsoft 365 / Entra ID", "identity", "oauth", ["user"], ["mfa-all-users", "offboarded-removed"], ["AC-07", "HR-08"], "/integrations", ["MS_CLIENT_ID", "MS_CLIENT_SECRET"], "https://learn.microsoft.com/graph/api/resources/user");
builtin("github", "GitHub", "code", "oauth", ["repository", "user"], ["branch-protection", "mfa-all-users"], ["CM-11", "AC-07"], "/integrations", ["GITHUB_APP_SLUG", "GITHUB_APP_ID", "GITHUB_APP_PRIVATE_KEY"], "https://docs.github.com/rest");
builtin("jamf", "Jamf Pro", "endpoint", "apiKey", ["device"], [], ["EP-03", "EP-08"], "/people", ["JAMF_BASE_URL", "JAMF_TOKEN"], "https://developer.jamf.com/jamf-pro/reference");
builtin("intune", "Microsoft Intune", "endpoint", "oauth", ["device"], [], ["EP-03", "EP-08"], "/people", ["MS_CLIENT_ID", "MS_CLIENT_SECRET"], "https://learn.microsoft.com/graph/api/resources/intune-devices-manageddevice");
builtin("kandji", "Kandji", "endpoint", "apiKey", ["device"], [], ["EP-03", "EP-08"], "/people", ["KANDJI_BASE_URL", "KANDJI_TOKEN"], "https://api-docs.kandji.io/");
builtin("jira", "Jira", "ticketing", "apiKey", ["ticket"], [], ["VM-02", "IR-02"], "/automations", ["JIRA_BASE_URL", "JIRA_EMAIL", "JIRA_API_TOKEN"], "https://developer.atlassian.com/cloud/jira/platform/rest/v3/");
builtin("linear", "Linear", "ticketing", "apiKey", ["ticket"], [], ["VM-02"], "/automations", ["LINEAR_API_KEY"], "https://developers.linear.app/docs/graphql/working-with-the-graphql-api");
builtin("slack", "Slack", "communication", "oauth", ["message"], [], ["LG-08"], "/automations", ["SLACK_WEBHOOK_URL"], "https://api.slack.com/messaging/webhooks");
builtin("teams", "Microsoft Teams", "communication", "oauth", ["message"], [], ["LG-08"], "/automations", ["TEAMS_WEBHOOK_URL"], "https://learn.microsoft.com/microsoftteams/platform/webhooks-and-connectors/");
builtin("knowbe4", "KnowBe4", "training", "apiKey", ["training", "phishing"], [], ["HR-06"], "/governance", ["KNOWBE4_API_KEY", "KNOWBE4_BASE_URL"], "https://developer.knowbe4.com/rest/reporting");
builtin("checkr", "Checkr", "background", "apiKey", ["backgroundCheck"], [], ["HR-01"], "/governance", ["CHECKR_API_KEY", "CHECKR_BASE_URL"], "https://docs.checkr.com/");

// ----- Identity / SSO -----
add({
  key: "okta",
  name: "Okta",
  category: "identity",
  authType: "apiKey",
  description: "Workforce directory users, MFA factor enrolment and lifecycle status.",
  fields: [field("domain", "Okta domain", { placeholder: `${slug}.okta.com` }), secret("apiToken", "API token (SSWS)")],
  resources: ["user"],
  tests: ["mfa-all-users", "offboarded-removed", "hris-leavers-deprovisioned"],
  controls: ["AC-07", "HR-08"],
  docsUrl: "https://developer.okta.com/docs/reference/api/users/",
  harborline: true,
  fixtures: users("okta", [...take(actives, 0, 44), ...take(leavers, 0, 6)], {
    extra: (p, i) => ({ factor: i % 3 === 0 ? "okta_verify" : i % 3 === 1 ? "webauthn" : "totp" }),
  }),
});
add({
  key: "jumpcloud",
  name: "JumpCloud",
  category: "identity",
  authType: "apiKey",
  description: "Directory users with MFA status plus managed systems with disk encryption.",
  fields: [secret("apiKey", "API key"), optional("orgId", "Organization ID (multi-tenant admins)")],
  resources: ["user", "device"],
  tests: ["mfa-all-users", "offboarded-removed", "endpoint-disk-encryption"],
  controls: ["AC-07", "HR-08", "EP-03"],
  docsUrl: "https://docs.jumpcloud.com/api/1.0/index.html",
  fixtures: [
    ...users("jumpcloud", [...take(actives, 60, 20), ...take(leavers, 6, 2)], { noMfa: [7] }),
    ...managedDevices("jumpcloud", take(devices, 60, 12), [5]),
  ],
});
add({
  key: "onelogin",
  name: "OneLogin",
  category: "identity",
  authType: "oauth",
  description: "Users, OTP enrolment and account state from the OneLogin API v2.",
  fields: [
    field("subdomain", "Subdomain", { placeholder: slug }),
    field("clientId", "Client ID"),
    secret("clientSecret", "Client secret"),
  ],
  resources: ["user"],
  tests: ["mfa-all-users", "offboarded-removed"],
  controls: ["AC-07", "HR-08"],
  docsUrl: "https://developers.onelogin.com/api-docs/2/users/list-users",
  fixtures: users("onelogin", [...take(actives, 90, 16), ...take(leavers, 8, 2)]),
});
add({
  key: "auth0",
  name: "Auth0",
  category: "identity",
  authType: "oauth",
  description: "Workforce tenant users with multifactor enrolment and blocked state.",
  fields: [
    field("domain", "Tenant domain", { placeholder: `${slug}.us.auth0.com` }),
    field("clientId", "M2M client ID"),
    secret("clientSecret", "M2M client secret"),
  ],
  resources: ["user"],
  tests: ["mfa-all-users", "offboarded-removed"],
  controls: ["AC-07", "HR-08"],
  docsUrl: "https://auth0.com/docs/api/management/v2/users/get-users",
  fixtures: users("auth0", [...take(actives, 120, 12), ...take(leavers, 10, 1)]),
});

// ----- HRIS -----
const hris = (key, name, fields, docsUrl, start, count, leaverStart, leaverCount, harborline = false, description = "") =>
  add({
    key,
    name,
    category: "hris",
    authType: fields.some((f) => f.key === "clientSecret") ? "oauth" : "apiKey",
    description: description || `Employee roster, employment status and termination dates from ${name}.`,
    fields,
    resources: ["employee"],
    tests: ["hris-leavers-deprovisioned"],
    controls: ["HR-08", "HR-01"],
    docsUrl,
    harborline,
    fixtures: employees(key, [...take(actives, start, count), ...take(leavers, leaverStart, leaverCount)]),
  });
hris("bamboohr", "BambooHR", [field("subdomain", "Company subdomain", { placeholder: slug }), secret("apiKey", "API key")], "https://documentation.bamboohr.com/reference/request-custom-report-1", 0, 72, 0, 8, true);
hris("rippling", "Rippling", [secret("apiToken", "API token")], "https://developer.rippling.com/documentation/base-api/reference/list-employees-including-terminated", 140, 30, 12, 3);
hris("gusto", "Gusto", [field("companyId", "Company UUID"), secret("accessToken", "Access token")], "https://docs.gusto.com/app-integrations/reference/get-v1-companies-company_id-employees", 170, 25, 15, 2);
hris("workday", "Workday", [field("reportUrl", "RaaS report URL (JSON)", { placeholder: "https://wd5-services1.myworkday.com/ccx/service/customreport2/..." }), field("username", "Integration system user"), secret("password", "Password")], "https://doc.workday.com/admin-guide/en-us/integrations/reports-as-a-service.html", 195, 30, 17, 3, false, "Worker roster from a Workday Report-as-a-Service (RaaS) custom report.");
hris("hibob", "HiBob", [field("serviceUserId", "Service user ID"), secret("serviceUserToken", "Service user token")], "https://apidocs.hibob.com/reference/post_people-search", 225, 25, 20, 2);
hris("deel", "Deel", [secret("apiToken", "API token")], "https://developer.deel.com/reference/getpeople", 250, 20, 22, 2);

// ----- MDM / EDR -----
add({
  key: "crowdstrike",
  name: "CrowdStrike Falcon",
  category: "endpoint",
  authType: "oauth",
  description: "Falcon sensor inventory, prevention policy and last-seen time per host.",
  fields: [
    optional("baseUrl", "API base URL", "https://api.crowdstrike.com"),
    field("clientId", "API client ID"),
    secret("clientSecret", "API client secret"),
  ],
  resources: ["edrAgent"],
  tests: ["endpoint-edr"],
  controls: ["MAL-01"],
  docsUrl: "https://falcon.crowdstrike.com/documentation/page/c0b16f1b/host-and-host-group-management-apis",
  harborline: true,
  fixtures: agents("crowdstrike", take(devices, 0, 40), [17], (d) => ({ sensorVersion: "7.18.18908" })),
});
add({
  key: "sentinelone",
  name: "SentinelOne",
  category: "endpoint",
  authType: "apiKey",
  description: "Singularity agents, protection state and active threats.",
  fields: [field("consoleUrl", "Console URL", { placeholder: `https://${slug}.sentinelone.net` }), secret("apiToken", "API token")],
  resources: ["edrAgent", "alert"],
  tests: ["endpoint-edr", "alerts-triaged"],
  controls: ["MAL-01", "LG-08"],
  docsUrl: "https://usea1-partners.sentinelone.net/api-doc/overview",
  fixtures: [
    ...agents("sentinelone", take(devices, 100, 18)),
    {
      provider: "sentinelone",
      kind: "alert",
      externalId: "sentinelone:threat-5501",
      name: "Potentially unwanted application quarantined",
      attributes: { severity: "medium", triaged: "true", status: "resolved", ageHours: "30" },
    },
  ],
});
add({
  key: "defender",
  name: "Microsoft Defender for Endpoint",
  category: "endpoint",
  authType: "oauth",
  description: "Onboarded machines, health status and exposure level.",
  fields: [field("tenantId", "Entra tenant ID"), field("clientId", "App (client) ID"), secret("clientSecret", "Client secret")],
  resources: ["edrAgent"],
  tests: ["endpoint-edr"],
  controls: ["MAL-01"],
  docsUrl: "https://learn.microsoft.com/defender-endpoint/api/get-machines",
  fixtures: agents("defender", take(devices.filter((d) => d.platform === "Windows"), 0, 20), [], () => ({ exposure: "Low" })),
});
add({
  key: "mosyle",
  name: "Mosyle",
  category: "endpoint",
  authType: "apiKey",
  description: "Apple device inventory with FileVault and passcode state.",
  fields: [secret("accessToken", "Access token"), field("email", "Admin email"), secret("password", "Admin password")],
  resources: ["device"],
  tests: ["endpoint-disk-encryption"],
  controls: ["EP-03", "EP-08"],
  docsUrl: "https://businessapi.mosyle.com/",
  fixtures: managedDevices("mosyle", take(devices.filter((d) => d.platform === "macOS"), 40, 16), [9]),
});

// ----- Cloud -----
add({
  key: "cloudflare",
  name: "Cloudflare",
  category: "cloud",
  authType: "apiKey",
  description: "Zones with TLS mode and Always Use HTTPS, plus account members with 2FA.",
  fields: [secret("apiToken", "API token"), field("accountId", "Account ID")],
  resources: ["domain", "user"],
  tests: ["tls-enforced", "mfa-all-users"],
  controls: ["CR-05", "AC-07"],
  docsUrl: "https://developers.cloudflare.com/api/resources/zones/methods/list/",
  harborline: true,
  fixtures: [
    ...[domain, `api.${domain}`, `status.${domain}`, `docs.${domain}`, `app.${domain}`].map((zone) => ({
      provider: "cloudflare",
      kind: "domain",
      externalId: `cloudflare:${zone}`,
      name: zone,
      attributes: { tls: "true", sslMode: "strict", alwaysHttps: "true", minTls: "1.2" },
    })),
    ...users("cloudflare", take(actives.filter((p) => ["Security", "IT", "Engineering"].includes(p.department)), 0, 8)),
  ],
});
add({
  key: "digitalocean",
  name: "DigitalOcean",
  category: "cloud",
  authType: "apiKey",
  description: "Droplets with backup state and cloud firewalls with inbound rules.",
  fields: [secret("apiToken", "Personal access token")],
  resources: ["server", "firewall"],
  tests: ["server-backups", "network-exposure"],
  controls: ["BC-01", "CFG-01"],
  docsUrl: "https://docs.digitalocean.com/reference/api/api-reference/#tag/Droplets",
  fixtures: [
    ...["edge-proxy-1", "edge-proxy-2", "batch-runner", "bastion"].map((name, i) => ({
      provider: "digitalocean",
      kind: "server",
      externalId: `digitalocean:${3200 + i}`,
      name,
      attributes: { backups: bool(i !== 2), region: "nyc3", size: "s-2vcpu-4gb" },
    })),
    ...["edge-web", "bastion-ssh"].map((name, i) => ({
      provider: "digitalocean",
      kind: "firewall",
      externalId: `digitalocean:fw-${name}`,
      name,
      attributes: { open: "false", inbound: i === 0 ? "443/tcp from 0.0.0.0/0" : "22/tcp from 203.0.113.10/32" },
    })),
  ],
});
add({
  key: "heroku",
  name: "Heroku",
  category: "cloud",
  authType: "apiKey",
  description: "Team apps with stack and region plus team members with two-factor state.",
  fields: [secret("apiKey", "API key"), field("team", "Team name")],
  resources: ["app", "user"],
  tests: ["mfa-all-users"],
  controls: ["AC-07", "CM-14"],
  docsUrl: "https://devcenter.heroku.com/articles/platform-api-reference",
  fixtures: [
    ...["marketing-preview", "partner-sandbox"].map((name) => ({
      provider: "heroku",
      kind: "app",
      externalId: `heroku:${name}`,
      name,
      attributes: { stack: "heroku-24", region: "us", environment: name.includes("sandbox") ? "test" : "staging" },
    })),
    ...users("heroku", take(actives, 300, 6)),
  ],
});
add({
  key: "vercel",
  name: "Vercel",
  category: "cloud",
  authType: "apiKey",
  description: "Projects and production domains with automatic HTTPS.",
  fields: [secret("apiToken", "Access token"), optional("teamId", "Team ID")],
  resources: ["app", "domain"],
  tests: ["tls-enforced"],
  controls: ["CR-05", "CM-14"],
  docsUrl: "https://vercel.com/docs/rest-api/endpoints/projects",
  fixtures: [
    ...["harborline-web", "harborline-docs"].map((name) => ({
      provider: "vercel",
      kind: "app",
      externalId: `vercel:${name}`,
      name,
      attributes: { framework: "vite", environment: "production" },
    })),
    {
      provider: "vercel",
      kind: "domain",
      externalId: `vercel:www.${domain}`,
      name: `www.${domain}`,
      attributes: { tls: "true", verified: "true" },
    },
  ],
});

// ----- Code / CI -----
add({
  key: "gitlab",
  name: "GitLab",
  category: "code",
  authType: "apiKey",
  description: "Group projects and default-branch protection with required approvals.",
  fields: [optional("baseUrl", "Base URL", "https://gitlab.com"), field("groupId", "Group ID or path", { placeholder: slug }), secret("accessToken", "Access token")],
  resources: ["repository"],
  tests: ["branch-protection"],
  controls: ["CM-11"],
  docsUrl: "https://docs.gitlab.com/ee/api/protected_branches.html",
  harborline: true,
  fixtures: repos("gitlab", ["data-pipelines", "ml-scoring", "terraform-modules", "helm-charts"]),
});
add({
  key: "bitbucket",
  name: "Bitbucket Cloud",
  category: "code",
  authType: "apiKey",
  description: "Workspace repositories and branch restrictions on the main branch.",
  fields: [field("workspace", "Workspace", { placeholder: slug }), field("username", "Username"), secret("appPassword", "App password")],
  resources: ["repository"],
  tests: ["branch-protection"],
  controls: ["CM-11"],
  docsUrl: "https://developer.atlassian.com/cloud/bitbucket/rest/api-group-branch-restrictions/",
  fixtures: repos("bitbucket", ["legacy-portal", "partner-sdk", "qa-automation"], [2]),
});
add({
  key: "azuredevops",
  name: "Azure DevOps",
  category: "code",
  authType: "apiKey",
  description: "Git repositories and minimum-reviewer branch policies.",
  fields: [field("organization", "Organization"), field("project", "Project"), secret("pat", "Personal access token")],
  resources: ["repository"],
  tests: ["branch-protection"],
  controls: ["CM-11"],
  docsUrl: "https://learn.microsoft.com/rest/api/azure/devops/policy/configurations/list",
  fixtures: repos("azuredevops", ["erp-connector", "reporting-service"]),
});
add({
  key: "circleci",
  name: "CircleCI",
  category: "code",
  authType: "apiKey",
  description: "Pipelines on the default branch and whether they came from an approved merge.",
  fields: [field("orgSlug", "Org slug", { placeholder: `gh/${slug}` }), secret("apiToken", "Personal API token")],
  resources: ["change"],
  tests: ["change-approved"],
  controls: ["CM-11"],
  docsUrl: "https://circleci.com/docs/api/v2/index.html#operation/listPipelines",
  fixtures: changes("circleci", "PIPE-", ["engine deploy", "ingest deploy", "web deploy", "infra apply", "docs publish", "billing deploy"]),
});

// ----- Vulnerability -----
add({
  key: "snyk",
  name: "Snyk",
  category: "vulnerability",
  authType: "apiKey",
  description: "Open source, code and container issues with severity and age.",
  fields: [field("orgId", "Organization ID"), secret("apiToken", "API token"), optional("baseUrl", "API base URL", "https://api.snyk.io")],
  resources: ["vulnerability"],
  tests: ["vulnerability-sla"],
  controls: ["VM-02"],
  docsUrl: "https://apidocs.snyk.io/?version=2024-10-15#get-/orgs/-org_id-/issues",
  harborline: true,
  fixtures: vulns("snyk", [
    ["SNYK-JS-AXIOS-6032459", "axios SSRF via absolute URL", "high", "harborline-web", 12, "open", "CVE-2024-39338"],
    ["SNYK-PYTHON-JINJA2-6809379", "jinja2 HTML attribute injection", "medium", "harborline-ingest", 40, "open", "CVE-2024-34064"],
    ["SNYK-DOTNET-SYSTEMTEXTJSON-1", "System.Text.Json DoS", "high", "harborline-engine", 9, "resolved", "CVE-2024-43485"],
    ["SNYK-JS-BRACES-6838727", "braces uncontrolled resource consumption", "high", "harborline-web", 44, "open", "CVE-2024-4068"],
    ["SNYK-GOLANG-NETHTTP-1", "net/http request smuggling", "critical", "harborline-coordinator", 6, "open", "CVE-2025-22871"],
    ["SNYK-JS-WS-7266574", "ws DoS on many headers", "high", "harborline-docs", 20, "resolved", "CVE-2024-37890"],
    ["SNYK-PYTHON-URLLIB3-7267250", "urllib3 proxy-authorization leak", "medium", "harborline-ingest", 18, "open", "CVE-2024-37891"],
    ["SNYK-CODE-SQLI-1", "SQL injection in report filter", "high", "harborline-billing", 3, "open", ""],
  ]),
});
add({
  key: "wiz",
  name: "Wiz",
  category: "vulnerability",
  authType: "oauth",
  description: "Cloud security issues and toxic combinations from the Wiz GraphQL API.",
  fields: [field("clientId", "Service account client ID"), secret("clientSecret", "Client secret"), field("apiEndpoint", "API endpoint", { placeholder: "https://api.us17.app.wiz.io/graphql" })],
  resources: ["vulnerability"],
  tests: ["vulnerability-sla"],
  controls: ["VM-02", "CFG-01"],
  docsUrl: "https://win.wiz.io/reference/issues-query",
  fixtures: vulns("wiz", [
    ["wiz-iss-101", "Publicly exposed VM with high-severity vulnerability", "critical", "aws/i-0a12 ingest-worker", 4, "open"],
    ["wiz-iss-102", "Storage bucket allows public listing", "high", "aws/marketing-site", 38, "open"],
    ["wiz-iss-103", "Admin role without MFA condition", "high", "aws/iam/role/ci-deployer", 10, "resolved"],
    ["wiz-iss-104", "Kubernetes pod runs as root", "medium", "aks/harborline-prod/ingest", 25, "open"],
    ["wiz-iss-105", "Unencrypted EBS snapshot shared cross-account", "medium", "aws/snap-0f9", 60, "resolved"],
  ]),
});
add({
  key: "tenable",
  name: "Tenable Vulnerability Management",
  category: "vulnerability",
  authType: "apiKey",
  description: "Host vulnerabilities from Tenable.io workbenches with plugin severity.",
  fields: [field("accessKey", "Access key"), secret("secretKey", "Secret key")],
  resources: ["vulnerability"],
  tests: ["vulnerability-sla"],
  controls: ["VM-02"],
  docsUrl: "https://developer.tenable.com/reference/workbenches-vulnerabilities",
  harborline: true,
  fixtures: vulns("tenable", [
    ["187315", "OpenSSH < 9.6 Terrapin prefix truncation", "medium", "bastion.harborline.example", 33, "open", "CVE-2023-48795"],
    ["201218", "OpenSSH regreSSHion RCE", "critical", "edge-proxy-1.harborline.example", 21, "open", "CVE-2024-6387"],
    ["192231", "Apache HTTP Server 2.4.58 multiple", "high", "legacy-portal.harborline.example", 16, "open", "CVE-2024-27316"],
    ["156032", "Log4j 2.x RCE (Log4Shell)", "critical", "reporting-01.harborline.example", 410, "resolved", "CVE-2021-44228"],
    ["181288", "TLS 1.0 protocol detection", "medium", "vpn.harborline.example", 74, "open"],
    ["57582", "SSL self-signed certificate", "low", "printer-02.harborline.example", 120, "open"],
    ["209876", "Windows cumulative update missing", "high", "HL-10002", 11, "open"],
  ]),
});
add({
  key: "qualys",
  name: "Qualys VMDR",
  category: "vulnerability",
  authType: "apiKey",
  description: "Host detections (QIDs) with severity from the Qualys VM API.",
  fields: [field("platformUrl", "API platform URL", { placeholder: "https://qualysapi.qualys.com" }), field("username", "Username"), secret("password", "Password")],
  resources: ["vulnerability"],
  tests: ["vulnerability-sla"],
  controls: ["VM-02"],
  docsUrl: "https://docs.qualys.com/en/vm/api/assets/host_lists/host_detection.htm",
  fixtures: vulns("qualys", [
    ["qid-38909", "SSH Prefix Truncation Vulnerability (Terrapin)", "medium", "10.20.1.14", 20, "open"],
    ["qid-376146", "Google Chrome prior to 125 multiple", "high", "HL-10044", 8, "open"],
    ["qid-105489", "Microsoft Office Security Update", "high", "HL-10051", 40, "resolved"],
    ["qid-11827", "HTTP Security Header Not Detected", "low", "status.harborline.example", 90, "open"],
  ]),
});
add({
  key: "semgrep",
  name: "Semgrep",
  category: "vulnerability",
  authType: "apiKey",
  description: "SAST and supply-chain findings per repository.",
  fields: [field("deploymentSlug", "Deployment slug", { placeholder: slug }), secret("apiToken", "Web API token")],
  resources: ["vulnerability"],
  tests: ["vulnerability-sla"],
  controls: ["VM-02", "CM-11"],
  docsUrl: "https://semgrep.dev/api/v1/docs/#tag/Finding",
  fixtures: vulns("semgrep", [
    ["sg-9001", "python.django.security.audit.raw-query", "high", "harborline-ingest", 5, "open"],
    ["sg-9002", "javascript.express.security.audit.xss.direct-response-write", "medium", "harborline-web", 30, "open"],
    ["sg-9003", "csharp.lang.security.insecure-deserialization", "high", "harborline-engine", 15, "resolved"],
  ]),
});

// ----- Observability / logging -----
const monitors = (provider, names, muted = []) =>
  names.map((name, i) => ({
    provider,
    kind: "monitor",
    externalId: `${provider}:mon-${100 + i}`,
    name,
    attributes: { muted: bool(muted.includes(i)), notifies: "pagerduty", type: i % 2 ? "metric" : "log" },
  }));
const alerts = (provider, rows) =>
  rows.map(([id, name, severity, triaged, status, ageHours]) => ({
    provider,
    kind: "alert",
    externalId: `${provider}:${id}`,
    name,
    attributes: { severity, triaged: bool(triaged), status, ageHours: String(ageHours) },
  }));
const schedules = (provider, names, gaps = []) =>
  names.map((name, i) => ({
    provider,
    kind: "schedule",
    externalId: `${provider}:${name.toLowerCase().replace(/[^a-z0-9]+/g, "-")}`,
    name,
    attributes: { covered: bool(!gaps.includes(i)), layers: String(1 + (i % 2)), members: String(4 + i) },
  }));

add({
  key: "datadog",
  name: "Datadog",
  category: "observability",
  authType: "apiKey",
  description: "Monitors, mute state and log index retention.",
  fields: [optional("site", "Site", "datadoghq.com"), secret("apiKey", "API key"), secret("appKey", "Application key")],
  resources: ["monitor", "logIndex"],
  tests: ["monitors-active", "log-retention"],
  controls: ["LG-08", "LG-03"],
  docsUrl: "https://docs.datadoghq.com/api/latest/monitors/#get-all-monitor-details",
  harborline: true,
  fixtures: [
    ...monitors("datadog", [
      "Ingest p95 latency > 2s",
      "Failed logins spike (auth service)",
      "GuardDuty high-severity finding",
      "Coordinator error rate > 1%",
      "Disk usage > 85% on warehouse",
      "Certificate expires in < 14 days",
    ]),
    ...[
      ["audit", "true", 400],
      ["security", "true", 365],
      ["main", "false", 15],
    ].map(([name, audit, days]) => ({
      provider: "datadog",
      kind: "logIndex",
      externalId: `datadog:index-${name}`,
      name,
      attributes: { audit, retentionDays: String(days) },
    })),
  ],
});
add({
  key: "splunk",
  name: "Splunk",
  category: "observability",
  authType: "apiKey",
  description: "Indexes and their frozen-time retention from the Splunk REST API.",
  fields: [field("baseUrl", "Management URL", { placeholder: `https://${slug}.splunkcloud.com:8089` }), secret("token", "Authentication token")],
  resources: ["logIndex"],
  tests: ["log-retention"],
  controls: ["LG-03"],
  docsUrl: "https://docs.splunk.com/Documentation/Splunk/latest/RESTREF/RESTintrospect#data.2Findexes",
  fixtures: [
    ["wineventlog", "true", 400],
    ["okta", "true", 395],
    ["main", "false", 90],
  ].map(([name, audit, days]) => ({
    provider: "splunk",
    kind: "logIndex",
    externalId: `splunk:${name}`,
    name,
    attributes: { audit, retentionDays: String(days) },
  })),
});
add({
  key: "newrelic",
  name: "New Relic",
  category: "observability",
  authType: "apiKey",
  description: "Alert policies and whether they route to a notification channel.",
  fields: [secret("apiKey", "User API key"), optional("region", "Region (us or eu)", "us")],
  resources: ["monitor"],
  tests: ["monitors-active"],
  controls: ["LG-08"],
  docsUrl: "https://docs.newrelic.com/docs/apis/rest-api-v2/alerts-examples-v2/rest-api-calls-alerts/",
  fixtures: monitors("newrelic", ["Golden signals - web", "Golden signals - ingest", "Synthetic: login journey"]),
});
add({
  key: "pagerduty",
  name: "PagerDuty",
  category: "observability",
  authType: "apiKey",
  description: "On-call schedules and incident acknowledgement.",
  fields: [secret("apiToken", "REST API key")],
  resources: ["schedule", "alert"],
  tests: ["oncall-coverage", "alerts-triaged"],
  controls: ["IR-02", "LG-08"],
  docsUrl: "https://developer.pagerduty.com/api-reference/",
  harborline: true,
  fixtures: [
    ...schedules("pagerduty", ["Platform primary", "Platform secondary", "Security on-call", "Data pipeline"]),
    ...alerts("pagerduty", [
      ["Q1R2", "Ingest backlog above threshold", "high", true, "resolved", 6],
      ["Q1R3", "Suspicious OAuth grant detected", "high", true, "resolved", 3],
      ["Q1R4", "Warehouse disk 90%", "low", true, "acknowledged", 1],
    ]),
  ],
});
add({
  key: "opsgenie",
  name: "Opsgenie",
  category: "observability",
  authType: "apiKey",
  description: "Schedules with on-call participants and alert acknowledgement.",
  fields: [secret("apiKey", "API key (GenieKey)"), optional("baseUrl", "API base URL", "https://api.opsgenie.com")],
  resources: ["schedule", "alert"],
  tests: ["oncall-coverage", "alerts-triaged"],
  controls: ["IR-02", "LG-08"],
  docsUrl: "https://docs.opsgenie.com/docs/schedule-api",
  fixtures: [
    ...schedules("opsgenie", ["IT helpdesk", "Corporate security"], [1]),
    ...alerts("opsgenie", [
      ["og-77", "VPN concentrator CPU high", "medium", true, "closed", 4],
      ["og-78", "Badge reader offline", "low", false, "open", 52],
    ]),
  ],
});

// ----- Ticketing -----
add({
  key: "asana",
  name: "Asana",
  category: "ticketing",
  authType: "apiKey",
  description: "Change tasks in a project and whether an approver signed off.",
  fields: [field("projectId", "Change project GID"), secret("accessToken", "Personal access token")],
  resources: ["change"],
  tests: ["change-approved"],
  controls: ["CM-11"],
  docsUrl: "https://developers.asana.com/reference/gettasks",
  fixtures: changes("asana", "ASN-", ["Rotate Okta signing key", "Enable SCIM for Zoom", "Update DLP rules"]),
});
add({
  key: "servicenow",
  name: "ServiceNow",
  category: "ticketing",
  authType: "apiKey",
  description: "Change requests with approval state from the Table API.",
  fields: [field("instanceUrl", "Instance URL", { placeholder: `https://${slug}.service-now.com` }), field("username", "Username"), secret("password", "Password")],
  resources: ["change"],
  tests: ["change-approved"],
  controls: ["CM-11", "CM-14"],
  docsUrl: "https://developer.servicenow.com/dev.do#!/reference/api/latest/rest/c_TableAPI",
  harborline: true,
  fixtures: changes("servicenow", "CHG", [
    "Upgrade Postgres 15 to 16 (warehouse)",
    "Rotate TLS certificates for api",
    "Firewall rule change: partner SFTP",
    "Deploy coordinator 4.12",
    "Patch edge proxies (regreSSHion)",
    "Decommission legacy portal",
  ]),
});
add({
  key: "clickup",
  name: "ClickUp",
  category: "ticketing",
  authType: "apiKey",
  description: "Change tasks from a list and their approval status.",
  fields: [field("listId", "List ID"), secret("apiToken", "API token")],
  resources: ["change"],
  tests: ["change-approved"],
  controls: ["CM-11"],
  docsUrl: "https://clickup.com/api/clickupreference/operation/GetTasks/",
  fixtures: changes("clickup", "CU-", ["Marketing site redesign launch", "HubSpot form change"], [1]),
});

// ----- Communication -----
add({
  key: "zoom",
  name: "Zoom",
  category: "communication",
  authType: "oauth",
  description: "Users, account two-factor enforcement and user status.",
  fields: [field("accountId", "Account ID"), field("clientId", "Client ID"), secret("clientSecret", "Client secret")],
  resources: ["user"],
  tests: ["mfa-all-users", "offboarded-removed"],
  controls: ["AC-07", "HR-08"],
  docsUrl: "https://developers.zoom.us/docs/api/users/#tag/users/GET/users",
  harborline: true,
  fixtures: users("zoom", [...take(actives, 320, 30), ...take(leavers, 24, 3)]),
});

// ----- Password managers -----
add({
  key: "onepassword",
  name: "1Password",
  category: "passwords",
  authType: "apiKey",
  description: "Members from the 1Password SCIM bridge, role and two-factor state.",
  fields: [field("scimBridgeUrl", "SCIM bridge URL", { placeholder: `https://scim.${domain}` }), secret("bearerToken", "Bearer token")],
  resources: ["vaultMember"],
  tests: ["password-manager-mfa"],
  controls: ["AC-07"],
  docsUrl: "https://support.1password.com/scim-endpoints/",
  harborline: true,
  fixtures: vault("onepassword", [...take(actives, 0, 36), ...take(leavers, 0, 4)]),
});
add({
  key: "bitwarden",
  name: "Bitwarden",
  category: "passwords",
  authType: "oauth",
  description: "Organization members with two-factor enablement from the Public API.",
  fields: [field("clientId", "Organization client ID"), secret("clientSecret", "Client secret"), optional("baseUrl", "API base URL", "https://api.bitwarden.com")],
  resources: ["vaultMember"],
  tests: ["password-manager-mfa"],
  controls: ["AC-07"],
  docsUrl: "https://bitwarden.com/help/api/",
  fixtures: vault("bitwarden", take(actives, 400, 18), [11]),
});
add({
  key: "lastpass",
  name: "LastPass Business",
  category: "passwords",
  authType: "apiKey",
  description: "Users and multifactor type from the Enterprise API (getuserdata).",
  fields: [field("cid", "Account number (CID)"), secret("provhash", "Provisioning hash")],
  resources: ["vaultMember"],
  tests: ["password-manager-mfa"],
  controls: ["AC-07"],
  docsUrl: "https://support.lastpass.com/s/document-item?language=en_US&bundleId=lastpass&topicId=LastPass/api_get_user_data.html",
  fixtures: vault("lastpass", take(actives, 430, 15)),
});

// ----- Security training -----
add({
  key: "curricula",
  name: "Curricula",
  category: "training",
  authType: "apiKey",
  description: "Learner assignments and completion for the annual security awareness course.",
  fields: [field("baseUrl", "API base URL", { placeholder: "https://mycurricula.com/api/v1" }), secret("apiKey", "API key")],
  resources: ["training"],
  tests: ["training-completed"],
  controls: ["HR-06"],
  docsUrl: "https://www.huntress.com/platform/security-awareness-training",
  harborline: true,
  fixtures: training("curricula", take(actives, 0, 60), "Annual security awareness 2025"),
});
add({
  key: "hoxhunt",
  name: "Hoxhunt",
  category: "training",
  authType: "apiKey",
  description: "Users, onboarding to phishing training and completion state.",
  fields: [secret("apiToken", "API token"), optional("baseUrl", "GraphQL URL", "https://app.hoxhunt.com/graphql-external")],
  resources: ["training"],
  tests: ["training-completed"],
  controls: ["HR-06"],
  docsUrl: "https://support.hoxhunt.com/hc/en-us/articles/4406611914385",
  fixtures: training("hoxhunt", take(actives, 500, 25), "Phishing simulation training"),
});

// ----- Background checks -----
add({
  key: "certn",
  name: "Certn",
  category: "background",
  authType: "apiKey",
  description: "Applicant screening status and adjudication result.",
  fields: [secret("apiKey", "API key"), optional("baseUrl", "API base URL", "https://api.certn.co")],
  resources: ["backgroundCheck"],
  tests: ["background-screened"],
  controls: ["HR-01"],
  docsUrl: "https://docs.certn.co/api/",
  harborline: true,
  fixtures: take(actives, 600, 30).map((p, i) => ({
    provider: "certn",
    kind: "backgroundCheck",
    externalId: `certn:app-${pad(7000 + i)}`,
    name: p.name,
    attributes: { email: p.email, cleared: "true", status: "complete", package: i % 4 === 0 ? "criminal+employment" : "criminal" },
  })),
});

// ----- Data stores -----
const database = (provider, name, attrs) => ({
  provider,
  kind: "database",
  externalId: `${provider}:${name}`,
  name,
  attributes: { encrypted: "true", public: "false", ...attrs },
});
add({
  key: "snowflake",
  name: "Snowflake",
  category: "data",
  authType: "apiKey",
  description: "Human users with MFA enrolment and account network policies.",
  fields: [field("account", "Account identifier", { placeholder: "xy12345.us-east-1" }), secret("token", "OAuth or programmatic access token"), optional("warehouse", "Warehouse", "COMPUTE_WH")],
  resources: ["user", "firewall"],
  tests: ["mfa-all-users", "network-exposure"],
  controls: ["AC-07", "CFG-01"],
  docsUrl: "https://docs.snowflake.com/en/developer-guide/sql-api/reference",
  fixtures: [
    ...users("snowflake", take(actives.filter((p) => p.department === "Engineering"), 30, 8)),
    {
      provider: "snowflake",
      kind: "firewall",
      externalId: "snowflake:corp_only",
      name: "CORP_ONLY",
      attributes: { open: "false", allowed: "203.0.113.0/24" },
    },
  ],
});
add({
  key: "mongodb-atlas",
  name: "MongoDB Atlas",
  category: "data",
  authType: "oauth",
  description: "Clusters, encryption at rest and project IP access lists.",
  fields: [field("groupId", "Project ID"), field("clientId", "Service account client ID"), secret("clientSecret", "Service account secret")],
  resources: ["database"],
  tests: ["database-hardening"],
  controls: ["CR-02", "CFG-01"],
  docsUrl: "https://www.mongodb.com/docs/atlas/reference/api-resources-spec/v2/",
  harborline: true,
  fixtures: [
    database("mongodb-atlas", "prod-events", { tier: "M40", version: "7.0" }),
    database("mongodb-atlas", "prod-sessions", { tier: "M30", version: "7.0" }),
    database("mongodb-atlas", "analytics-replica", { tier: "M20", version: "7.0" }),
  ],
});
add({
  key: "supabase",
  name: "Supabase",
  category: "data",
  authType: "apiKey",
  description: "Projects with SSL enforcement and network restrictions.",
  fields: [secret("accessToken", "Personal access token")],
  resources: ["database"],
  tests: ["database-hardening"],
  controls: ["CR-02", "CR-05"],
  docsUrl: "https://supabase.com/docs/reference/api/introduction",
  fixtures: [
    database("supabase", "partner-portal", { sslEnforced: "true" }),
    database("supabase", "growth-experiments", { sslEnforced: "true", public: "true" }),
  ],
});

const checks = [
  ["endpoint-edr", "EDR agent healthy on every endpoint", "MAL-01", ["soc2 CC6.8", "iso27001 A.8.7"], "Reinstall or repair the sensor on degraded endpoints and confirm prevention is on."],
  ["endpoint-disk-encryption", "Disk encryption on managed devices", "EP-03", ["soc2 CC6.7", "iso27001 A.8.1"], "Enforce FileVault or BitLocker with an MDM profile and escrow the recovery key."],
  ["vulnerability-sla", "Vulnerabilities fixed within SLA", "VM-02", ["soc2 CC7.1", "iso27001 A.8.8"], "Patch or accept the risk for open findings past their severity SLA (critical 15, high 30, medium 90, low 180 days)."],
  ["hris-leavers-deprovisioned", "HRIS leavers have no active accounts", "HR-08", ["soc2 CC6.2", "soc2 CC6.3", "iso27001 A.6.5"], "Suspend every account that belongs to a terminated employee."],
  ["alerts-triaged", "Security alerts are triaged", "LG-08", ["soc2 CC7.3", "iso27001 A.5.25"], "Acknowledge or resolve open alerts and assign an owner."],
  ["monitors-active", "Monitoring alerts are active", "LG-08", ["soc2 CC7.2", "iso27001 A.8.16"], "Unmute monitors or document why they are muted."],
  ["log-retention", "Audit logs retained for one year", "LG-03", ["soc2 CC7.2", "iso27001 A.8.15"], "Raise retention on audit indexes to at least 365 days or archive to cold storage."],
  ["training-completed", "Security training completed", "HR-06", ["soc2 CC1.4", "soc2 CC2.2", "iso27001 A.6.3"], "Send reminders and escalate overdue learners to their managers."],
  ["background-screened", "Background checks cleared", "HR-01", ["soc2 CC1.4", "iso27001 A.6.1"], "Finish or adjudicate the pending screenings before granting production access."],
  ["database-hardening", "Managed databases encrypted and private", "CR-02", ["soc2 CC6.1", "soc2 CC6.7", "iso27001 A.8.24"], "Turn on encryption at rest and remove 0.0.0.0/0 from database access lists."],
  ["tls-enforced", "TLS enforced on public domains", "CR-05", ["soc2 CC6.7", "iso27001 A.8.24"], "Set SSL mode to Full (strict), turn on Always Use HTTPS and minimum TLS 1.2."],
  ["password-manager-mfa", "Password manager members use MFA", "AC-07", ["soc2 CC6.1", "iso27001 A.5.17"], "Require two-factor in the password manager policy for every member."],
  ["server-backups", "Server backups enabled", "BC-01", ["soc2 A1.2", "iso27001 A.8.13"], "Turn on automated backups for every production server."],
  ["network-exposure", "Firewalls block unrestricted inbound", "CFG-01", ["soc2 CC6.6", "iso27001 A.8.20"], "Restrict administrative ports to known address ranges."],
  ["change-approved", "Changes approved before release", "CM-11", ["soc2 CC8.1", "iso27001 A.8.32"], "Record an approver on every change before it is released."],
  ["oncall-coverage", "On-call schedules are staffed", "IR-02", ["soc2 CC7.4", "iso27001 A.5.24"], "Fill the gaps in on-call rotations so every hour has a responder."],
].map(([key, name, controlCode, requirements, remediation]) => ({ key, name, controlCode, requirements, remediation }));

const categories = [
  "identity",
  "hris",
  "endpoint",
  "cloud",
  "code",
  "vulnerability",
  "observability",
  "ticketing",
  "communication",
  "passwords",
  "training",
  "background",
  "data",
];

writeFileSync(join(root, "connectors.json"), JSON.stringify({ categories, checks, connectors: catalog }, null, 2) + "\n");
console.log(`${catalog.length} connectors, ${catalog.filter((c) => !c.builtin).length} new, ${catalog.reduce((n, c) => n + c.fixtures.length, 0)} fixture resources`);
