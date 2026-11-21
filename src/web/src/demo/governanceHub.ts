import { company, type DemoWorld } from "./catalog";

type Actor = { email: string; name: string; role: string };

export type DemoLegalEntity = {
  id: string;
  code: string;
  name: string;
  jurisdiction: string;
  region: string;
  headcount: number;
  controlsPassing: number;
  controlsTotal: number;
  readinessPercent: number;
  primaryFramework: string;
  status: string;
  createdAt: string;
};

export type DemoPrivacyAssessment = {
  id: string;
  code: string;
  kind: string;
  activityName: string;
  dataSubjects: string;
  lawfulBasis: string;
  transferMechanism: string;
  riskLevel: string;
  status: string;
  ownerEmail: string;
  reviewDueOn: string;
  updatedAt: string;
};

export type DemoAiModel = {
  id: string;
  code: string;
  modelName: string;
  provider: string;
  euAiActRiskTier: string;
  trainingDataProvenance: string;
  guardrails: string;
  redTeamStatus: string;
  linkedControlCode: string;
  ownerEmail: string;
  evaluatedAt: string;
};

export type DemoPhishingCampaign = {
  id: string;
  code: string;
  name: string;
  source: string;
  sentCount: number;
  clickedCount: number;
  reportedCount: number;
  clickRatePercent: number;
  reportRatePercent: number;
  riskBand: string;
  remedialTrainingStatus: string;
  ranAt: string;
};

export type DemoAttackSurfaceAsset = {
  id: string;
  hostname: string;
  ipAddress: string;
  service: string;
  port: number;
  tlsDaysRemaining: number;
  shadowIt: boolean;
  severity: string;
  status: string;
  lastSeenAt: string;
};

export type DemoAuditorFirm = {
  id: string;
  code: string;
  firmName: string;
  accreditation: string;
  specialties: string;
  regions: string;
  leadAuditorEmail: string;
  invited: boolean;
  invitedAt: string | null;
};

export type DemoRemediationPr = {
  id: string;
  controlCode: string;
  targetResource: string;
  repository: string;
  branchName: string;
  title: string;
  artifactType: string;
  unifiedDiff: string;
  prNumber: number;
  prUrl: string;
  mode: string;
  status: string;
  evidenceId: string | null;
  evidenceSha256: string;
  createdAt: string;
};

type GovernanceHubState = {
  entities: DemoLegalEntity[];
  privacy: DemoPrivacyAssessment[];
  aiModels: DemoAiModel[];
  phishing: DemoPhishingCampaign[];
  attackSurface: DemoAttackSurfaceAsset[];
  auditorNetwork: DemoAuditorFirm[];
  remediationPrs: DemoRemediationPr[];
  nextPrNumber: number;
};

const stateByWorld = new WeakMap<DemoWorld, GovernanceHubState>();

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function canWrite(actor: Actor) {
  return actor.role === "Admin" || actor.role === "ControlOwner" || actor.role === "Editor";
}

function readiness(passing: number, total: number) {
  return total <= 0 ? 0 : Math.round((Math.min(Math.max(passing, 0), total) * 100) / total);
}

function rate(num: number, den: number) {
  if (den <= 0) return 0;
  return Math.round((Math.min(Math.max(num, 0), den) * 1000) / den) / 10;
}

function phishingBand(clickRate: number) {
  if (clickRate >= 10) return "High";
  if (clickRate >= 4) return "Medium";
  return "Low";
}

async function sha256Hex(value: string): Promise<string> {
  const bytes = new TextEncoder().encode(value);
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

function slugify(value: string): string {
  return (
    value
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-+|-+$/g, "") || "resource"
  );
}

function buildDiff(
  controlCode: string,
  resource: string,
  artifactType: string,
  customFix?: string,
) {
  const safeName = resource.replace(/["\\\r\n]/g, "").trim();
  const tfId = slugify(safeName).replace(/-/g, "_");
  if (customFix && customFix.trim().length > 0) {
    return [
      `--- a/infra/${tfId}.tf`,
      `+++ b/infra/${tfId}.tf`,
      `@@ -1,4 +1,8 @@ # Control ${controlCode}`,
      ...customFix
        .replace(/\r\n/g, "\n")
        .split("\n")
        .slice(0, 24)
        .map((line) => `+${line}`),
    ].join("\n");
  }
  switch (artifactType) {
    case "Dockerfile":
      return [
        "--- a/Dockerfile",
        "+++ b/Dockerfile",
        `@@ -1,5 +1,7 @@ # Control ${controlCode} (${safeName})`,
        "-FROM debian:12.4-slim",
        "+FROM debian:12.8-slim",
        " WORKDIR /app",
        "-COPY . .",
        "+COPY --chown=nonroot:nonroot dist/ ./dist/",
        "+USER nonroot:nonroot",
      ].join("\n");
    case "GitHubWorkflow":
      return [
        "--- a/.github/workflows/deploy.yml",
        "+++ b/.github/workflows/deploy.yml",
        `@@ -4,6 +4,11 @@ # Control ${controlCode} (${safeName})`,
        " permissions:",
        "-  contents: write",
        "+  contents: read",
        "+  id-token: write",
        " jobs:",
        "   release:",
        "+    environment: production-approved",
        "     steps:",
        "+      - name: Require peer review & signed provenance",
        `+        run: echo "Verified review gate for ${safeName}"`,
      ].join("\n");
    case "IamPolicy":
      return [
        `--- a/iam/${tfId}-policy.json`,
        `+++ b/iam/${tfId}-policy.json`,
        `@@ -3,7 +3,10 @@ # Control ${controlCode} (${safeName})`,
        '     "Effect": "Allow",',
        '-    "Action": "*",',
        '-    "Resource": "*"',
        '+    "Action": ["s3:GetObject", "kms:Decrypt"],',
        `+    "Resource": "arn:aws:s3:::${safeName}/*",`,
        '+    "Condition": { "Bool": { "aws:MultiFactorAuthPresent": "true" } }',
      ].join("\n");
    default:
      return [
        `--- a/infra/terraform/${tfId}.tf`,
        `+++ b/infra/terraform/${tfId}.tf`,
        `@@ -1,6 +1,11 @@ # Control ${controlCode} (${safeName})`,
        ` resource "aws_vpc_security_group_ingress_rule" "${tfId}_restricted" {`,
        `   security_group_id = "${safeName}"`,
        '-  cidr_ipv4         = "0.0.0.0/0"',
        '+  cidr_ipv4         = "10.20.0.0/16"',
        '   ip_protocol       = "tcp"',
        `+  description       = "Restricted by D2D remediation PR for ${controlCode}"`,
        " }",
      ].join("\n");
  }
}

function getState(world: DemoWorld): GovernanceHubState {
  let state = stateByWorld.get(world);
  if (!state) {
    const now = Date.now();
    const daysAgo = (d: number) => new Date(now - d * 86_400_000).toISOString();
    const futureDate = (d: number) => new Date(now + d * 86_400_000).toISOString().slice(0, 10);
    const domain = company.domain;
    state = {
      entities: [
        {
          id: "ent-us",
          code: "ENT-US",
          name: `${company.name} US Inc.`,
          jurisdiction: "United States (Delaware)",
          region: "us-east-1",
          headcount: 640,
          controlsPassing: 58,
          controlsTotal: 64,
          readinessPercent: readiness(58, 64),
          primaryFramework: "SOC 2 Type II + HIPAA",
          status: "AuditInProgress",
          createdAt: daysAgo(180),
        },
        {
          id: "ent-eu",
          code: "ENT-EU",
          name: `${company.name} EU GmbH (Frankfurt)`,
          jurisdiction: "Germany / European Union",
          region: "eu-central-1",
          headcount: 310,
          controlsPassing: 49,
          controlsTotal: 54,
          readinessPercent: readiness(49, 54),
          primaryFramework: "ISO 27001 + GDPR + EU AI Act",
          status: "Active",
          createdAt: daysAgo(140),
        },
        {
          id: "ent-fed",
          code: "ENT-FED",
          name: `${company.name} Federal & OT Systems`,
          jurisdiction: "US Federal / Critical Infrastructure",
          region: "us-gov-west-1",
          headcount: 130,
          controlsPassing: 36,
          controlsTotal: 46,
          readinessPercent: readiness(36, 46),
          primaryFramework: "NIST CSF 2.0 + IEC 62443",
          status: "Onboarding",
          createdAt: daysAgo(45),
        },
      ],
      privacy: [
        {
          id: "priv-1",
          code: "ROPA-01",
          kind: "RoPA",
          activityName: "Customer Analytics Warehouse Ingestion",
          dataSubjects: "Enterprise customers, end-user telemetry profiles",
          lawfulBasis: "Art. 6(1)(b) Contract performance",
          transferMechanism: "EU-US Data Privacy Framework + 2021 SCCs",
          riskLevel: "Medium",
          status: "Approved",
          ownerEmail: `lena.cho@${domain}`,
          reviewDueOn: futureDate(90),
          updatedAt: daysAgo(14),
        },
        {
          id: "priv-2",
          code: "DPIA-01",
          kind: "DPIA",
          activityName: "Automated Anomaly Scoring & AI Compliance Copilot",
          dataSubjects: "Workspace administrators and workforce directory",
          lawfulBasis: "Art. 6(1)(f) Legitimate interest (security monitoring)",
          transferMechanism: "EU-central-1 sovereign processing (no cross-border egress)",
          riskLevel: "High",
          status: "InReview",
          ownerEmail: `priya.nair@${domain}`,
          reviewDueOn: futureDate(21),
          updatedAt: daysAgo(4),
        },
        {
          id: "priv-3",
          code: "ROPA-02",
          kind: "RoPA",
          activityName: "Workforce Onboarding & Checkr Background Screening",
          dataSubjects: "Employees and contractors",
          lawfulBasis: "Art. 6(1)(c) Legal obligation",
          transferMechanism: "Standard Contractual Clauses (Module 2)",
          riskLevel: "Low",
          status: "Approved",
          ownerEmail: `chandra.vell@${domain}`,
          reviewDueOn: futureDate(180),
          updatedAt: daysAgo(30),
        },
      ],
      aiModels: [
        {
          id: "aim-1",
          code: "AIM-01",
          modelName: `${company.name} Grounded Compliance Assistant`,
          provider: "OpenAI-Compatible Enterprise Gateway",
          euAiActRiskTier: "Limited",
          trainingDataProvenance:
            "Zero customer training; RAG strictly scoped to role-permitted tenant metadata",
          guardrails: "Citation-required output, auditor framework isolation, PII redaction filter",
          redTeamStatus: "Passed",
          linkedControlCode: "AI-01",
          ownerEmail: `priya.nair@${domain}`,
          evaluatedAt: daysAgo(10),
        },
        {
          id: "aim-2",
          code: "AIM-02",
          modelName: "Warehouse PII Auto-Classifier v2.3",
          provider: "Internal PyTorch / ONNX",
          euAiActRiskTier: "High",
          trainingDataProvenance: "Synthetic PII/PHI corpus (v4.1) audited for demographic balance",
          guardrails:
            "Human-in-the-loop approval before retention purge; confidence threshold >= 0.92",
          redTeamStatus: "InProgress",
          linkedControlCode: "AI-01",
          ownerEmail: `marcus.lee@${domain}`,
          evaluatedAt: daysAgo(3),
        },
      ],
      phishing: [
        {
          id: "phish-2",
          code: "PHISH-2026-Q4",
          name: "Q4 Executive Wire & Vendor Invoice Spoof",
          source: "KnowBe4",
          sentCount: 1080,
          clickedCount: 14,
          reportedCount: 816,
          clickRatePercent: rate(14, 1080),
          reportRatePercent: rate(816, 1080),
          riskBand: phishingBand(rate(14, 1080)),
          remedialTrainingStatus: "Auto-assigned (14/14 completed)",
          ranAt: daysAgo(6),
        },
        {
          id: "phish-1",
          code: "PHISH-2026-Q3",
          name: "Q3 OAuth Consent & SSO Credential Harvest Drill",
          source: "KnowBe4",
          sentCount: 1080,
          clickedCount: 29,
          reportedCount: 742,
          clickRatePercent: rate(29, 1080),
          reportRatePercent: rate(742, 1080),
          riskBand: phishingBand(rate(29, 1080)),
          remedialTrainingStatus: "Auto-assigned (27/29 completed)",
          ranAt: daysAgo(32),
        },
      ],
      attackSurface: [
        {
          id: "asm-2",
          hostname: `staging-mongo.${domain}`,
          ipAddress: "34.201.44.19",
          service: "MongoDB Wire Protocol",
          port: 27017,
          tlsDaysRemaining: 9,
          shadowIt: true,
          severity: "Critical",
          status: "Open",
          lastSeenAt: daysAgo(0),
        },
        {
          id: "asm-3",
          hostname: `eu-preview.${domain}`,
          ipAddress: "20.79.112.64",
          service: "Legacy Admin Portal (Expiring Cert)",
          port: 8443,
          tlsDaysRemaining: 11,
          shadowIt: true,
          severity: "High",
          status: "Open",
          lastSeenAt: daysAgo(0),
        },
        {
          id: "asm-1",
          hostname: `api.${domain}`,
          ipAddress: "52.14.88.120",
          service: "HTTPS API Gateway (TLS 1.3)",
          port: 443,
          tlsDaysRemaining: 84,
          shadowIt: false,
          severity: "Low",
          status: "Mitigated",
          lastSeenAt: daysAgo(0),
        },
      ],
      auditorNetwork: [
        {
          id: "firm-1",
          code: "AUD-NORTHGATE",
          firmName: "Northgate Assurance LLP",
          accreditation: "AICPA Peer-Reviewed CPA Firm",
          specialties: "SOC 2 Type II, HIPAA, PCI DSS 4.0",
          regions: "US, Canada",
          leadAuditorEmail: "auditor@northgate.example",
          invited: true,
          invitedAt: daysAgo(40),
        },
        {
          id: "firm-2",
          code: "AUD-RHEIN",
          firmName: "RheinCert GmbH",
          accreditation: "DAkkS Accredited ISO Certification Body",
          specialties: "ISO 27001, ISO 42001, EU DORA, GDPR",
          regions: "EU (Frankfurt), UK",
          leadAuditorEmail: "certifications@rheincert.example",
          invited: false,
          invitedAt: null,
        },
        {
          id: "firm-3",
          code: "AUD-MERIDIAN",
          firmName: "Meridian Cyber Assessors",
          accreditation: "PCI QSA & FedRAMP 3PAO",
          specialties: "PCI DSS 4.0, NIST CSF 2.0, IEC 62443",
          regions: "US, APAC",
          leadAuditorEmail: "intake@meridiancyber.example",
          invited: false,
          invitedAt: null,
        },
      ],
      remediationPrs: [
        {
          id: "pr-419",
          controlCode: "CFG-01",
          targetResource: "sg-staging-analytics-mongo",
          repository: "harborline-data/infra-terraform",
          branchName: "d2d/fix-cfg-01-sg-staging-analytics-mongo",
          title: "fix(CFG-01): remediate sg-staging-analytics-mongo via Terraform",
          artifactType: "Terraform",
          unifiedDiff: buildDiff("CFG-01", "sg-staging-analytics-mongo", "Terraform"),
          prNumber: 419,
          prUrl: "https://github.com/harborline-data/infra-terraform/pull/419",
          mode: "signed-receipt",
          status: "Open",
          evidenceId: world.evidence[0]?.id ?? null,
          evidenceSha256:
            world.evidence[0]?.sha256 ??
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
          createdAt: daysAgo(1),
        },
      ],
      nextPrNumber: 420,
    };
    stateByWorld.set(world, state);
  }
  return state;
}

function summarizeEntities(entities: DemoLegalEntity[]) {
  const totalHeadcount = entities.reduce((acc, e) => acc + e.headcount, 0);
  const controlsPassing = entities.reduce((acc, e) => acc + e.controlsPassing, 0);
  const controlsTotal = entities.reduce((acc, e) => acc + e.controlsTotal, 0);
  return {
    entityCount: entities.length,
    totalHeadcount,
    controlsPassing,
    controlsTotal,
    consolidatedReadinessPercent: readiness(controlsPassing, controlsTotal),
  };
}

function computeTimeMachine(
  world: DemoWorld,
  asOfParam: string | null,
  compareToParam: string | null,
) {
  const today = new Date().toISOString().slice(0, 10);
  const ninetyAgo = new Date(Date.now() - 90 * 86_400_000).toISOString().slice(0, 10);
  let from = asOfParam && /^\d{4}-\d{2}-\d{2}$/.test(asOfParam) ? asOfParam : ninetyAgo;
  let to = compareToParam && /^\d{4}-\d{2}-\d{2}$/.test(compareToParam) ? compareToParam : today;
  if (to < from) {
    const tmp = from;
    from = to;
    to = tmp;
  }

  const weeks = [...world.history].sort((a, b) => a.weekOf.localeCompare(b.weekOf));
  const totalControls = world.controls.length;
  const passingControls = world.controls.filter((c) => c.status === "passing").length;
  const currentReadiness = readiness(passingControls, totalControls);
  const latestWeek = weeks[weeks.length - 1];
  const latestPass = latestWeek
    ? readiness(latestWeek.passing, latestWeek.passing + latestWeek.failing)
    : currentReadiness;

  const snapshot = (target: string) => {
    const week = [...weeks].reverse().find((w) => w.weekOf <= target) ?? weeks[0];
    const passingTests = week?.passing ?? 0;
    const failingTests = week?.failing ?? 0;
    const testPassPercent = readiness(passingTests, passingTests + failingTests);
    const estimatedReadinessPercent = Math.min(
      100,
      Math.max(0, currentReadiness - (latestPass - testPassPercent)),
    );
    const cutoff = `${target}T23:59:59.999Z`;
    const evidenceAt = world.evidence.filter((e) => (e.collectedAt ?? "") <= cutoff);
    const latestEv = evidenceAt[0];
    return {
      date: target,
      passingTests,
      failingTests,
      testPassPercent,
      estimatedReadinessPercent,
      chainEntryCount: evidenceAt.length,
      latestChainSequence: evidenceAt.length,
      latestChainHead:
        latestEv?.sha256 ?? "a4f2d18c7e9032bc5d1481e0b87a561c002f3e4419d2b5c8a1023f7e8a9c4b11",
    };
  };

  const asOf = snapshot(from);
  const compareTo = snapshot(to);
  const passPercentDelta = compareTo.testPassPercent - asOf.testPassPercent;
  const readinessDelta = compareTo.estimatedReadinessPercent - asOf.estimatedReadinessPercent;
  const chainEntriesDelta = compareTo.chainEntryCount - asOf.chainEntryCount;
  const direction = readinessDelta >= 0 ? "improved" : "regressed";
  return {
    asOf,
    compareTo,
    passPercentDelta,
    readinessDelta,
    chainEntriesDelta,
    summary: `Posture ${direction} by ${Math.abs(readinessDelta)}% (${asOf.estimatedReadinessPercent}% on ${asOf.date} -> ${compareTo.estimatedReadinessPercent}% on ${compareTo.date}); EvidenceChain grew by ${chainEntriesDelta >= 0 ? "+" : ""}${chainEntriesDelta} verified entries.`,
  };
}

export async function handleGovernanceHub(
  method: string,
  path: string,
  query: URLSearchParams,
  body: Record<string, unknown>,
  world: DemoWorld,
  actor: Actor,
): Promise<Response | null> {
  const isScimPath = path.startsWith("/api/v1/scim/v2/Users/") && path.endsWith("/deprovision");
  const isGovPath = path.startsWith("/api/v1/governance/");
  if (!isGovPath && !isScimPath) return null;

  if (actor.role === "Auditor") {
    return json({ code: "forbidden" }, 403);
  }

  const state = getState(world);

  if (path === "/api/v1/governance/overview" && method === "GET") {
    return json({
      entities: { summary: summarizeEntities(state.entities), entities: state.entities },
      privacyAssessments: state.privacy,
      aiModels: state.aiModels,
      phishingCampaigns: state.phishing,
      attackSurface: state.attackSurface,
      auditorNetwork: state.auditorNetwork,
      remediationPrs: state.remediationPrs,
      timeMachine: computeTimeMachine(world, null, null),
    });
  }

  if (path === "/api/v1/governance/entities" && method === "GET") {
    return json({ summary: summarizeEntities(state.entities), entities: state.entities });
  }

  if (path === "/api/v1/governance/entities" && method === "POST") {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const code = String(body.code ?? "")
      .trim()
      .toUpperCase();
    const name = String(body.name ?? "").trim();
    const jurisdiction = String(body.jurisdiction ?? "").trim();
    const region = String(body.region ?? "us-east-1").trim() || "us-east-1";
    const headcount = Number(body.headcount ?? 0);
    const controlsPassing = Number(body.controlsPassing ?? 0);
    const controlsTotal = Number(body.controlsTotal ?? 0);
    const primaryFramework = String(body.primaryFramework ?? "SOC 2 Type II").trim();
    const status = String(body.status ?? "Active");
    if (
      code.length < 2 ||
      !name ||
      !jurisdiction ||
      headcount < 0 ||
      controlsTotal < 0 ||
      controlsPassing < 0 ||
      controlsPassing > controlsTotal
    ) {
      return json({ code: "invalid_entity" }, 400);
    }
    const existing = state.entities.find((e) => e.code === code || (body.id && e.id === body.id));
    if (existing) {
      Object.assign(existing, {
        code,
        name,
        jurisdiction,
        region,
        headcount,
        controlsPassing,
        controlsTotal,
        readinessPercent: readiness(controlsPassing, controlsTotal),
        primaryFramework,
        status,
      });
      return json(existing);
    }
    const created: DemoLegalEntity = {
      id: `ent-${Date.now()}`,
      code,
      name,
      jurisdiction,
      region,
      headcount,
      controlsPassing,
      controlsTotal,
      readinessPercent: readiness(controlsPassing, controlsTotal),
      primaryFramework,
      status,
      createdAt: new Date().toISOString(),
    };
    state.entities.push(created);
    return json(created);
  }

  if (path === "/api/v1/governance/checkr/sync" && method === "POST") {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    let updated = 0;
    const today = new Date().toISOString().slice(0, 10);
    for (const person of world.people) {
      if (
        person.state === "Active" &&
        !person.backgroundExcluded &&
        (person.background === "Pending" ||
          person.background === "NotStarted" ||
          !person.background)
      ) {
        person.backgroundProvider = "Checkr";
        person.background = "Clear";
        person.backgroundOn = today;
        person.backgroundLink = `https://dashboard.checkr.com/reports/chk-${person.id}`;
        updated++;
      }
    }
    return json({
      provider: "Checkr",
      mode: "example-fixture",
      updatedRecords: updated,
      summary: `Synced ${updated} Checkr background screening record(s) (example-fixture); HR-01 test is Pass.`,
      syncedAt: new Date().toISOString(),
    });
  }

  if (path === "/api/v1/governance/knowbe4/sync" && method === "POST") {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    let updated = 0;
    const today = new Date().toISOString().slice(0, 10);
    for (const person of world.people) {
      if (person.state === "Active" && person.training !== "Complete" && updated < 10) {
        person.training = "Complete";
        person.trainingCompletedOn = today;
        updated++;
      }
    }
    if (state.phishing[0]) {
      state.phishing[0].remedialTrainingStatus = `Auto-assigned (${state.phishing[0].clickedCount}/${state.phishing[0].clickedCount} completed — 100%)`;
      updated++;
    }
    return json({
      provider: "KnowBe4",
      mode: "example-fixture",
      updatedRecords: updated,
      summary: `Synced ${updated} KnowBe4 security awareness & phishing record(s) (example-fixture).`,
      syncedAt: new Date().toISOString(),
    });
  }

  if ((path === "/api/v1/governance/scim/deprovision" || isScimPath) && method === "POST") {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const pathEmail = isScimPath
      ? decodeURIComponent(path.slice("/api/v1/scim/v2/Users/".length, -"/deprovision".length))
      : "";
    const email = (pathEmail || String(body.email ?? "")).trim().toLowerCase();
    const revoke = Boolean(body.revokeDownstreamAccounts);
    const reason = String(body.reason ?? "").trim() || "SCIM 2.0 automated deprovisioning";
    if (!email || !email.includes("@")) return json({ code: "invalid_email" }, 400);
    const person = world.people.find((p) => p.email.toLowerCase() === email);
    if (!person) return json({ code: "not_found" }, 404);

    person.state = "Offboarded";
    person.leftOn = new Date().toISOString().slice(0, 10);
    person.statusReason = reason;
    if (revoke) {
      for (const acct of person.accounts) acct.active = false;
    }
    const remainingActive = person.accounts.filter((a) => a.active).length;
    const leaverFlagged = remainingActive > 0;
    const offboardedCheckOutcome = leaverFlagged ? "Fail" : "Pass";
    const offboardedCheckDetail = leaverFlagged
      ? `SCIM leaver ${person.name} (${person.email}) still has ${remainingActive} active downstream cloud/repo account(s).`
      : `SCIM leaver ${person.name} deprovisioned with zero remaining active accounts.`;

    const hrCheck = world.checks.find((c) => c.key === "offboarded-removed");
    if (hrCheck) {
      hrCheck.outcome = offboardedCheckOutcome;
      hrCheck.detail = offboardedCheckDetail;
    }

    return json({
      email: person.email,
      personName: person.name,
      state: person.state,
      leaverAccessFlagged: leaverFlagged,
      remainingActiveAccounts: remainingActive,
      offboardedCheckOutcome,
      offboardedCheckDetail,
      backgroundCheckOutcome: "Pass",
      processedAt: new Date().toISOString(),
    });
  }

  if (path === "/api/v1/governance/privacy" && method === "GET") return json(state.privacy);
  if (path === "/api/v1/governance/privacy" && method === "POST") {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const code = String(body.code ?? "")
      .trim()
      .toUpperCase();
    const activityName = String(body.activityName ?? "").trim();
    const dataSubjects = String(body.dataSubjects ?? "").trim();
    const lawfulBasis = String(body.lawfulBasis ?? "").trim();
    if (code.length < 2 || !activityName || !dataSubjects || !lawfulBasis) {
      return json({ code: "invalid_privacy_assessment" }, 400);
    }
    const record: DemoPrivacyAssessment = {
      id: String(body.id ?? `priv-${Date.now()}`),
      code,
      kind: body.kind === "DPIA" ? "DPIA" : "RoPA",
      activityName,
      dataSubjects,
      lawfulBasis,
      transferMechanism: String(body.transferMechanism ?? "Standard Contractual Clauses"),
      riskLevel: ["Low", "Medium", "High"].includes(String(body.riskLevel))
        ? String(body.riskLevel)
        : "Medium",
      status: ["Draft", "InReview", "Approved"].includes(String(body.status))
        ? String(body.status)
        : "InReview",
      ownerEmail: String(body.ownerEmail ?? actor.email),
      reviewDueOn: String(
        body.reviewDueOn ?? new Date(Date.now() + 90 * 86_400_000).toISOString().slice(0, 10),
      ),
      updatedAt: new Date().toISOString(),
    };
    const idx = state.privacy.findIndex((p) => p.code === code);
    if (idx >= 0) state.privacy[idx] = record;
    else state.privacy.push(record);
    return json(record);
  }

  if (path === "/api/v1/governance/ai-models" && method === "GET") return json(state.aiModels);
  if (path === "/api/v1/governance/ai-models" && method === "POST") {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const code = String(body.code ?? "")
      .trim()
      .toUpperCase();
    const modelName = String(body.modelName ?? "").trim();
    const provider = String(body.provider ?? "").trim();
    const provenance = String(body.trainingDataProvenance ?? "").trim();
    if (code.length < 2 || !modelName || !provider || !provenance) {
      return json({ code: "invalid_ai_model" }, 400);
    }
    const record: DemoAiModel = {
      id: String(body.id ?? `aim-${Date.now()}`),
      code,
      modelName,
      provider,
      euAiActRiskTier: ["Minimal", "Limited", "High"].includes(String(body.euAiActRiskTier))
        ? String(body.euAiActRiskTier)
        : "Limited",
      trainingDataProvenance: provenance,
      guardrails: String(body.guardrails ?? ""),
      redTeamStatus: ["Passed", "InProgress", "NeedsReview"].includes(String(body.redTeamStatus))
        ? String(body.redTeamStatus)
        : "InProgress",
      linkedControlCode: String(body.linkedControlCode ?? "AI-01").toUpperCase(),
      ownerEmail: String(body.ownerEmail ?? actor.email),
      evaluatedAt: new Date().toISOString(),
    };
    const idx = state.aiModels.findIndex((m) => m.code === code);
    if (idx >= 0) state.aiModels[idx] = record;
    else state.aiModels.push(record);
    return json(record);
  }

  if (path === "/api/v1/governance/phishing" && method === "GET") return json(state.phishing);
  if (path === "/api/v1/governance/phishing" && method === "POST") {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const name = String(body.name ?? "").trim();
    const sentCount = Number(body.sentCount ?? 0);
    const clickedCount = Number(body.clickedCount ?? 0);
    const reportedCount = Number(body.reportedCount ?? 0);
    if (
      !name ||
      sentCount <= 0 ||
      clickedCount < 0 ||
      reportedCount < 0 ||
      clickedCount > sentCount ||
      reportedCount > sentCount
    ) {
      return json({ code: "invalid_phishing_campaign" }, 400);
    }
    const clickRatePercent = rate(clickedCount, sentCount);
    const reportRatePercent = rate(reportedCount, sentCount);
    const created: DemoPhishingCampaign = {
      id: `phish-${Date.now()}`,
      code: `PHISH-${new Date().getFullYear()}-${String(state.phishing.length + 1).padStart(2, "0")}`,
      name,
      source: body.source === "Internal" ? "Internal" : "KnowBe4",
      sentCount,
      clickedCount,
      reportedCount,
      clickRatePercent,
      reportRatePercent,
      riskBand: phishingBand(clickRatePercent),
      remedialTrainingStatus:
        String(body.remedialTrainingStatus ?? "").trim() || `Auto-assigned (${clickedCount} users)`,
      ranAt: new Date().toISOString(),
    };
    state.phishing.unshift(created);
    return json(created);
  }

  if (path === "/api/v1/governance/attack-surface" && method === "GET")
    return json(state.attackSurface);
  if (path === "/api/v1/governance/attack-surface" && method === "POST") {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const hostname = String(body.hostname ?? "")
      .trim()
      .toLowerCase();
    const ipAddress = String(body.ipAddress ?? "").trim();
    const service = String(body.service ?? "").trim();
    const port = Number(body.port ?? 443);
    if (hostname.length < 3 || !ipAddress || !service || port < 1 || port > 65535) {
      return json({ code: "invalid_asm_asset" }, 400);
    }
    const record: DemoAttackSurfaceAsset = {
      id: String(body.id ?? `asm-${Date.now()}`),
      hostname,
      ipAddress,
      service,
      port,
      tlsDaysRemaining: Number(body.tlsDaysRemaining ?? 60),
      shadowIt: Boolean(body.shadowIt),
      severity: ["Critical", "High", "Medium", "Low"].includes(String(body.severity))
        ? String(body.severity)
        : "Medium",
      status: body.status === "Mitigated" ? "Mitigated" : "Open",
      lastSeenAt: new Date().toISOString(),
    };
    const idx = state.attackSurface.findIndex((a) => a.hostname === hostname);
    if (idx >= 0) state.attackSurface[idx] = record;
    else state.attackSurface.unshift(record);
    return json(record);
  }

  if (path === "/api/v1/governance/auditor-network" && method === "GET") {
    return json(state.auditorNetwork);
  }
  if (
    path.startsWith("/api/v1/governance/auditor-network/") &&
    path.endsWith("/invite") &&
    method === "POST"
  ) {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const id = path.slice("/api/v1/governance/auditor-network/".length, -"/invite".length);
    const firm = state.auditorNetwork.find((f) => f.id === id);
    if (!firm) return json({ code: "not_found" }, 404);
    firm.invited = true;
    firm.invitedAt = new Date().toISOString();
    return json(firm);
  }

  if (path === "/api/v1/governance/remediation-prs" && method === "GET") {
    return json(state.remediationPrs);
  }
  if (path === "/api/v1/governance/remediation-prs" && method === "POST") {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const controlCode = String(body.controlCode ?? "CFG-01")
      .trim()
      .toUpperCase();
    const targetResource = String(body.targetResource ?? "").trim();
    const repository = String(body.repository ?? "harborline-data/infra-terraform").trim();
    const artifactType = ["Terraform", "Dockerfile", "GitHubWorkflow", "IamPolicy"].includes(
      String(body.artifactType),
    )
      ? String(body.artifactType)
      : "Terraform";
    if (!controlCode || !targetResource) return json({ code: "invalid_remediation_pr" }, 400);

    const branchName = `d2d/fix-${slugify(controlCode)}-${slugify(targetResource)}`;
    const title = `fix(${controlCode}): remediate ${targetResource} via ${artifactType}`;
    const unifiedDiff = buildDiff(
      controlCode,
      targetResource,
      artifactType,
      typeof body.customFix === "string" ? body.customFix : undefined,
    );
    const prNumber = state.nextPrNumber++;
    const prUrl = `https://github.com/${repository}/pull/${prNumber}`;
    const nowIso = new Date().toISOString();
    const receiptBody = JSON.stringify(
      {
        kind: "remediation_pr_receipt_v1",
        controlCode,
        targetResource,
        repository,
        branchName,
        prNumber,
        prUrl,
        artifactType,
        unifiedDiff,
        issuedAtUtc: nowIso,
        actor: actor.email,
      },
      null,
      2,
    );
    const evidenceSha256 = await sha256Hex(receiptBody);
    const evidenceId = `ev-pr-${prNumber}`;
    world.evidence.unshift({
      id: evidenceId,
      controlCode,
      title: `Remediation PR #${prNumber} (${controlCode} — ${targetResource})`,
      source: "Remediation PR",
      collectedAt: nowIso,
      sha256: evidenceSha256,
      integrity: "Verified",
      lockedUntil: new Date(Date.now() + 6 * 365 * 86_400_000).toISOString(),
      body: receiptBody,
    });

    const created: DemoRemediationPr = {
      id: `pr-${prNumber}`,
      controlCode,
      targetResource,
      repository,
      branchName,
      title,
      artifactType,
      unifiedDiff,
      prNumber,
      prUrl,
      mode: "signed-receipt",
      status: "Open",
      evidenceId,
      evidenceSha256,
      createdAt: nowIso,
    };
    state.remediationPrs.unshift(created);
    return json(created);
  }

  if (path === "/api/v1/governance/time-machine" && method === "GET") {
    return json(computeTimeMachine(world, query.get("asOf"), query.get("compareTo")));
  }

  return null;
}
