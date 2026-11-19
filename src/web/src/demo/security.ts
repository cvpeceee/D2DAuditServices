import { company, type DemoWorld } from "./catalog";

type Actor = { email: string; name: string; role: string };

export type DemoExposure = {
  id: string;
  provider: string;
  resourceName: string;
  resourceKind: string;
  region: string;
  cidr: string;
  port: number;
  protocol: string;
  service: string;
  severity: string;
  riskScore: number;
  controlCode: string;
  summary: string;
  remediationTerraform: string;
  remediationCli: string;
  status: string;
  resolutionNote: string;
  detectedAt: string;
  resolvedAt: string | null;
};

export type DemoScanFinding = {
  id: string;
  kind: "ContainerCve" | "License" | "Secret";
  severity: string;
  assetName: string;
  location: string;
  lineNumber: number;
  ruleId: string;
  title: string;
  detail: string;
  snippet: string;
  source: string;
  fixVersion: string;
  controlCode: string;
  status: string;
  detectedAt: string;
  resolvedAt: string | null;
};

export type DemoDataStore = {
  id: string;
  storeName: string;
  provider: string;
  engine: string;
  region: string;
  classification: string;
  sensitiveDataTypes: string;
  recordCountEstimate: number;
  encrypted: boolean;
  publicAccess: boolean;
  retentionCompliant: boolean;
  controlCode: string;
  riskScore: number;
  riskLevel: string;
  ownerEmail: string;
  lastScannedAt: string;
};

export type DemoCostRec = {
  id: string;
  provider: string;
  resourceName: string;
  resourceType: string;
  region: string;
  category: string;
  monthlySavingsUsd: number;
  annualSavingsUsd: number;
  linkedControlCode: string;
  complianceImpact: string;
  remediationSummary: string;
  status: string;
  detectedAt: string;
  actionedAt: string | null;
};

type SecurityState = {
  exposures: DemoExposure[];
  scans: DemoScanFinding[];
  stores: DemoDataStore[];
  costs: DemoCostRec[];
  seq: number;
};

const stateByWorld = new WeakMap<DemoWorld, SecurityState>();

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function canWrite(actor: Actor) {
  return actor.role === "Admin" || actor.role === "ControlOwner" || actor.role === "Editor";
}

function isoDaysAgo(days: number) {
  return new Date(Date.now() - days * 86_400_000).toISOString();
}

function scoreDspm(
  classification: string,
  publicAccess: boolean,
  encrypted: boolean,
  dataTypes: string,
  retentionCompliant: boolean,
) {
  const upper = classification.toUpperCase();
  let score =
    upper === "RESTRICTED" ? 40 : upper === "CONFIDENTIAL" ? 28 : upper === "INTERNAL" ? 15 : 5;
  if (publicAccess) score += 35;
  if (!encrypted) score += 22;
  if (!retentionCompliant) score += 10;
  const tags = dataTypes.toUpperCase();
  if (
    tags.includes("SSN") ||
    tags.includes("PHI") ||
    tags.includes("PCI") ||
    tags.includes("CREDITCARD")
  ) {
    score += 12;
  } else if (tags.includes("PII") || tags.includes("EMAIL")) {
    score += 6;
  }
  return Math.max(0, Math.min(100, score));
}

function riskLevel(score: number) {
  if (score >= 80) return "Critical";
  if (score >= 60) return "High";
  if (score >= 35) return "Medium";
  return "Low";
}

function initState(): SecurityState {
  const domain = company.domain;
  const stores: DemoDataStore[] = [
    {
      id: "dspm-1",
      storeName: "harborline-staging-exports",
      provider: "aws",
      engine: "AWS S3",
      region: "us-east-1",
      classification: "Restricted",
      sensitiveDataTypes: "PII,Email,SSN",
      recordCountEstimate: 480_000,
      encrypted: false,
      publicAccess: true,
      retentionCompliant: false,
      controlCode: "CFG-01",
      riskScore: 100,
      riskLevel: "Critical",
      ownerEmail: `priya.nair@${domain}`,
      lastScannedAt: isoDaysAgo(0.2),
    },
    {
      id: "dspm-2",
      storeName: "harborline-warehouse-prod",
      provider: "aws",
      engine: "AWS RDS PostgreSQL",
      region: "us-east-1",
      classification: "Restricted",
      sensitiveDataTypes: "PII,PCI,Email,BillingId",
      recordCountEstimate: 4_200_000,
      encrypted: true,
      publicAccess: false,
      retentionCompliant: true,
      controlCode: "CR-02",
      riskScore: 52,
      riskLevel: "Medium",
      ownerEmail: `marcus.lee@${domain}`,
      lastScannedAt: isoDaysAgo(0.1),
    },
    {
      id: "dspm-3",
      storeName: "harborline-eu-customers",
      provider: "azure",
      engine: "Azure Blob Storage",
      region: "westeurope",
      classification: "Restricted",
      sensitiveDataTypes: "PII,GDPR,Email,Phone",
      recordCountEstimate: 1_150_000,
      encrypted: true,
      publicAccess: false,
      retentionCompliant: false,
      controlCode: "CR-02",
      riskScore: 56,
      riskLevel: "Medium",
      ownerEmail: `lena.cho@${domain}`,
      lastScannedAt: isoDaysAgo(0.25),
    },
    {
      id: "dspm-4",
      storeName: "harborline-analytics-lake",
      provider: "gcp",
      engine: "Google Cloud Storage",
      region: "us-central1",
      classification: "Confidential",
      sensitiveDataTypes: "PII,CustomerId,UsageTelemetry",
      recordCountEstimate: 18_500_000,
      encrypted: true,
      publicAccess: false,
      retentionCompliant: true,
      controlCode: "CR-02",
      riskScore: 34,
      riskLevel: "Low",
      ownerEmail: `priya.nair@${domain}`,
      lastScannedAt: isoDaysAgo(0.2),
    },
    {
      id: "dspm-5",
      storeName: "harborline-audit-archive",
      provider: "aws",
      engine: "AWS S3 (Object Lock)",
      region: "us-east-1",
      classification: "Confidential",
      sensitiveDataTypes: "AuditLogs,EvidenceHashChain",
      recordCountEstimate: 920_000,
      encrypted: true,
      publicAccess: false,
      retentionCompliant: true,
      controlCode: "LG-03",
      riskScore: 28,
      riskLevel: "Low",
      ownerEmail: `chandra.vell@${domain}`,
      lastScannedAt: isoDaysAgo(0.1),
    },
  ];

  return {
    seq: 100,
    exposures: [
      {
        id: "exp-1",
        provider: "aws",
        resourceName: "sg-staging-analytics-mongo",
        resourceKind: "NetworkFirewall",
        region: "us-east-1",
        cidr: "0.0.0.0/0",
        port: 27017,
        protocol: "tcp",
        service: "MongoDB",
        severity: "Critical",
        riskScore: 98,
        controlCode: "CFG-01",
        summary:
          "Security group sg-staging-analytics-mongo exposes MongoDB port 27017 directly to 0.0.0.0/0.",
        remediationTerraform:
          'resource "aws_vpc_security_group_ingress_rule" "mongo_private" {\n  security_group_id = "sg-staging-analytics-mongo"\n  cidr_ipv4         = "10.20.0.0/16"\n  from_port         = 27017\n  to_port           = 27017\n  ip_protocol       = "tcp"\n}',
        remediationCli:
          "aws ec2 revoke-security-group-ingress --group-name sg-staging-analytics-mongo --protocol tcp --port 27017 --cidr 0.0.0.0/0",
        status: "Open",
        resolutionNote: "",
        detectedAt: isoDaysAgo(3),
        resolvedAt: null,
      },
      {
        id: "exp-2",
        provider: "aws",
        resourceName: "bastion-edge-sg",
        resourceKind: "NetworkFirewall",
        region: "us-east-1",
        cidr: "0.0.0.0/0",
        port: 22,
        protocol: "tcp",
        service: "SSH",
        severity: "Critical",
        riskScore: 92,
        controlCode: "CFG-01",
        summary:
          "Security group bastion-edge-sg allows SSH port 22 from the public internet instead of SSM Session Manager.",
        remediationTerraform:
          'resource "aws_vpc_security_group_ingress_rule" "bastion_corp" {\n  security_group_id = "bastion-edge-sg"\n  cidr_ipv4         = "198.51.100.0/24"\n  from_port         = 22\n  to_port           = 22\n  ip_protocol       = "tcp"\n}',
        remediationCli:
          "aws ec2 revoke-security-group-ingress --group-name bastion-edge-sg --protocol tcp --port 22 --cidr 0.0.0.0/0",
        status: "Open",
        resolutionNote: "",
        detectedAt: isoDaysAgo(6),
        resolvedAt: null,
      },
      {
        id: "exp-3",
        provider: "azure",
        resourceName: "harborline-eu-nsg",
        resourceKind: "NetworkFirewall",
        region: "westeurope",
        cidr: "0.0.0.0/0",
        port: 3389,
        protocol: "tcp",
        service: "RDP",
        severity: "Critical",
        riskScore: 94,
        controlCode: "CFG-01",
        summary: "Azure NSG harborline-eu-nsg permits inbound RDP 3389 from Internet.",
        remediationTerraform:
          'resource "azurerm_network_security_rule" "deny_rdp_internet" {\n  name                        = "deny-rdp-internet"\n  priority                    = 110\n  direction                   = "Inbound"\n  access                      = "Deny"\n  protocol                    = "Tcp"\n  source_address_prefix       = "Internet"\n  destination_port_range      = "3389"\n  network_security_group_name = "harborline-eu-nsg"\n}',
        remediationCli:
          "az network nsg rule delete --nsg-name harborline-eu-nsg --name AllowRDPInbound",
        status: "Open",
        resolutionNote: "",
        detectedAt: isoDaysAgo(4),
        resolvedAt: null,
      },
      {
        id: "exp-4",
        provider: "aws",
        resourceName: "harborline-staging-exports",
        resourceKind: "StorageBucket",
        region: "us-east-1",
        cidr: "0.0.0.0/0",
        port: 443,
        protocol: "https",
        service: "Public Object Storage",
        severity: "Critical",
        riskScore: 91,
        controlCode: "CFG-01",
        summary: "S3 bucket harborline-staging-exports has Block Public Access disabled.",
        remediationTerraform:
          'resource "aws_s3_bucket_public_access_block" "harborline_staging_exports" {\n  bucket                  = "harborline-staging-exports"\n  block_public_acls       = true\n  block_public_policy     = true\n  ignore_public_acls      = true\n  restrict_public_buckets = true\n}',
        remediationCli:
          "aws s3api put-public-access-block --bucket harborline-staging-exports --public-access-block-configuration BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true",
        status: "Open",
        resolutionNote: "",
        detectedAt: isoDaysAgo(5),
        resolvedAt: null,
      },
      {
        id: "exp-5",
        provider: "gcp",
        resourceName: "warehouse-preview-fw",
        resourceKind: "NetworkFirewall",
        region: "us-central1",
        cidr: "0.0.0.0/0",
        port: 5432,
        protocol: "tcp",
        service: "PostgreSQL",
        severity: "High",
        riskScore: 88,
        controlCode: "CFG-01",
        summary:
          "GCP firewall rule warehouse-preview-fw allowed PostgreSQL 5432 externally before Cloud SQL Auth Proxy cutover.",
        remediationTerraform:
          'resource "google_compute_firewall" "warehouse_preview_fw" {\n  name          = "warehouse-preview-fw"\n  source_ranges = ["10.128.0.0/9"]\n  allow { protocol = "tcp" ports = ["5432"] }\n}',
        remediationCli:
          "gcloud compute firewall-rules update warehouse-preview-fw --source-ranges=10.128.0.0/9",
        status: "Resolved",
        resolutionNote:
          "Migrated preview workloads to Cloud SQL Auth Proxy over Private Service Connect.",
        detectedAt: isoDaysAgo(18),
        resolvedAt: isoDaysAgo(9),
      },
    ],
    scans: [
      {
        id: "scan-1",
        kind: "ContainerCve",
        severity: "Critical",
        assetName: "ecr/harborline-worker:2.14.0",
        location: "usr/lib/x86_64-linux-gnu/libssl.so.3 (layer sha256:9f41c0)",
        lineNumber: 14,
        ruleId: "CVE-2024-21626",
        title: "Container runtime file descriptor leak in worker base image",
        detail: "Debian 12 slim base layer bundles an outdated runc helper package.",
        snippet: "FROM debian:12.4-slim -> upgrade to debian:12.8-slim",
        source: "AWS ECR Inspector",
        fixVersion: "runc 1.1.14 / debian:12.8-slim",
        controlCode: "VM-01",
        status: "Open",
        detectedAt: isoDaysAgo(3),
        resolvedAt: null,
      },
      {
        id: "scan-2",
        kind: "ContainerCve",
        severity: "High",
        assetName: "ecr/harborline-warehouse-api:4.8.2",
        location: "app/deps/libcurl.so.4 (layer sha256:4a21b8)",
        lineNumber: 29,
        ruleId: "CVE-2023-38545",
        title: "Heap buffer overflow in container SOCKS5 proxy client library",
        detail: "Warehouse ETL connector container image ships curl 8.3.0.",
        snippet: "Package curl@8.3.0 installed; patched in 8.4.0+",
        source: "Trivy Container Scan",
        fixVersion: "curl 8.10.1",
        controlCode: "VM-01",
        status: "Open",
        detectedAt: isoDaysAgo(7),
        resolvedAt: null,
      },
      {
        id: "scan-3",
        kind: "License",
        severity: "High",
        assetName: "ecr/harborline-analytics-ci:1.9.0",
        location: "vendor/pdf-renderer/LICENSE",
        lineNumber: 1,
        ruleId: "OSS-LICENSE-AGPL-3.0",
        title: "Restricted copyleft license AGPL-3.0 detected in commercial container image",
        detail:
          "AGPL-3.0 requires network-accessible derivative source disclosure unless a commercial license is recorded.",
        snippet: "GNU AFFERO GENERAL PUBLIC LICENSE Version 3",
        source: "Syft / Trivy License",
        fixVersion: "Replace with Apache-2.0 or MIT renderer",
        controlCode: "CM-11",
        status: "Open",
        detectedAt: isoDaysAgo(11),
        resolvedAt: null,
      },
      {
        id: "scan-4",
        kind: "License",
        severity: "Medium",
        assetName: "harborline-warehouse-api",
        location: "packages/csv-stream/package.json",
        lineNumber: 8,
        ruleId: "OSS-LICENSE-GPL-3.0",
        title: "GPL-3.0 transitive dependency detected in API bundle",
        detail: "Transitive CLI helper declares GPL-3.0-only license.",
        snippet: '"license": "GPL-3.0-only"',
        source: "SBOM License Audit",
        fixVersion: "Move to devDependencies or replace package",
        controlCode: "CM-11",
        status: "Open",
        detectedAt: isoDaysAgo(9),
        resolvedAt: null,
      },
      {
        id: "scan-5",
        kind: "Secret",
        severity: "Critical",
        assetName: "ecr/harborline-worker:2.14.0",
        location: "app/config/staging-etl.env",
        lineNumber: 42,
        ruleId: "SECRET-CLOUD-ACCESS-KEY",
        title: "Baked-in cloud access credential found in container image layer",
        detail:
          "An unrotated staging ingestion key was copied into the Docker build context via COPY . .",
        snippet: "CLOUD_INGEST_KEY=EXAMPLE_REDACTED_CREDENTIAL_01",
        source: "Container Secret Scanner",
        fixVersion: "Rotate credential and add .env to .dockerignore",
        controlCode: "CR-02",
        status: "Open",
        detectedAt: isoDaysAgo(2),
        resolvedAt: null,
      },
      {
        id: "scan-6",
        kind: "Secret",
        severity: "High",
        assetName: "harborline-warehouse-api",
        location: "scripts/seed-warehouse.sh",
        lineNumber: 19,
        ruleId: "SECRET-DB-CONNECTION-URI",
        title: "Hardcoded PostgreSQL password literal in helper script",
        detail:
          "Staging database URI contains an embedded credential instead of reading AWS Secrets Manager.",
        snippet: "DATABASE_URL=postgres://etl_svc:[REDACTED]@warehouse-stg.internal:5432/analytics",
        source: "SARIF Secret Scan",
        fixVersion: "Read secret from IAM / Secrets Manager at runtime",
        controlCode: "CR-02",
        status: "Open",
        detectedAt: isoDaysAgo(5),
        resolvedAt: null,
      },
    ],
    stores,
    costs: [
      {
        id: "cost-1",
        provider: "aws",
        resourceName: "harborline-staging-exports",
        resourceType: "S3 Bucket",
        region: "us-east-1",
        category: "LifecyclePolicy",
        monthlySavingsUsd: 640,
        annualSavingsUsd: 7680,
        linkedControlCode: "CR-02",
        complianceImpact:
          "Missing lifecycle expiration retains stale PII exports beyond 90-day GDPR Art.5 limit.",
        remediationSummary:
          "Attach 30-day lifecycle expiration and Glacier/delete rule to staging exports bucket.",
        status: "Open",
        detectedAt: isoDaysAgo(5),
        actionedAt: null,
      },
      {
        id: "cost-2",
        provider: "aws",
        resourceName: "vol-0a89f31d22c14e990",
        resourceType: "Unattached EBS Volume (2 TB)",
        region: "us-east-1",
        category: "OrphanedStorage",
        monthlySavingsUsd: 215,
        annualSavingsUsd: 2580,
        linkedControlCode: "CR-02",
        complianceImpact:
          "Orphaned unencrypted EBS volume fails encryption-at-rest and data disposal controls.",
        remediationSummary:
          "Snapshot if needed with KMS encryption, then delete unattached volume.",
        status: "Open",
        detectedAt: isoDaysAgo(10),
        actionedAt: null,
      },
      {
        id: "cost-3",
        provider: "aws",
        resourceName: "bastion-edge-i-049b12",
        resourceType: "Idle EC2 m6i.xlarge",
        region: "us-east-1",
        category: "IdleCompute",
        monthlySavingsUsd: 185,
        annualSavingsUsd: 2220,
        linkedControlCode: "CFG-01",
        complianceImpact:
          "Legacy SSH bastion runs 24/7 with <3% CPU while widening internet port-22 exposure.",
        remediationSummary: "Terminate bastion and standardize on AWS SSM Session Manager.",
        status: "Open",
        detectedAt: isoDaysAgo(8),
        actionedAt: null,
      },
      {
        id: "cost-4",
        provider: "azure",
        resourceName: "harborline-eu-dev-pg",
        resourceType: "Overprovisioned PostgreSQL",
        region: "westeurope",
        category: "OverprovisionedDb",
        monthlySavingsUsd: 890,
        annualSavingsUsd: 10680,
        linkedControlCode: "BC-01",
        complianceImpact:
          "Dev cluster provisioned at 16 vCores with 7% peak load and unverified backup retention.",
        remediationSummary:
          "Right-size to 4 vCores Burstable tier and align 35-day backup retention.",
        status: "Open",
        detectedAt: isoDaysAgo(6),
        actionedAt: null,
      },
      {
        id: "cost-5",
        provider: "gcp",
        resourceName: "gke-preview-stale-ip",
        resourceType: "Unattached Static IP & Disks",
        region: "us-central1",
        category: "UnattachedIp",
        monthlySavingsUsd: 145,
        annualSavingsUsd: 1740,
        linkedControlCode: "CFG-01",
        complianceImpact: "Unattached public IP risks dangling DNS takeover and unnecessary spend.",
        remediationSummary: "Release static IP and prune 14 orphaned persistent disk snapshots.",
        status: "Open",
        detectedAt: isoDaysAgo(4),
        actionedAt: null,
      },
      {
        id: "cost-6",
        provider: "aws",
        resourceName: "nat-gw-unused-az-c",
        resourceType: "Idle NAT Gateway",
        region: "us-west-2",
        category: "IdleCompute",
        monthlySavingsUsd: 405,
        annualSavingsUsd: 4860,
        linkedControlCode: "",
        complianceImpact: "Unused secondary-region NAT gateway had zero egress traffic in 45 days.",
        remediationSummary: "Removed route table reference and deleted idle NAT gateway.",
        status: "Applied",
        detectedAt: isoDaysAgo(15),
        actionedAt: isoDaysAgo(12),
      },
    ],
  };
}

function getState(world: DemoWorld) {
  let state = stateByWorld.get(world);
  if (!state) {
    state = initState();
    stateByWorld.set(world, state);
  }
  return state;
}

function exposureBoard(state: SecurityState) {
  const open = state.exposures.filter((item) => item.status === "Open");
  return {
    openCount: open.length,
    criticalCount: open.filter((item) => item.severity === "Critical").length,
    highCount: open.filter((item) => item.severity === "High").length,
    publicBuckets: open.filter((item) => item.resourceKind === "StorageBucket").length,
    openPortsCount: open.filter(
      (item) => item.resourceKind === "NetworkFirewall" || item.resourceKind === "ManagedDatabase",
    ).length,
    findings: state.exposures,
  };
}

function scansBoard(state: SecurityState) {
  const containers = state.scans.filter((item) => item.kind === "ContainerCve");
  const licenses = state.scans.filter((item) => item.kind === "License");
  const secrets = state.scans.filter((item) => item.kind === "Secret");
  return {
    openContainers: containers.filter((item) => item.status === "Open").length,
    openLicenses: licenses.filter((item) => item.status === "Open").length,
    openSecrets: secrets.filter((item) => item.status === "Open").length,
    containers,
    licenses,
    secrets,
  };
}

function dspmBoard(state: SecurityState) {
  const sorted = [...state.stores].sort((a, b) => b.riskScore - a.riskScore);
  return {
    totalStores: sorted.length,
    restrictedStores: sorted.filter((item) => item.classification === "Restricted").length,
    publicSensitiveStores: sorted.filter(
      (item) =>
        item.publicAccess &&
        (item.classification === "Restricted" || item.classification === "Confidential"),
    ).length,
    unencryptedStores: sorted.filter((item) => !item.encrypted).length,
    stores: sorted,
  };
}

function finOpsBoard(state: SecurityState) {
  const open = state.costs.filter((item) => item.status === "Open");
  const applied = state.costs.filter((item) => item.status === "Applied");
  const compliance = open.filter((item) => item.linkedControlCode.length > 0);
  const openMonthlyUsd = open.reduce((sum, item) => sum + item.monthlySavingsUsd, 0);
  const realizedMonthlyUsd = applied.reduce((sum, item) => sum + item.monthlySavingsUsd, 0);
  const complianceLinkedMonthlyUsd = compliance.reduce(
    (sum, item) => sum + item.monthlySavingsUsd,
    0,
  );
  return {
    summary: {
      openMonthlyUsd,
      openAnnualUsd: openMonthlyUsd * 12,
      realizedMonthlyUsd,
      realizedAnnualUsd: realizedMonthlyUsd * 12,
      complianceLinkedCount: compliance.length,
      complianceLinkedMonthlyUsd,
    },
    recommendations: state.costs,
  };
}

function buildAttackPaths(world: DemoWorld, state: SecurityState) {
  const openExposures = state.exposures
    .filter((item) => item.status === "Open")
    .sort((a, b) => b.riskScore - a.riskScore);
  const openSecrets = state.scans.filter(
    (item) => item.status === "Open" && item.kind === "Secret",
  );
  const riskyPerson =
    world.people.find((person) => person.state === "Offboarding" || person.mfaEnrolled === false) ??
    world.people[0];
  const sensitiveStores = [...state.stores]
    .filter((item) => item.riskScore >= 50 || item.publicAccess || !item.encrypted)
    .sort((a, b) => b.riskScore - a.riskScore);

  const paths = openExposures.slice(0, 4).map((exposure, index) => {
    const secret = openSecrets[index % Math.max(1, openSecrets.length)];
    const store =
      sensitiveStores.find((item) => item.provider === exposure.provider) ?? sensitiveStores[0];
    const hops = [
      {
        stage: "Entry",
        kind: exposure.resourceKind,
        label: `${exposure.provider.toUpperCase()} ${exposure.resourceName} (${exposure.service}:${exposure.port})`,
        detail: `Reachable from ${exposure.cidr}. ${exposure.summary}`,
        href: "/exposure",
        severity: exposure.severity,
      },
    ];
    const controls = new Set<string>([exposure.controlCode]);
    let score = Math.min(50, Math.floor(exposure.riskScore / 2));

    if (secret) {
      hops.push({
        stage: "Credential",
        kind: "Baked-in Secret",
        label: `${secret.title} (${secret.location}:${secret.lineNumber})`,
        detail: `Exposed in ${secret.assetName} via ${secret.source}.`,
        href: "/vulnerabilities",
        severity: secret.severity,
      });
      controls.add(secret.controlCode);
      score += 22;
    }

    if (riskyPerson) {
      const isLeaver = riskyPerson.state === "Offboarding";
      hops.push({
        stage: "Identity",
        kind: isLeaver ? "Leaver Access" : "Missing MFA",
        label: `${riskyPerson.name} (${riskyPerson.email})`,
        detail: isLeaver
          ? "Offboarding account still has active access in connected providers."
          : "Active identity lacks MFA enforcement, enabling credential pivot.",
        href: "/people",
        severity: "High",
      });
      controls.add(isLeaver ? "HR-08" : "AC-07");
      score += 12;
    }

    if (store) {
      hops.push({
        stage: "Crown Jewel",
        kind: store.engine,
        label: `${store.storeName} (${store.classification})`,
        detail: `Contains ${store.sensitiveDataTypes} (~${store.recordCountEstimate.toLocaleString()} records) in ${store.region}.`,
        href: "/dspm",
        severity: store.riskLevel,
      });
      controls.add(store.controlCode);
      score += Math.min(25, Math.floor(store.riskScore / 4));
    }

    const blastRadiusScore = Math.max(10, Math.min(100, score));
    return {
      id: `AP-0${index + 1}`,
      title: store
        ? `Internet ${exposure.service} (${exposure.resourceName}) to ${store.storeName}`
        : `Internet ${exposure.service} (${exposure.resourceName}) lateral pivot`,
      blastRadiusScore,
      severity: riskLevel(blastRadiusScore),
      hops,
      controlCodes: [...controls].sort(),
      chokepointRemediation: `Close ${exposure.cidr} access on ${exposure.resourceName} (port ${exposure.port}) first — eliminates entry hop for this entire attack chain.`,
    };
  });

  return {
    totalPaths: paths.length,
    criticalPaths: paths.filter((item) => item.severity === "Critical").length,
    highestBlastRadius: paths.reduce((max, item) => Math.max(max, item.blastRadiusScore), 0),
    paths,
  };
}

function parseSarifDemo(
  sarifJson: string,
  assetName: string,
  state: SecurityState,
): DemoScanFinding[] {
  const parsed = JSON.parse(sarifJson) as {
    runs?: {
      tool?: { driver?: { name?: string } };
      results?: {
        ruleId?: string;
        level?: string;
        message?: { text?: string };
        locations?: {
          physicalLocation?: {
            artifactLocation?: { uri?: string };
            region?: { startLine?: number; snippet?: { text?: string } };
          };
        }[];
      }[];
    }[];
  };
  const added: DemoScanFinding[] = [];
  for (const run of parsed.runs ?? []) {
    const tool = run.tool?.driver?.name ?? "SARIF";
    for (const item of run.results ?? []) {
      const ruleId = item.ruleId ?? "SARIF-RULE";
      const message = item.message?.text ?? ruleId;
      const combined = `${ruleId} ${message} ${tool}`.toUpperCase();
      const kind: DemoScanFinding["kind"] =
        combined.includes("SECRET") || combined.includes("GITLEAKS") || combined.includes("TOKEN")
          ? "Secret"
          : combined.includes("LICENSE") || combined.includes("GPL")
            ? "License"
            : "ContainerCve";
      const physical = item.locations?.[0]?.physicalLocation;
      const rawSnippet = physical?.region?.snippet?.text ?? "";
      const redacted = rawSnippet.replace(/[A-Za-z0-9_-]{16,}/g, "[REDACTED]");
      added.push({
        id: `scan-${++state.seq}`,
        kind,
        severity:
          item.level === "error"
            ? kind === "Secret"
              ? "Critical"
              : "High"
            : item.level === "warning"
              ? "Medium"
              : "Low",
        assetName: assetName || "imported-workload",
        location: physical?.artifactLocation?.uri ?? "",
        lineNumber: physical?.region?.startLine ?? 0,
        ruleId,
        title: message,
        detail: message,
        snippet: redacted,
        source: tool,
        fixVersion: "",
        controlCode: kind === "Secret" ? "CR-02" : kind === "License" ? "CM-11" : "VM-01",
        status: "Open",
        detectedAt: new Date().toISOString(),
        resolvedAt: null,
      });
    }
  }
  return added;
}

export function handleCloudSecurity(
  method: string,
  path: string,
  body: Record<string, unknown>,
  world: DemoWorld,
  actor: Actor,
): Response | null {
  if (!path.startsWith("/api/v1/security/")) return null;
  const state = getState(world);

  if (path === "/api/v1/security/exposure" && method === "GET") {
    return json(exposureBoard(state));
  }
  if (path === "/api/v1/security/exposure/scan" && method === "POST") {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    return json(exposureBoard(state));
  }
  if (
    path.startsWith("/api/v1/security/exposure/") &&
    path.endsWith("/resolve") &&
    method === "POST"
  ) {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const id = path.slice("/api/v1/security/exposure/".length, -"/resolve".length);
    const note = String(body.note ?? "").trim();
    if (!note) return json({ code: "invalid_resolution" }, 400);
    const row = state.exposures.find((item) => item.id === id);
    if (!row) return json({ code: "not_found" }, 404);
    row.status = "Resolved";
    row.resolutionNote = note;
    row.resolvedAt = new Date().toISOString();
    return json(row);
  }

  if (path === "/api/v1/security/scans" && method === "GET") {
    return json(scansBoard(state));
  }
  if (path === "/api/v1/security/scans/sarif" && method === "POST") {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const sarifJson = String(body.sarifJson ?? "");
    const assetName = String(body.assetName ?? "").trim();
    try {
      const added = parseSarifDemo(sarifJson, assetName, state);
      if (added.length === 0) return json({ code: "invalid_sarif" }, 400);
      state.scans.unshift(...added);
      return json(added.length);
    } catch {
      return json({ code: "invalid_sarif" }, 400);
    }
  }
  if (
    path.startsWith("/api/v1/security/scans/") &&
    path.endsWith("/resolve") &&
    method === "POST"
  ) {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const id = path.slice("/api/v1/security/scans/".length, -"/resolve".length);
    const row = state.scans.find((item) => item.id === id);
    if (!row) return json({ code: "not_found" }, 404);
    row.status = "Resolved";
    row.resolvedAt = new Date().toISOString();
    return json(row);
  }

  if (path === "/api/v1/security/dspm" && method === "GET") {
    return json(dspmBoard(state));
  }
  if (path === "/api/v1/security/dspm/scan" && method === "POST") {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const now = new Date().toISOString();
    for (const store of state.stores) store.lastScannedAt = now;
    return json(dspmBoard(state));
  }
  if (path === "/api/v1/security/dspm" && method === "POST") {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const storeName = String(body.storeName ?? "").trim();
    const provider = String(body.provider ?? "aws")
      .trim()
      .toLowerCase();
    const engine = String(body.engine ?? "AWS S3").trim();
    const region = String(body.region ?? "us-east-1").trim();
    const classification = String(body.classification ?? "Confidential").trim();
    const sensitiveDataTypes = String(body.sensitiveDataTypes ?? "").trim();
    const recordCountEstimate = Number(body.recordCountEstimate ?? 0);
    const encrypted = Boolean(body.encrypted);
    const publicAccess = Boolean(body.publicAccess);
    const retentionCompliant = Boolean(body.retentionCompliant);
    const controlCode = String(body.controlCode ?? "CR-02").trim() || "CR-02";
    const ownerEmail = String(body.ownerEmail ?? "").trim();
    if (!storeName || !engine) return json({ code: "invalid_datastore" }, 400);
    const score = scoreDspm(
      classification,
      publicAccess,
      encrypted,
      sensitiveDataTypes,
      retentionCompliant,
    );
    const existing = state.stores.find(
      (item) => item.provider === provider && item.storeName === storeName,
    );
    const record: DemoDataStore = {
      id: existing?.id ?? `dspm-${++state.seq}`,
      storeName,
      provider,
      engine,
      region,
      classification,
      sensitiveDataTypes,
      recordCountEstimate,
      encrypted,
      publicAccess,
      retentionCompliant,
      controlCode,
      riskScore: score,
      riskLevel: riskLevel(score),
      ownerEmail,
      lastScannedAt: new Date().toISOString(),
    };
    if (existing) {
      Object.assign(existing, record);
    } else {
      state.stores.unshift(record);
    }
    return json(record);
  }

  if (path === "/api/v1/security/finops" && method === "GET") {
    return json(finOpsBoard(state));
  }
  if (
    path.startsWith("/api/v1/security/finops/") &&
    path.endsWith("/status") &&
    method === "POST"
  ) {
    if (!canWrite(actor)) return json({ code: "forbidden" }, 403);
    const id = path.slice("/api/v1/security/finops/".length, -"/status".length);
    const status = String(body.status ?? "");
    if (!["Open", "Applied", "Dismissed"].includes(status)) {
      return json({ code: "invalid_status" }, 400);
    }
    const row = state.costs.find((item) => item.id === id);
    if (!row) return json({ code: "not_found" }, 404);
    row.status = status;
    row.actionedAt = status === "Open" ? null : new Date().toISOString();
    return json(row);
  }

  if (path === "/api/v1/security/attack-paths" && method === "GET") {
    return json(buildAttackPaths(world, state));
  }

  return null;
}
