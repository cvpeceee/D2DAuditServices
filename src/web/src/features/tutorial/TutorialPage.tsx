import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { Pill } from "../../shared/ui/ui";

export type TutorialNode = {
  stage: string;
  detail: string;
  payload: string;
  invariant: string;
};

export type TutorialChapter = {
  number: number;
  id: string;
  title: string;
  shortTitle: string;
  startTime: number;
  endTime: number;
  duration: number;
  moduleRoute: string;
  moduleLabel: string;
  secondaryRoute: string;
  secondaryLabel: string;
  badge: string;
  codeSnippet: string;
  narration: string;
  nodes: TutorialNode[];
  bullets: string[];
};

export type PacketFlowPreset = {
  id: string;
  title: string;
  subtitle: string;
  moduleRoute: string;
  moduleLabel: string;
  hops: {
    step: string;
    actor: string;
    protocol: string;
    detail: string;
    verification: string;
  }[];
};

export const SUPER_APP_MILESTONES = [
  {
    code: "M1–M6",
    title:
      "Core Compliance, Collectors, Evidence Chain, Auditor Portal, Risk, People, MDM & Trust Foundation",
    percent: 100,
    status: "Completed",
    route: "/",
  },
  {
    code: "M7",
    title:
      "Security Hardening, Per-Auditor Storage Prefix Isolation & Real Jira/Linear/Slack/Teams/SMTP Adapters",
    percent: 100,
    status: "Completed",
    route: "/auditor",
  },
  {
    code: "M8",
    title:
      "VRINDA Cloud Security Intelligence: Public Exposure + IaC Patches, SARIF 2.1.0, DSPM, FinOps & Attack Paths",
    percent: 100,
    status: "Completed",
    route: "/attack-paths",
  },
  {
    code: "CI/CD",
    title:
      "Open-Source Self-Hosted Jenkins CI/CD Stack (Zero GitHub Actions Auto-Trigger Minute Consumption)",
    percent: 100,
    status: "Completed",
    route: "/settings",
  },
  {
    code: "M9",
    title:
      "13 Frameworks + What-If Simulator, Trust Center v2 (X-Trust-Token & Crypto Receipts), Grounded AI & Runtime API Keys/Webhooks",
    percent: 100,
    status: "Completed",
    route: "/trust/harborline",
  },
  {
    code: "M10",
    title:
      "Governance Hub (Multi-Entity, RoPA/DPIA, AI Registry, Checkr/KnowBe4/SCIM, ASM PRs, Time Machine), Render & 1080p Tutorial",
    percent: 100,
    status: "Completed",
    route: "/governance-hub",
  },
] as const;

export const GAP_TRACKER_ITEMS = [
  {
    id: 1,
    priority: "P0",
    title: "Live AWS, Azure & GCP collectors (16 cross-cloud checks mapped to controls)",
    status: "Done",
    route: "/integrations",
  },
  {
    id: 2,
    priority: "P0",
    title: "Framework readiness percent & 1-page executive PDF export",
    status: "Done",
    route: "/frameworks",
  },
  {
    id: 3,
    priority: "P0",
    title: "Security incident register, employee /me reporting & SLA test",
    status: "Done",
    route: "/incidents",
  },
  {
    id: 4,
    priority: "P0",
    title: "Change management from GitHub PRs & Jira with production review test",
    status: "Done",
    route: "/changes",
  },
  {
    id: 5,
    priority: "P0",
    title: "Security questionnaire automation with knowledge base, citations & export",
    status: "Done",
    route: "/questionnaires",
  },
  {
    id: 6,
    priority: "P0",
    title: "SOC 2 system description drafted from tenant data & bundled in audit zip",
    status: "Done",
    route: "/system-description",
  },
  {
    id: 7,
    priority: "P0",
    title: "Business continuity impact analysis, DR plans & recovery exercise log",
    status: "Done",
    route: "/continuity",
  },
  {
    id: 8,
    priority: "P0",
    title: "Live AWS Inspector & GitHub Dependabot ingest + pentest tracking",
    status: "Done",
    route: "/vulnerabilities",
  },
  {
    id: 9,
    priority: "P1",
    title: "Device agent check-in MVP & MDM import (Jamf, Intune, Kandji)",
    status: "Done",
    route: "/people/computers",
  },
  {
    id: 10,
    priority: "P1",
    title: "Real Jira, Linear, Slack, Microsoft Teams, Resend & SMTP TLS automation",
    status: "Done",
    route: "/settings",
  },
  {
    id: 11,
    priority: "P1",
    title:
      "13 frameworks (PCI 4.0, ISO 42001, NIST 2.0, DORA, EU AI Act, IEC 62443) + What-If Simulator",
    status: "Done",
    route: "/frameworks",
  },
  {
    id: 12,
    priority: "P1",
    title: "Runtime Bearer d2d_api_... API keys, OpenAPI v1 & HMAC-SHA256 outbound webhooks",
    status: "Done",
    route: "/settings",
  },
  {
    id: 13,
    priority: "P1",
    title: "Checkr background check adapter & KnowBe4 security awareness / phishing sync",
    status: "Done",
    route: "/governance-hub",
  },
  {
    id: 14,
    priority: "P2",
    title: "Multi-entity parent/subsidiary workspaces & consolidated readiness rollup",
    status: "Done",
    route: "/governance-hub",
  },
  {
    id: 15,
    priority: "P2",
    title: "Trust Center v2: clickwrap NDA (X-Trust-Token), Cryptographic Receipts & Trust AI Q&A",
    status: "Done",
    route: "/trust/harborline",
  },
  {
    id: 16,
    priority: "P2",
    title: "Role-scoped Grounded AI Assistant over product docs & live tenant posture",
    status: "Done",
    route: "/frameworks",
  },
  {
    id: 17,
    priority: "P2",
    title: "Production Terraform (ECS Fargate, RDS, S3 Object Lock, EU region) & Render Blueprint",
    status: "Done",
    route: "/evidence",
  },
  {
    id: 18,
    priority: "P3",
    title:
      "GDPR RoPA & DPIA, AI Model Registry, Attack Surface Monitoring PRs, Time Machine & Auditor Network",
    status: "Done",
    route: "/governance-hub",
  },
] as const;

export const PACKET_FLOW_PRESETS: PacketFlowPreset[] = [
  {
    id: "evidence-to-trust-receipt",
    title:
      "Cloud Collector -> Parquet + SHA-256 Chain -> Cryptographic Trust Receipt -> Buyer Browser Verification",
    subtitle:
      "End-to-end cryptographic provenance from cloud API snapshot to zero-trust buyer verification in Web Crypto",
    moduleRoute: "/trust/harborline",
    moduleLabel: "Open Trust Center & Verify Receipt",
    hops: [
      {
        step: "1. Cloud Collector",
        actor: "AwsCloudProvider / AzureCloudProvider / GcpCloudProvider",
        protocol: "Parquet + Canonical Snapshot Bytes",
        detail:
          "Collects 16 live multi-cloud posture checks (IAM MFA, S3/Blob public access, KMS rotation, CloudTrail/Audit logs, GuardDuty).",
        verification: "Serialized per tenant via EvidenceWriter SemaphoreSlim lock",
      },
      {
        step: "2. Parquet + SHA-256 Chain",
        actor: "EvidenceChain + S3 Object Lock",
        protocol: "Head = SHA256(PreviousHead \\n ContentHash \\n Canonical)",
        detail:
          "Appends tamper-evident ChainEntry linking ContentHash to PreviousHead with WORM retention under S3 COMPLIANCE-mode Object Lock.",
        verification: "EnterprisePortal.VerifyChainContinuity walks genesis -> head",
      },
      {
        step: "3. Cryptographic Trust Receipt",
        actor: "TrustReceiptBuilder (/api/v1/public/trust/{slug}/receipt)",
        protocol: "Canonical JSON Payload + Head Entry Hash",
        detail:
          "Embeds live framework pass counts, retention policy, and verified EvidenceChain head hash into a deterministic canonical payload.",
        verification: "ChainStatus == 'Verified' required before issuing receipt",
      },
      {
        step: "4. Buyer Browser Verification",
        actor: "TrustPublicPage Web Crypto API (crypto.subtle.digest)",
        protocol: "Client-Side SHA-256 Digest Comparison",
        detail:
          "Buyer browser hashes canonicalPayload locally via Web Crypto SHA-256 and compares against receiptHash with zero server trust.",
        verification: "100% browser-verified cryptographic match + X-Trust-Token NDA header",
      },
    ],
  },
  {
    id: "exposure-to-pii-attack-path",
    title: "Internet Exposure -> Leaked Secret -> Identity Gap -> PII Store Attack Path",
    subtitle:
      "Multi-hop blast-radius correlation engine connecting network posture, SARIF secrets, IAM hygiene, and DSPM stores",
    moduleRoute: "/attack-paths",
    moduleLabel: "Open Blast-Radius Attack Paths",
    hops: [
      {
        step: "1. Internet Exposure",
        actor: "ExposureAnalyzer (/exposure)",
        protocol: "0.0.0.0/0 Ingress / Public Storage Endpoint",
        detail:
          "Detects internet-exposed security group sg-0a9f21e4 on port 22/443 and generates copy-ready Terraform HCL & AWS CLI lockdown patches.",
        verification: "Chokepoint remediation + 1-click GitHub PR generator",
      },
      {
        step: "2. Leaked Secret",
        actor: "SarifParser (/vulnerabilities)",
        protocol: "SARIF 2.1.0 Secret & Container CVE Scan",
        detail:
          "Correlates baked-in service credentials or critical container RCE on the exposed workload (with automatic secret snippet redaction).",
        verification: "SarifParser.RedactSnippet neutralizes raw credentials at ingest",
      },
      {
        step: "3. Identity Gap",
        actor: "AccessReviews & Directory Sync (/people)",
        protocol: "Privileged Principal Missing MFA / Stale Leaver",
        detail:
          "Identifies lateral movement path through an unrotated IAM principal or privileged account lacking hardware MFA enforcement.",
        verification: "Maps directly to AC-01, AC-04, and HR-03 failing tests",
      },
      {
        step: "4. PII Store Attack Path",
        actor: "DspmScorer & AttackPathAnalyzer (/dspm & /attack-paths)",
        protocol: "Blast-Radius Score 96/100 -> Restricted PII/PHI Store",
        detail:
          "Terminates at production customer PII data store, ranking the full 4-hop chain by blast radius and naming the single chokepoint fix.",
        verification: "Closing Hop 1 or Hop 2 collapses the entire multi-hop path",
      },
    ],
  },
];

export const TUTORIAL_CHAPTERS: TutorialChapter[] = [
  {
    number: 1,
    id: "ch1-executive-overview",
    title:
      "Chapter 1: Executive Overview — The Vanta + Drata + VRINDA All-in-One Super App & Database-per-Tenant Architecture",
    shortTitle: "Executive Overview & DB-per-Tenant",
    startTime: 0,
    endTime: 21.5,
    duration: 21.5,
    moduleRoute: "/frameworks",
    moduleLabel: "Open Live Module: Frameworks",
    secondaryRoute: "/settings",
    secondaryLabel: "Open Settings & Tenancy",
    badge: "CONTROL PLANE + DEDICATED TENANT DB | 18/18 GAPS DONE | M1–M10 100%",
    codeSnippet:
      "TenantDbContext -> postgres://tenant_{id} | ProviderClaim uniqueness | Zero cross-tenant leakage",
    narration:
      "Welcome to D2D Audit Services, the all-in-one compliance and cloud security super app unifying Vanta, Drata, and VRINDA in a single platform. Every customer workspace runs inside a dedicated PostgreSQL database per tenant, orchestrated by a central Control Plane with strict Provider Claim uniqueness and role-scoped authorization across all ten milestones.",
    nodes: [
      {
        stage: "Control Plane",
        detail: "WorkOS / Local Auth + Tenant Resolver",
        payload: "POST /api/v1/auth/login -> HttpOnly Session + CSRF Token",
        invariant:
          "ProviderClaim prevents cross-tenant cloud account hijacking (409 already_claimed)",
      },
      {
        stage: "Dedicated Tenant DB",
        detail: "Isolated Postgres Schema & Claims",
        payload: "TenantResolutionMiddleware -> Scoped TenantDbContext",
        invariant: "SchemaParityTests enforces HasPendingModelChanges() == false",
      },
      {
        stage: "Continuous Collectors",
        detail: "AWS, Azure, GCP, GitHub & MDM",
        payload: "CollectionRunner -> 16 Cross-Cloud Checks + check_weeks",
        invariant: "128-bit cryptographic OAuth state nonces verified in constant time",
      },
      {
        stage: "Unified Super App",
        detail: "13 Frameworks + Cloud Sec + Trust",
        payload: "62 Controls x 13 Frameworks + VRINDA Cloud Security + Governance Hub",
        invariant: "WorkspaceMember policy isolates external Auditors from internal telemetry",
      },
    ],
    bullets: [
      "100% physical database-per-tenant isolation with automated EF Core migration parity gates",
      "Unifies continuous GRC compliance, cloud posture, blast-radius attack paths, and buyer trust",
      "18 of 18 Gap Tracker capabilities and Milestones M1 through M10 delivered end-to-end",
    ],
  },
  {
    number: 2,
    id: "ch2-cspm-frameworks",
    title:
      "Chapter 2: Multi-Cloud CSPM (AWS, Azure, GCP) & 13 Compliance Frameworks + What-If Gap Simulator",
    shortTitle: "Multi-Cloud CSPM & 13 Frameworks",
    startTime: 21.5,
    endTime: 62.5,
    duration: 41,
    moduleRoute: "/frameworks",
    moduleLabel: "Open Live Module: 13 Frameworks & Simulator",
    secondaryRoute: "/integrations",
    secondaryLabel: "Open Multi-Cloud Collectors",
    badge: "13 EMBEDDED FRAMEWORKS | CUSTOM CSV UPLOAD | 1-CLICK WHAT-IF GAP ADOPTION",
    codeSnippet:
      "POST /api/v1/frameworks/{key}/simulator/adopt -> adopts missing controls & recalculates readiness",
    narration:
      "Chapter Two covers Multi-Cloud CSPM and our thirteen embedded compliance frameworks. Live collectors inspect AWS, Microsoft Azure, and Google Cloud across sixteen automated checks. Security teams track readiness across SOC 2, ISO 27001, 27017, 27018, 42001, HIPAA, GDPR, PCI DSS 4.0, NIST CSF 2.0, DORA, NIST AI RMF, the EU AI Act, and IEC 62443, and run the What-If Gap Simulator to adopt missing controls in one click.",
    nodes: [
      {
        stage: "AWS + Azure + GCP",
        detail: "16 Live Cross-Cloud Collector Checks",
        payload: "AwsCloudProvider + AzureCloudProvider + GcpCloudProvider",
        invariant: "Covers IAM MFA, storage public access, KMS/KeyVault, logging & network rules",
      },
      {
        stage: "Control Crosswalk",
        detail: "62 Controls Mapped Across 13 Catalogs",
        payload: "soc2, iso27001, pci-dss, nist-csf, dora, iso42001, eu-ai-act, iec62443...",
        invariant:
          "One control test automatically satisfies mapped requirements across all 13 frameworks",
      },
      {
        stage: "What-If Simulator",
        detail: "Projected Readiness & Gap Delta",
        payload: "GET /api/v1/frameworks/{key}/simulator -> current vs projected readiness",
        invariant: "Pure domain FrameworkSimulator computes exact requirement coverage delta",
      },
      {
        stage: "1-Click Adoption",
        detail: "Auto-Creates & Maps Missing Controls",
        payload: "POST /api/v1/frameworks/{key}/simulator/adopt",
        invariant: "Idempotently adopts missing controls and refreshes live readiness metrics",
      },
    ],
    bullets: [
      "Live AWS, Azure Entra/Storage/KeyVault/NSG, and GCP IAM/Buckets/KMS/Audit collector pipeline",
      "13 built-in regulatory and AI frameworks plus custom CSV framework upload and control import",
      "Interactive What-If Gap Simulator shows immediate readiness lift and adopts gap controls in 1 click",
    ],
  },
  {
    number: 3,
    id: "ch3-exposure-attack-paths",
    title:
      "Chapter 3: Cloud Security Intelligence — Public Exposure, 1-Click Terraform/CLI Remediation & Blast-Radius Attack Paths",
    shortTitle: "Public Exposure & Blast-Radius Attack Paths",
    startTime: 62.5,
    endTime: 91.5,
    duration: 29,
    moduleRoute: "/exposure",
    moduleLabel: "Open Live Module: Public Exposure",
    secondaryRoute: "/attack-paths",
    secondaryLabel: "Open Blast-Radius Attack Paths",
    badge: "INTERNET EXPOSURE -> LEAKED SECRET -> IDENTITY GAP -> PII STORE",
    codeSnippet:
      'aws ec2 revoke-security-group-ingress --port 22 | resource "aws_security_group_rule" "lockdown"',
    narration:
      "Chapter Three introduces VRINDA-grade Cloud Security Intelligence and Blast-Radius Attack Paths. The Exposure engine detects internet-open security groups, public buckets, and exposed databases, generating copy-ready Terraform HCL, AWS CLI, Azure CLI, and GCloud remediation patches. Our Attack Path Analyzer correlates public exposures, leaked secrets, identity gaps, and PII stores into multi-hop blast-radius graphs with a single chokepoint fix.",
    nodes: [
      {
        stage: "Internet Exposure",
        detail: "0.0.0.0/0 SSH/RDP & Public Buckets",
        payload: "ExposureAnalyzer -> Terraform HCL + AWS/Azure/GCP CLI patch",
        invariant: "Deterministic risk scoring by port sensitivity and public CIDR scope",
      },
      {
        stage: "Leaked Secret / CVE",
        detail: "Baked-in Token or Critical RCE",
        payload: "SecurityScanFinding -> Container CVE or Redacted Secret",
        invariant: "Correlates workload vulnerabilities exposed by Hop 1 ingress",
      },
      {
        stage: "Identity Gap",
        detail: "Admin Principal Missing MFA / Stale",
        payload: "PersonAccount / Directory Identity -> MFA disabled or unreviewed",
        invariant: "Connects workload compromise to lateral privilege escalation",
      },
      {
        stage: "PII Store Target",
        detail: "Restricted Customer Data Reached",
        payload: "AttackPathAnalyzer.Build -> Blast Radius Score + Chokepoint Fix",
        invariant: "Identifies highest-leverage chokepoint control to sever the attack path",
      },
    ],
    bullets: [
      "Continuous public exposure scanning with deterministic Terraform HCL and cloud CLI remediation patches",
      "Multi-hop blast-radius correlation: Internet Exposure -> Secret/CVE -> Identity Gap -> PII Data Store",
      "1-click GitHub Pull Request remediation generator in Governance Hub to close exposure chokepoints",
    ],
  },
  {
    number: 4,
    id: "ch4-sarif-dspm-finops",
    title:
      "Chapter 4: Container CVEs, OSS Licenses, Baked-in Secrets (SARIF 2.1.0), DSPM PII Discovery & FinOps × Compliance",
    shortTitle: "SARIF 2.1.0, DSPM PII & FinOps × Compliance",
    startTime: 91.5,
    endTime: 119.5,
    duration: 28,
    moduleRoute: "/dspm",
    moduleLabel: "Open Live Module: DSPM PII Discovery",
    secondaryRoute: "/finops",
    secondaryLabel: "Open FinOps × Compliance",
    badge: "SARIF 2.1.0 SECRET REDACTION | DSPM PII/PHI/PCI | FINOPS x CONTROL CORRELATION",
    codeSnippet:
      "SarifParser.RedactSnippet() -> [REDACTED-SECRET] | FinOps savings linked to CFG-01, CR-02, BC-01",
    narration:
      "Chapter Four unites application vulnerability scanning, Data Security Posture Management, and FinOps. Engineers import SARIF 2.1.0 reports to track container CVEs, copyleft open-source licenses, and baked-in secrets with automatic token redaction. DSPM classifies PII, PHI, and PCI data stores by encryption and residency risk, while FinOps correlates cloud cost savings directly with failing controls.",
    nodes: [
      {
        stage: "SARIF 2.1.0 Ingest",
        detail: "Container CVEs, Licenses & Redacted Secrets",
        payload: "POST /api/v1/security/scans/sarif -> SarifParser.Parse",
        invariant: "SarifParser.RedactSnippet scrubs secret values before DB persistence",
      },
      {
        stage: "DSPM Classification",
        detail: "PII / PHI / PCI & Residency Scoring",
        payload: "DspmScorer.Score -> DataClassification + Encryption + Residency",
        invariant: "Flags unencrypted or publicly accessible PII/PHI stores as Critical",
      },
      {
        stage: "FinOps Analyzer",
        detail: "Idle & Unencrypted Resource Waste",
        payload: "FinOpsAnalyzer -> Projected & Realized Monthly Savings ($)",
        invariant: "Quantifies monthly cloud savings alongside security posture lift",
      },
      {
        stage: "Control Alignment",
        detail: "Closes CFG-01, CR-02 & BC-01 Failures",
        payload: "LinkedControlCode -> CFG-01, CR-02, BC-01",
        invariant: "Applying a FinOps recommendation directly resolves its linked compliance gap",
      },
    ],
    bullets: [
      "Automatic secret redaction on SARIF 2.1.0 ingestion prevents credential leakage inside scan findings",
      "DSPM evaluates encryption at rest, public access, data residency, and retention across cloud stores",
      "FinOps × Compliance links every cost optimization directly to the compliance controls it resolves",
    ],
  },
  {
    number: 5,
    id: "ch5-evidence-chain-time-machine",
    title:
      "Chapter 5: Tamper-Evident Evidence Chain (Parquet + SHA-256 + S3 Object Lock) & Audit Time Machine",
    shortTitle: "SHA-256 Evidence Chain & Audit Time Machine",
    startTime: 119.5,
    endTime: 144.5,
    duration: 25,
    moduleRoute: "/evidence",
    moduleLabel: "Open Live Module: Evidence Chain",
    secondaryRoute: "/governance-hub",
    secondaryLabel: "Open Audit Time Machine",
    badge: "PARQUET SNAPSHOTS | SHA-256 HASH CHAIN | S3 OBJECT LOCK | AUDIT TIME MACHINE",
    codeSnippet:
      "Head = SHA256(PreviousHead\\nContentHash\\nCanonical) | S3 COMPLIANCE Object Lock | Web Crypto Verify",
    narration:
      "Chapter Five showcases our tamper-evident Evidence Chain and the Audit Time Machine. Every collector snapshot and uploaded artifact is serialized to Parquet or raw bytes, hashed with SHA-256, and chained to the previous entry hash in S3 Object Lock compatible storage. Users can verify byte integrity locally in their browser via Web Crypto and diff historical posture snapshots across any two dates in the Audit Time Machine.",
    nodes: [
      {
        stage: "Cloud Collector",
        detail: "Parquet & Artifact Byte Capture",
        payload: "EvidenceWriter.WriteAsync -> S3 / Local Artifact Store",
        invariant: "Per-tenant SemaphoreSlim guarantees monotonic sequence numbers",
      },
      {
        stage: "SHA-256 Hash Link",
        detail: "Chained to Previous Sequence Hash",
        payload: "EvidenceChain.ComputeEntryHash(seq, prevHash, contentSha256)",
        invariant: "Any mutated byte invalidates the entry hash and all subsequent links",
      },
      {
        stage: "S3 Object Lock",
        detail: "Immutable WORM Retention Storage",
        payload: "infra/terraform/main.tf -> S3 Object Lock GOVERNANCE mode",
        invariant: "Enforces multi-year audit retention across US and EU regions",
      },
      {
        stage: "Time Machine & Verify",
        detail: "Historical Diff & Browser Web Crypto",
        payload: "Web Crypto SHA-256 Verify + Audit Time Machine Snapshot Diff",
        invariant: "Compares readiness, passing tests, and chain head hashes across dates",
      },
    ],
    bullets: [
      "Per-tenant serialized EvidenceWriter prevents sequence collisions and links every SHA-256 entry hash",
      "Browser-native Web Crypto SHA-256 verification proves downloaded bytes match the recorded chain hash",
      "Audit Time Machine compares historical posture snapshots to show exact control and evidence deltas",
    ],
  },
  {
    number: 6,
    id: "ch6-trust-center-v2",
    title:
      "Chapter 6: Trust Center v2 — Clickwrap NDA (X-Trust-Token), Buyer-Verifiable Cryptographic Receipts & Trust AI Q&A",
    shortTitle: "Trust Center v2 & Cryptographic Receipts",
    startTime: 144.5,
    endTime: 169,
    duration: 24.5,
    moduleRoute: "/trust/harborline",
    moduleLabel: "Open Live Module: Public Trust Center",
    secondaryRoute: "/trust-center",
    secondaryLabel: "Open Trust Center Admin",
    badge: "X-TRUST-TOKEN NDA HEADER | FULL-CHAIN CRYPTOGRAPHIC RECEIPT | PUBLIC TRUST AI Q&A",
    codeSnippet:
      "GET /api/v1/public/trust/{slug}/receipt -> Verify canonical payload SHA-256 in Buyer Browser",
    narration:
      "Chapter Six demonstrates Trust Center version two. Prospective buyers inspect live framework readiness, submit clickwrap NDA requests, and download gated reports using constant-time validated X-Trust-Token headers without leaking tokens in URLs. Buyers can independently verify the Cryptographic Trust Receipt anchored to the workspace evidence chain head hash, or ask the grounded Trust Center AI assistant.",
    nodes: [
      {
        stage: "Evidence Chain Head",
        detail: "Full-Chain Continuity Verified",
        payload: "EnterprisePortal.VerifyChainContinuity -> ChainStatus: Verified",
        invariant: "Walks every link from sequence 1 to head before minting a receipt",
      },
      {
        stage: "Canonical Trust Receipt",
        detail: "Deterministic JSON Posture Payload",
        payload: "TrustReceiptBuilder.Build -> CanonicalPayload + ReceiptSha256",
        invariant: "Binds live framework readiness percentages to the evidence chain head hash",
      },
      {
        stage: "Buyer Browser Crypto",
        detail: "1-Click Local SHA-256 Verification",
        payload: "crypto.subtle.digest('SHA-256', canonicalPayloadBytes)",
        invariant: "Zero-trust client-side verification right inside /trust/:slug",
      },
      {
        stage: "X-Trust-Token NDA",
        detail: "Header-Gated Report Downloads & AI Q&A",
        payload: "Header: X-Trust-Token + CryptographicOperations.FixedTimeEquals",
        invariant: "Prevents NDA token leakage in URL query strings, logs, or Referer headers",
      },
    ],
    bullets: [
      "Cryptographic Trust Receipts verify full-chain continuity from genesis to head hash before signing",
      "Constant-time X-Trust-Token header validation protects NDA-gated documents from URL/referer leakage",
      "Public Trust Center AI Q&A answers buyer security questions strictly from published posture data",
    ],
  },
  {
    number: 7,
    id: "ch7-governance-workforce-privacy-ai",
    title:
      "Chapter 7: Workforce, Checkr, KnowBe4, MDM/Endpoint Agent, RoPA/DPIA, AI Model Registry & Multi-Entity Rollup",
    shortTitle: "Governance Hub: Multi-Entity, Privacy & AI",
    startTime: 169,
    endTime: 199,
    duration: 30,
    moduleRoute: "/governance-hub",
    moduleLabel: "Open Live Module: Governance Hub",
    secondaryRoute: "/people",
    secondaryLabel: "Open Workforce & MDM",
    badge:
      "MULTI-ENTITY ROLLUP | GDPR ROPA & DPIA | AI MODEL REGISTRY | CHECKR + KNOWBE4 + SCIM 2.0",
    codeSnippet:
      "GET /api/v1/governance-hub/board -> Multi-Entity Readiness, RoPA/DPIA, AI Risk Tiers & SCIM",
    narration:
      "Chapter Seven tours workforce security and the Enterprise Governance Hub. D2D integrates Jamf, Intune, Kandji, and endpoint device agent check-ins alongside Checkr background checks, KnowBe4 phishing simulations, and SCIM 2.0 directory sync. The Governance Hub adds parent-child Multi-Entity Rollups, GDPR Record of Processing Activities and DPIA assessments, and an AI Model Governance Registry mapped to ISO 42001 and the EU AI Act.",
    nodes: [
      {
        stage: "Workforce & MDM",
        detail: "Checkr, KnowBe4, SCIM 2.0 & Agent",
        payload: "Checkr + KnowBe4 Phishing + SCIM 2.0 + Jamf/Intune/Kandji/Agent",
        invariant:
          "Automates HR-02 background checks, AT-01 phishing failure rates, and MDM posture",
      },
      {
        stage: "Privacy RoPA & DPIA",
        detail: "GDPR Article 30 & Impact Scoring",
        payload: "ProcessingActivity (RoPA) + DPIA Risk & Safeguard Register",
        invariant: "Links lawful basis, data subjects, international transfers, and DPIA sign-off",
      },
      {
        stage: "AI Model Registry",
        detail: "EU AI Act Tiering & Guardrails",
        payload: "AiSystemRecord -> Risk Tier, Bias/Hallucination Tests & Oversight",
        invariant:
          "Maps production LLMs and ML models directly to ISO 42001 and EU AI Act controls",
      },
      {
        stage: "Multi-Entity Rollup",
        detail: "Parent / Subsidiary Consolidated Score",
        payload: "EntityUnit Rollup -> Harborline US, EU, APAC & Federal Subsidiaries",
        invariant: "Computes weighted consolidated readiness and surfaces subsidiary control gaps",
      },
    ],
    bullets: [
      "Live & deterministic adapters for Checkr background checks, KnowBe4 phishing campaigns, and SCIM 2.0",
      "GDPR RoPA & DPIA registers plus AI Model Registry tracking bias/hallucination tests and human oversight",
      "Multi-Entity Rollup aggregates readiness, controls, and open risks across global business units",
    ],
  },
  {
    number: 8,
    id: "ch8-auditor-api-webhooks-jenkins",
    title:
      "Chapter 8: Auditor Workspace, Grounded AI Assistant, Developer API Keys (Bearer d2d_api_...), Signed Webhooks & Open-Source Jenkins CI",
    shortTitle: "Auditor Portal, API Keys, Webhooks & Jenkins CI",
    startTime: 199,
    endTime: 228,
    duration: 29,
    moduleRoute: "/auditor",
    moduleLabel: "Open Live Module: Auditor Workspace",
    secondaryRoute: "/settings",
    secondaryLabel: "Open Developer API Keys & Webhooks",
    badge:
      "BEARER d2d_api_... | HMAC-SHA256 WEBHOOKS | AUDITOR ISOLATION | JENKINS + RENDER DEPLOY",
    codeSnippet:
      "Authorization: Bearer d2d_api_<tenant>_<secret> | X-D2D-Signature: t=1763680000,v1=<hmac_sha256>",
    narration:
      "Finally, Chapter Eight covers the External Auditor Workspace, Grounded AI Assistant, Developer Console, and CI/CD infrastructure. External auditors operate under strict framework and storage-prefix isolation. Developers automate workflows with tenant-bound Bearer API keys and SSRF-hardened HMAC-SHA256 outbound webhooks. The entire stack builds via self-hosted open-source Jenkins CI and deploys cleanly via Terraform or Render Blueprint.",
    nodes: [
      {
        stage: "Auditor Isolation",
        detail: "Framework-Scoped Portal & ZIP Prefix",
        payload: "{tenantId}/exports/auditor-{auditorUserId}/{jobId}.zip",
        invariant:
          "Auditors see only granted frameworks and in-window evidence; blocked from admin zips",
      },
      {
        stage: "Grounded AI Sidecar",
        detail: "Role-Scoped Citations & Retrieval",
        payload: "IAssistantProvider -> OpenAI-compatible or deterministic GroundedAssistant",
        invariant:
          "Auditor queries are strictly filtered to granted frameworks with exact citations",
      },
      {
        stage: "Developer API & Hooks",
        detail: "Bearer d2d_api_... & HMAC-SHA256",
        payload: "ApiKeyAuthenticator (SHA-256) + WebhookDispatcher (SSRF + DNS guard)",
        invariant: "Constant-time key check, tenant lock, and public-IP DNS resolution before POST",
      },
      {
        stage: "Jenkins CI & Render",
        detail: "Zero GH Actions Minutes + IaC Blueprint",
        payload: "Jenkinsfile + ci/jenkins/* + render.yaml + infra/terraform/*.tf",
        invariant: "100% open-source CI pipeline + turnkey Render and AWS/EU Terraform deployment",
      },
    ],
    bullets: [
      "External auditors are isolated by WorkspaceMember policies, granted frameworks, and dedicated S3 zip prefixes",
      "Runtime SHA-256 API key authentication and DNS-rebinding-protected HMAC-SHA256 outbound webhooks",
      "Turnkey open-source Jenkins CI stack, AWS/EU Object Lock Terraform modules, and Render cloud blueprint",
    ],
  },
];

export const DEEP_LINK_MODULES = [
  { route: "/frameworks", label: "Frameworks & What-If Simulator", badge: "13 Catalogs" },
  { route: "/exposure", label: "Public Exposure & IaC Patches", badge: "Terraform / CLI" },
  { route: "/attack-paths", label: "Blast-Radius Attack Paths", badge: "Multi-Hop Graph" },
  { route: "/dspm", label: "DSPM PII/PHI/PCI Discovery", badge: "Data Posture" },
  { route: "/finops", label: "FinOps × Compliance", badge: "Cost × Controls" },
  { route: "/trust/harborline", label: "Public Trust Center v2", badge: "Crypto Receipt" },
  { route: "/auditor", label: "External Auditor Workspace", badge: "Scoped Portal" },
  { route: "/governance-hub", label: "Enterprise Governance Hub", badge: "M10 Rollup & AI" },
  { route: "/settings", label: "Settings & Developer Console", badge: "API Keys & Hooks" },
] as const;

function formatTimestamp(seconds: number): string {
  const whole = Math.max(0, Math.floor(seconds));
  const mins = Math.floor(whole / 60);
  const secs = whole % 60;
  return `${mins}:${secs.toString().padStart(2, "0")}`;
}

export function TutorialPage() {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const [chapters, setChapters] = useState<TutorialChapter[]>(TUTORIAL_CHAPTERS);
  const [activeChapterIdx, setActiveChapterIdx] = useState<number>(0);
  const [activeNodeIdx, setActiveNodeIdx] = useState<number>(0);
  const [activeFlowId, setActiveFlowId] = useState<string>(PACKET_FLOW_PRESETS[0].id);
  const [activeFlowHopIdx, setActiveFlowHopIdx] = useState<number>(0);
  const [autoAdvance, setAutoAdvance] = useState<boolean>(false);
  const [narrationEnabled, setNarrationEnabled] = useState<boolean>(false);
  const [speechStatus, setSpeechStatus] = useState<string>("Ready");
  const [gapFilter, setGapFilter] = useState<"ALL" | "P0" | "P1" | "P2" | "P3">("ALL");

  const baseUrl = import.meta.env.BASE_URL || "/";
  const videoSrc = `${baseUrl}tutorial/d2d-super-app-tutorial.mp4`;
  const vttSrc = `${baseUrl}tutorial/d2d-super-app-tutorial.vtt`;
  const chaptersJsonUrl = `${baseUrl}tutorial/tutorial-chapters.json`;

  const currentChapter = chapters[activeChapterIdx] ?? chapters[0];
  const currentNode = currentChapter.nodes[activeNodeIdx] ?? currentChapter.nodes[0];
  const currentFlow =
    PACKET_FLOW_PRESETS.find((f) => f.id === activeFlowId) ?? PACKET_FLOW_PRESETS[0];
  const currentFlowHop = currentFlow.hops[activeFlowHopIdx] ?? currentFlow.hops[0];

  // Load exact generated chapter timestamps from tutorial-chapters.json if available
  useEffect(() => {
    let cancelled = false;
    fetch(chaptersJsonUrl)
      .then((res) => (res.ok ? res.json() : null))
      .then(
        (
          data: {
            chapters?: Array<{
              number: number;
              startTime: number;
              endTime: number;
              duration: number;
            }>;
          } | null,
        ) => {
          if (cancelled || !data?.chapters?.length) return;
          const timingByNumber = new Map(
            data.chapters.map((c) => [
              c.number,
              { startTime: c.startTime, endTime: c.endTime, duration: c.duration },
            ]),
          );
          setChapters((prev) =>
            prev.map((ch) => {
              const timing = timingByNumber.get(ch.number);
              return timing ? { ...ch, ...timing } : ch;
            }),
          );
        },
      )
      .catch(() => {
        // Fallback to built-in chapter timestamps when offline or in unit tests
      });
    return () => {
      cancelled = true;
    };
  }, [chaptersJsonUrl]);

  // Auto-advance interactive animation timer
  useEffect(() => {
    if (!autoAdvance) return;
    const timer = window.setInterval(() => {
      setActiveNodeIdx((prevNode) => {
        if (prevNode < 3) {
          return prevNode + 1;
        }
        setActiveChapterIdx((prevCh) => (prevCh + 1) % chapters.length);
        return 0;
      });
      setActiveFlowHopIdx((prevHop) => (prevHop + 1) % 4);
    }, 2200);
    return () => window.clearInterval(timer);
  }, [autoAdvance, chapters.length]);

  // Web Speech API voiceover narration
  useEffect(() => {
    if (!narrationEnabled) {
      if (typeof window !== "undefined" && "speechSynthesis" in window) {
        window.speechSynthesis.cancel();
      }
      return;
    }
    if (typeof window === "undefined" || !("speechSynthesis" in window)) {
      setSpeechStatus("Web Speech API unavailable in this browser");
      return;
    }
    window.speechSynthesis.cancel();
    if (typeof SpeechSynthesisUtterance !== "undefined") {
      const utterance = new SpeechSynthesisUtterance(
        `${currentChapter.title}. ${currentChapter.narration}`,
      );
      utterance.rate = 1.02;
      utterance.onstart = () => setSpeechStatus(`Narrating Chapter ${currentChapter.number}`);
      utterance.onend = () => setSpeechStatus(`Finished Chapter ${currentChapter.number}`);
      utterance.onerror = () => setSpeechStatus("Narration stopped");
      window.speechSynthesis.speak(utterance);
      setSpeechStatus(`Narrating Chapter ${currentChapter.number}`);
    } else {
      setSpeechStatus(`Narrating Chapter ${currentChapter.number}`);
    }
    return () => {
      if (typeof window !== "undefined" && "speechSynthesis" in window) {
        window.speechSynthesis.cancel();
      }
    };
  }, [narrationEnabled, currentChapter]);

  function selectChapter(index: number, seekVideo = true) {
    const target = chapters[index];
    if (!target) return;
    setActiveChapterIdx(index);
    setActiveNodeIdx(0);
    if (seekVideo && videoRef.current) {
      try {
        videoRef.current.currentTime = target.startTime;
      } catch {
        // Ignore media seek errors in headless test environments
      }
    }
  }

  function toggleNarration() {
    setNarrationEnabled((prev) => {
      const next = !prev;
      if (!next) {
        setSpeechStatus("Narration muted");
      }
      return next;
    });
  }

  function stepPacketFlow() {
    setActiveNodeIdx((prev) => (prev + 1) % currentChapter.nodes.length);
    setActiveFlowHopIdx((prev) => (prev + 1) % currentFlow.hops.length);
  }

  const filteredGaps =
    gapFilter === "ALL"
      ? GAP_TRACKER_ITEMS
      : GAP_TRACKER_ITEMS.filter((item) => item.priority === gapFilter);

  return (
    <>
      <div className="head">
        <div>
          <h1>Interactive Product Tour & 1080p Video + Audio Tutorial Center</h1>
          <div className="sub">
            End-to-end 8-chapter video & spoken voiceover walkthrough, live animated packet-flow
            simulator, and 100% Super-App completion dashboard across Milestones M1–M10.
          </div>
        </div>
        <div className="act">
          <a className="btn" href={videoSrc} download="d2d-super-app-tutorial.mp4">
            Download 1080p MP4
          </a>
          <Link className="btn primary" to={currentChapter.moduleRoute}>
            {currentChapter.moduleLabel}
          </Link>
        </div>
      </div>

      {/* 1. SUPER-APP COMPLETION PROGRESS DASHBOARD */}
      <section
        className="card"
        aria-label="Super-App Completion Progress Dashboard"
        style={{ marginBottom: 16 }}
      >
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            flexWrap: "wrap",
            gap: 10,
            marginBottom: 12,
          }}
        >
          <div>
            <h3 style={{ margin: 0 }}>
              Super-App Completion Progress Dashboard (Vanta + Drata + VRINDA All-in-One)
            </h3>
            <div className="sub">
              All 18 Gap Tracker capabilities (#1–#18) and all M1–M10 engineering milestones are
              100% built, verified, and live.
            </div>
          </div>
          <div style={{ display: "flex", gap: 8, alignItems: "center", flexWrap: "wrap" }}>
            <Pill status="passing">100% Super-App Complete</Pill>
            <Pill status="passing">18 / 18 Gap Tracker Items Done</Pill>
            <Pill status="passing">M1–M10 Milestones Verified</Pill>
          </div>
        </div>

        {/* Visual 100% Master Progress Bar */}
        <div
          role="progressbar"
          aria-label="Super-App Overall Completion"
          aria-valuenow={100}
          aria-valuemin={0}
          aria-valuemax={100}
          style={{
            height: 16,
            borderRadius: 999,
            background: "var(--bg2)",
            overflow: "hidden",
            border: "1px solid var(--border)",
            marginBottom: 14,
          }}
        >
          <div
            style={{
              width: "100%",
              height: "100%",
              background: "linear-gradient(90deg, var(--accent), var(--ok))",
              transition: "width 0.4s ease",
            }}
          />
        </div>

        {/* M1-M10 Milestone Cards */}
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(260px, 1fr))",
            gap: 10,
            marginBottom: 16,
          }}
        >
          {SUPER_APP_MILESTONES.map((ms) => (
            <Link
              key={ms.code}
              to={ms.route}
              style={{
                display: "block",
                padding: 12,
                borderRadius: 10,
                border: "1px solid var(--border)",
                background: "var(--surface2)",
                textDecoration: "none",
                color: "inherit",
              }}
            >
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  marginBottom: 6,
                }}
              >
                <b>{ms.code}</b>
                <Pill status="passing">{`${ms.percent}% ${ms.status}`}</Pill>
              </div>
              <div className="sub" style={{ fontSize: 12 }}>
                {ms.title}
              </div>
            </Link>
          ))}
        </div>

        {/* 18 Gap Tracker Capabilities Filter & Grid */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            flexWrap: "wrap",
            gap: 8,
            marginBottom: 10,
          }}
        >
          <b>18 / 18 Competitor & Harborline Gap Tracker Capabilities</b>
          <div className="toggle" role="group" aria-label="Filter Gap Tracker Capabilities">
            {(["ALL", "P0", "P1", "P2", "P3"] as const).map((tier) => (
              <button
                key={tier}
                type="button"
                className={gapFilter === tier ? "on" : ""}
                onClick={() => setGapFilter(tier)}
              >
                {tier === "ALL" ? "All 18 Items" : tier}
              </button>
            ))}
          </div>
        </div>

        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(270px, 1fr))",
            gap: 8,
          }}
        >
          {filteredGaps.map((gap) => (
            <Link
              key={gap.id}
              to={gap.route}
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                gap: 8,
                padding: "8px 10px",
                borderRadius: 8,
                border: "1px solid var(--border)",
                textDecoration: "none",
                color: "inherit",
                fontSize: 12,
              }}
            >
              <span>
                <b>#{gap.id}</b> ({gap.priority}) · {gap.title}
              </span>
              <Pill status="passing">{gap.status}</Pill>
            </Link>
          ))}
        </div>
      </section>

      {/* 2. EMBEDDED 1080p VIDEO + SPOKEN AUDIO PLAYER WITH CHAPTER SEEKING */}
      <section
        className="card"
        aria-label="Embedded 1080p Video and Spoken Audio Player"
        style={{ marginBottom: 16 }}
      >
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            flexWrap: "wrap",
            gap: 10,
            marginBottom: 12,
          }}
        >
          <div>
            <h3 style={{ margin: 0 }}>1080p MP4 Video + Spoken Audio Walkthrough (8 Chapters)</h3>
            <div className="sub">
              Click any chapter jump button below to seek the H.264 + AAC video player directly to
              that chapter&apos;s exact timestamp and synchronize the interactive simulator.
            </div>
          </div>
          <Pill status="info">{`Active: Chapter ${currentChapter.number} (${formatTimestamp(currentChapter.startTime)})`}</Pill>
        </div>

        <div
          style={{
            display: "grid",
            gridTemplateColumns: "minmax(0, 1.55fr) minmax(280px, 1fr)",
            gap: 16,
            alignItems: "start",
          }}
        >
          <div>
            <video
              ref={videoRef}
              controls
              preload="metadata"
              src={videoSrc}
              aria-label="D2D Audit Services Super App 1080p Video Tutorial"
              style={{
                width: "100%",
                borderRadius: 12,
                border: "1px solid var(--border)",
                background: "#0b0e1a",
                aspectRatio: "16 / 9",
              }}
            >
              <track kind="captions" src={vttSrc} srcLang="en" label="English Captions" default />
            </video>

            <div
              style={{
                marginTop: 10,
                padding: 12,
                borderRadius: 10,
                background: "var(--surface2)",
                border: "1px solid var(--border)",
              }}
            >
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  flexWrap: "wrap",
                  gap: 8,
                  marginBottom: 6,
                }}
              >
                <b>{currentChapter.title}</b>
                <span className="kbd">
                  {formatTimestamp(currentChapter.startTime)} –{" "}
                  {formatTimestamp(currentChapter.endTime)}
                </span>
              </div>
              <p className="sub" style={{ margin: 0, lineHeight: 1.5 }}>
                {currentChapter.narration}
              </p>
            </div>
          </div>

          {/* Chapter Jump List */}
          <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
            <b>Jump to Video Chapter Timestamp</b>
            {chapters.map((ch, idx) => {
              const isSelected = idx === activeChapterIdx;
              return (
                <button
                  key={ch.id}
                  type="button"
                  onClick={() => selectChapter(idx, true)}
                  aria-pressed={isSelected}
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    alignItems: "center",
                    gap: 10,
                    padding: "10px 12px",
                    borderRadius: 10,
                    border: isSelected ? "2px solid var(--accent)" : "1px solid var(--border)",
                    background: isSelected ? "var(--accentSoft)" : "var(--surface2)",
                    textAlign: "start",
                    fontWeight: isSelected ? 700 : 500,
                  }}
                >
                  <span>
                    Ch {ch.number}: {ch.shortTitle}
                  </span>
                  <span className="kbd">{formatTimestamp(ch.startTime)}</span>
                </button>
              );
            })}
          </div>
        </div>
      </section>

      {/* 3. INTERACTIVE ANIMATED ARCHITECTURE & WORKFLOW SIMULATOR */}
      <section
        className="card"
        aria-label="Interactive Animated Architecture and Workflow Simulator"
        style={{ marginBottom: 16 }}
      >
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            flexWrap: "wrap",
            gap: 10,
            marginBottom: 14,
          }}
        >
          <div>
            <h3 style={{ margin: 0 }}>Interactive Animated Architecture & Packet-Flow Simulator</h3>
            <div className="sub">
              Step through all 8 chapters, toggle auto-advance animations, enable live Web Speech
              API voiceover narration, and inspect live cryptographic & attack-path packet flows.
            </div>
          </div>

          <div style={{ display: "flex", gap: 8, flexWrap: "wrap", alignItems: "center" }}>
            <button
              type="button"
              className="btn"
              onClick={() =>
                selectChapter((activeChapterIdx - 1 + chapters.length) % chapters.length, true)
              }
            >
              Previous Chapter
            </button>
            <button
              type="button"
              className="btn"
              onClick={() => selectChapter((activeChapterIdx + 1) % chapters.length, true)}
            >
              Next Chapter
            </button>
            <button
              type="button"
              className={`btn ${autoAdvance ? "primary" : ""}`}
              aria-pressed={autoAdvance}
              onClick={() => setAutoAdvance((prev) => !prev)}
            >
              {autoAdvance ? "Pause Auto-Advance" : "Play Auto-Advance"}
            </button>
            <button
              type="button"
              className={`btn ${narrationEnabled ? "primary" : ""}`}
              aria-pressed={narrationEnabled}
              onClick={toggleNarration}
            >
              {narrationEnabled ? "Stop Voiceover Narration" : "Start Voiceover Narration"}
            </button>
            <button type="button" className="btn" onClick={stepPacketFlow}>
              Step Packet Flow
            </button>
          </div>
        </div>

        {/* Status Bar */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            flexWrap: "wrap",
            gap: 8,
            padding: "8px 12px",
            borderRadius: 10,
            background: "var(--surface2)",
            border: "1px solid var(--border)",
            marginBottom: 14,
          }}
        >
          <span>
            <b>Active Simulation:</b> {currentChapter.shortTitle} · Stage {activeNodeIdx + 1} of{" "}
            {currentChapter.nodes.length} ({currentNode.stage})
          </span>
          <span>
            <b>Voiceover Status:</b> {speechStatus} · <b>Badge:</b> {currentChapter.badge}
          </span>
        </div>

        {/* Animated SVG Pipeline Diagram for the Active Chapter */}
        <div
          style={{
            padding: 14,
            borderRadius: 12,
            background: "var(--surface2)",
            border: "1px solid var(--border)",
            marginBottom: 14,
          }}
        >
          <svg
            viewBox="0 0 960 150"
            role="img"
            aria-label={`Animated architecture pipeline for ${currentChapter.shortTitle}`}
            style={{ width: "100%", height: "auto", display: "block" }}
          >
            <defs>
              <linearGradient id="d2dPipeGrad" x1="0%" y1="0%" x2="100%" y2="0%">
                <stop offset="0%" stopColor="#6366f1" />
                <stop offset="50%" stopColor="#10b981" />
                <stop offset="100%" stopColor="#f59e0b" />
              </linearGradient>
            </defs>

            {/* Base connector line */}
            <line x1="120" y1="75" x2="840" y2="75" stroke="var(--border)" strokeWidth="6" />
            {/* Active progress line */}
            <line
              x1="120"
              y1="75"
              x2={120 + activeNodeIdx * 240}
              y2="75"
              stroke="url(#d2dPipeGrad)"
              strokeWidth="6"
            />

            {currentChapter.nodes.map((node, idx) => {
              const cx = 120 + idx * 240;
              const isCurrent = idx === activeNodeIdx;
              const isReached = idx <= activeNodeIdx;
              return (
                <g
                  key={node.stage}
                  onClick={() => setActiveNodeIdx(idx)}
                  style={{ cursor: "pointer" }}
                >
                  <rect
                    x={cx - 95}
                    y={24}
                    width={190}
                    height={102}
                    rx={14}
                    fill={isCurrent ? "var(--accentSoft)" : "var(--surface)"}
                    stroke={isCurrent ? "var(--accent)" : isReached ? "var(--ok)" : "var(--border)"}
                    strokeWidth={isCurrent ? 3 : 2}
                  />
                  <text
                    x={cx}
                    y={52}
                    textAnchor="middle"
                    fill="var(--muted)"
                    fontSize="11"
                    fontWeight="700"
                  >
                    {`STAGE 0${idx + 1} ${isCurrent ? "● ACTIVE" : isReached ? "✓" : ""}`}
                  </text>
                  <text
                    x={cx}
                    y={78}
                    textAnchor="middle"
                    fill="var(--text)"
                    fontSize="13"
                    fontWeight="700"
                  >
                    {node.stage}
                  </text>
                  <text x={cx} y={102} textAnchor="middle" fill="var(--muted)" fontSize="10.5">
                    {node.detail}
                  </text>
                </g>
              );
            })}

            {/* Glowing animated packet indicator */}
            <circle
              cx={120 + activeNodeIdx * 240}
              cy={24}
              r={9}
              fill="var(--ok)"
              stroke="#fff"
              strokeWidth="2"
            />
          </svg>

          {/* Clickable Stage Buttons + Inspected Stage Telemetry */}
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(210px, 1fr))",
              gap: 10,
              marginTop: 12,
            }}
          >
            {currentChapter.nodes.map((node, idx) => (
              <button
                key={node.stage}
                type="button"
                onClick={() => setActiveNodeIdx(idx)}
                aria-pressed={idx === activeNodeIdx}
                style={{
                  padding: 10,
                  borderRadius: 10,
                  border:
                    idx === activeNodeIdx ? "2px solid var(--accent)" : "1px solid var(--border)",
                  background: idx === activeNodeIdx ? "var(--accentSoft)" : "var(--surface)",
                  textAlign: "start",
                }}
              >
                <div style={{ fontSize: 11, color: "var(--muted)", fontWeight: 700 }}>
                  Stage {idx + 1} · {idx === activeNodeIdx ? "TRANSMITTING" : "INSPECT"}
                </div>
                <b style={{ display: "block", margin: "4px 0" }}>{node.stage}</b>
                <div className="sub" style={{ fontSize: 11.5 }}>
                  {node.detail}
                </div>
              </button>
            ))}
          </div>

          <div
            style={{
              marginTop: 12,
              padding: 12,
              borderRadius: 10,
              background: "var(--surface)",
              border: "1px solid var(--border)",
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(260px, 1fr))",
              gap: 12,
            }}
          >
            <div>
              <div className="sub" style={{ fontSize: 11 }}>
                Inspected Stage Wire Payload
              </div>
              <code style={{ fontFamily: "var(--mono)", fontSize: 12 }}>{currentNode.payload}</code>
            </div>
            <div>
              <div className="sub" style={{ fontSize: 11 }}>
                Security & Domain Invariant
              </div>
              <b style={{ fontSize: 12.5 }}>{currentNode.invariant}</b>
            </div>
            <div>
              <div className="sub" style={{ fontSize: 11 }}>
                Chapter Code / Cryptographic Badge
              </div>
              <code style={{ fontFamily: "var(--mono)", fontSize: 12 }}>
                {currentChapter.codeSnippet}
              </code>
            </div>
          </div>
        </div>

        {/* Live Animated Packet Flows Selector (Trust Provenance Flow & Blast-Radius Attack Path Flow) */}
        <div
          style={{
            padding: 14,
            borderRadius: 12,
            background: "var(--surface2)",
            border: "1px solid var(--border)",
          }}
        >
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              flexWrap: "wrap",
              gap: 10,
              marginBottom: 12,
            }}
          >
            <div>
              <b>Live End-to-End Packet Flow Inspector</b>
              <div className="sub">{currentFlow.subtitle}</div>
            </div>
            <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
              {PACKET_FLOW_PRESETS.map((preset) => (
                <button
                  key={preset.id}
                  type="button"
                  className={`btn ${preset.id === activeFlowId ? "primary" : ""}`}
                  aria-pressed={preset.id === activeFlowId}
                  onClick={() => {
                    setActiveFlowId(preset.id);
                    setActiveFlowHopIdx(0);
                  }}
                >
                  {preset.title}
                </button>
              ))}
            </div>
          </div>

          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(210px, 1fr))",
              gap: 10,
              marginBottom: 12,
            }}
          >
            {currentFlow.hops.map((hop, idx) => {
              const active = idx === activeFlowHopIdx;
              return (
                <button
                  key={hop.step}
                  type="button"
                  onClick={() => setActiveFlowHopIdx(idx)}
                  aria-pressed={active}
                  style={{
                    padding: 12,
                    borderRadius: 10,
                    border: active ? "2px solid var(--accent)" : "1px solid var(--border)",
                    background: active ? "var(--accentSoft)" : "var(--surface)",
                    textAlign: "start",
                  }}
                >
                  <div
                    style={{
                      display: "flex",
                      justifyContent: "space-between",
                      alignItems: "center",
                      marginBottom: 4,
                    }}
                  >
                    <b>{hop.step}</b>
                    <Pill status={active ? "passing" : "info"}>
                      {active ? "Active Packet" : `Hop ${idx + 1}`}
                    </Pill>
                  </div>
                  <div style={{ fontSize: 11.5, fontWeight: 600, marginBottom: 4 }}>
                    {hop.actor}
                  </div>
                  <div className="sub" style={{ fontSize: 11.5 }}>
                    {hop.protocol}
                  </div>
                </button>
              );
            })}
          </div>

          <div
            style={{
              padding: 12,
              borderRadius: 10,
              background: "var(--surface)",
              border: "1px solid var(--border)",
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              flexWrap: "wrap",
              gap: 12,
            }}
          >
            <div>
              <b>
                {currentFlowHop.step} — {currentFlowHop.actor}
              </b>
              <div className="sub" style={{ marginTop: 4 }}>
                {currentFlowHop.detail}
              </div>
              <div style={{ marginTop: 4, fontSize: 12 }}>
                <b>Verification Gate:</b> {currentFlowHop.verification}
              </div>
            </div>
            <Link className="btn primary" to={currentFlow.moduleRoute}>
              {currentFlow.moduleLabel}
            </Link>
          </div>
        </div>
      </section>

      {/* 4. "OPEN LIVE MODULE" DEEP-LINK LAUNCHPAD */}
      <section className="card" aria-label="Open Live Module Deep-Link Launchpad">
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            flexWrap: "wrap",
            gap: 10,
            marginBottom: 12,
          }}
        >
          <div>
            <h3 style={{ margin: 0 }}>Open Live Module — Interactive Super-App Deep Links</h3>
            <div className="sub">
              Jump directly from this tutorial into any flagship capability with live Harborline
              data.
            </div>
          </div>
          <div style={{ display: "flex", gap: 8 }}>
            <Link className="btn primary" to={currentChapter.moduleRoute}>
              {currentChapter.moduleLabel}
            </Link>
            <Link className="btn" to={currentChapter.secondaryRoute}>
              {currentChapter.secondaryLabel}
            </Link>
          </div>
        </div>

        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))",
            gap: 10,
          }}
        >
          {DEEP_LINK_MODULES.map((mod) => (
            <Link
              key={mod.route}
              to={mod.route}
              style={{
                display: "flex",
                flexDirection: "column",
                justifyContent: "space-between",
                gap: 8,
                padding: 12,
                borderRadius: 10,
                border: "1px solid var(--border)",
                background: "var(--surface2)",
                textDecoration: "none",
                color: "inherit",
              }}
            >
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  gap: 6,
                }}
              >
                <b>{mod.label}</b>
                <Pill status="info">{mod.badge}</Pill>
              </div>
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  fontSize: 12,
                  color: "var(--accent)",
                  fontWeight: 600,
                }}
              >
                <span>Open Live Module</span>
                <span className="kbd">{mod.route}</span>
              </div>
            </Link>
          ))}
        </div>
      </section>
    </>
  );
}
