using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.CloudSecurity;

internal static class CloudSecurityExample
{
    public static async Task ApplyAsync(TenantDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!db.ExposureFindings.Local.Any() && !await db.ExposureFindings.AnyAsync(cancellationToken))
        {
            AddExposures(db, now);
        }

        if (!db.SecurityScanFindings.Local.Any() && !await db.SecurityScanFindings.AnyAsync(cancellationToken))
        {
            AddScans(db, now);
        }

        if (!db.DataStoreAssets.Local.Any() && !await db.DataStoreAssets.AnyAsync(cancellationToken))
        {
            AddDataStores(db, now);
        }

        if (!db.CostRecommendations.Local.Any() && !await db.CostRecommendations.AnyAsync(cancellationToken))
        {
            AddCosts(db, now);
        }
    }

    private static void AddExposures(TenantDbContext db, DateTimeOffset now)
    {
        db.ExposureFindings.AddRange(
            new ExposureFinding
            {
                Id = Guid.NewGuid(),
                Provider = "aws",
                ResourceName = "sg-staging-analytics-mongo",
                ResourceKind = "NetworkFirewall",
                Region = "us-east-1",
                Cidr = "0.0.0.0/0",
                Port = 27017,
                Protocol = "tcp",
                Service = "MongoDB",
                Severity = "Critical",
                RiskScore = 98,
                ControlCode = "CFG-01",
                Summary = "Security group sg-staging-analytics-mongo exposes MongoDB port 27017 directly to 0.0.0.0/0.",
                RemediationTerraform = "resource \"aws_vpc_security_group_ingress_rule\" \"mongo_private\" {\n  security_group_id = \"sg-staging-analytics-mongo\"\n  cidr_ipv4         = \"10.20.0.0/16\"\n  from_port         = 27017\n  to_port           = 27017\n  ip_protocol       = \"tcp\"\n}",
                RemediationCli = "aws ec2 revoke-security-group-ingress --group-name sg-staging-analytics-mongo --protocol tcp --port 27017 --cidr 0.0.0.0/0",
                Status = "Open",
                DetectedAt = now.AddDays(-3),
            },
            new ExposureFinding
            {
                Id = Guid.NewGuid(),
                Provider = "aws",
                ResourceName = "bastion-edge-sg",
                ResourceKind = "NetworkFirewall",
                Region = "us-east-1",
                Cidr = "0.0.0.0/0",
                Port = 22,
                Protocol = "tcp",
                Service = "SSH",
                Severity = "Critical",
                RiskScore = 92,
                ControlCode = "CFG-01",
                Summary = "Security group bastion-edge-sg allows SSH port 22 from the public internet instead of SSM Session Manager.",
                RemediationTerraform = "# Replace SSH ingress with AWS Systems Manager Session Manager\nresource \"aws_vpc_security_group_ingress_rule\" \"bastion_corp\" {\n  security_group_id = \"bastion-edge-sg\"\n  cidr_ipv4         = \"198.51.100.0/24\"\n  from_port         = 22\n  to_port           = 22\n  ip_protocol       = \"tcp\"\n}",
                RemediationCli = "aws ec2 revoke-security-group-ingress --group-name bastion-edge-sg --protocol tcp --port 22 --cidr 0.0.0.0/0",
                Status = "Open",
                DetectedAt = now.AddDays(-6),
            },
            new ExposureFinding
            {
                Id = Guid.NewGuid(),
                Provider = "azure",
                ResourceName = "harborline-eu-nsg",
                ResourceKind = "NetworkFirewall",
                Region = "westeurope",
                Cidr = "0.0.0.0/0",
                Port = 3389,
                Protocol = "tcp",
                Service = "RDP",
                Severity = "Critical",
                RiskScore = 94,
                ControlCode = "CFG-01",
                Summary = "Azure NSG harborline-eu-nsg permits inbound RDP 3389 from Internet.",
                RemediationTerraform = "resource \"azurerm_network_security_rule\" \"deny_rdp_internet\" {\n  name                        = \"deny-rdp-internet\"\n  priority                    = 110\n  direction                   = \"Inbound\"\n  access                      = \"Deny\"\n  protocol                    = \"Tcp\"\n  source_address_prefix       = \"Internet\"\n  destination_port_range      = \"3389\"\n  network_security_group_name = \"harborline-eu-nsg\"\n}",
                RemediationCli = "az network nsg rule delete --nsg-name harborline-eu-nsg --name AllowRDPInbound",
                Status = "Open",
                DetectedAt = now.AddDays(-4),
            },
            new ExposureFinding
            {
                Id = Guid.NewGuid(),
                Provider = "aws",
                ResourceName = "harborline-staging-exports",
                ResourceKind = "StorageBucket",
                Region = "us-east-1",
                Cidr = "0.0.0.0/0",
                Port = 443,
                Protocol = "https",
                Service = "Public Object Storage",
                Severity = "Critical",
                RiskScore = 91,
                ControlCode = "CFG-01",
                Summary = "S3 bucket harborline-staging-exports has Block Public Access disabled.",
                RemediationTerraform = "resource \"aws_s3_bucket_public_access_block\" \"harborline_staging_exports\" {\n  bucket                  = \"harborline-staging-exports\"\n  block_public_acls       = true\n  block_public_policy     = true\n  ignore_public_acls      = true\n  restrict_public_buckets = true\n}",
                RemediationCli = "aws s3api put-public-access-block --bucket harborline-staging-exports --public-access-block-configuration BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true",
                Status = "Open",
                DetectedAt = now.AddDays(-5),
            },
            new ExposureFinding
            {
                Id = Guid.NewGuid(),
                Provider = "gcp",
                ResourceName = "warehouse-preview-fw",
                ResourceKind = "NetworkFirewall",
                Region = "us-central1",
                Cidr = "0.0.0.0/0",
                Port = 5432,
                Protocol = "tcp",
                Service = "PostgreSQL",
                Severity = "High",
                RiskScore = 88,
                ControlCode = "CFG-01",
                Summary = "GCP firewall rule warehouse-preview-fw allowed PostgreSQL 5432 externally before Cloud SQL Auth Proxy cutover.",
                RemediationTerraform = "resource \"google_compute_firewall\" \"warehouse_preview_fw\" {\n  name          = \"warehouse-preview-fw\"\n  source_ranges = [\"10.128.0.0/9\"]\n  allow { protocol = \"tcp\" ports = [\"5432\"] }\n}",
                RemediationCli = "gcloud compute firewall-rules update warehouse-preview-fw --source-ranges=10.128.0.0/9",
                Status = "Resolved",
                ResolutionNote = "Migrated preview workloads to Cloud SQL Auth Proxy over Private Service Connect.",
                DetectedAt = now.AddDays(-18),
                ResolvedAt = now.AddDays(-9),
            });
    }

    private static void AddScans(TenantDbContext db, DateTimeOffset now)
    {
        db.SecurityScanFindings.AddRange(
            new SecurityScanFinding
            {
                Id = Guid.NewGuid(),
                Kind = "ContainerCve",
                Severity = "Critical",
                AssetName = "ecr/harborline-worker:2.14.0",
                Location = "usr/lib/x86_64-linux-gnu/libssl.so.3 (layer sha256:9f41c0)",
                LineNumber = 14,
                RuleId = "CVE-2024-21626",
                Title = "Container runtime file descriptor leak in worker base image",
                Detail = "Debian 12 slim base layer bundles an outdated runc helper package.",
                Snippet = "FROM debian:12.4-slim -> upgrade to debian:12.8-slim",
                Source = "AWS ECR Inspector",
                FixVersion = "runc 1.1.14 / debian:12.8-slim",
                ControlCode = "VM-01",
                Status = "Open",
                DetectedAt = now.AddDays(-3),
            },
            new SecurityScanFinding
            {
                Id = Guid.NewGuid(),
                Kind = "ContainerCve",
                Severity = "High",
                AssetName = "ecr/harborline-warehouse-api:4.8.2",
                Location = "app/deps/libcurl.so.4 (layer sha256:4a21b8)",
                LineNumber = 29,
                RuleId = "CVE-2023-38545",
                Title = "Heap buffer overflow in container SOCKS5 proxy client library",
                Detail = "Warehouse ETL connector container image ships curl 8.3.0.",
                Snippet = "Package curl@8.3.0 installed; patched in 8.4.0+",
                Source = "Trivy Container Scan",
                FixVersion = "curl 8.10.1",
                ControlCode = "VM-01",
                Status = "Open",
                DetectedAt = now.AddDays(-7),
            },
            new SecurityScanFinding
            {
                Id = Guid.NewGuid(),
                Kind = "License",
                Severity = "High",
                AssetName = "ecr/harborline-analytics-ci:1.9.0",
                Location = "vendor/pdf-renderer/LICENSE",
                LineNumber = 1,
                RuleId = "OSS-LICENSE-AGPL-3.0",
                Title = "Restricted copyleft license AGPL-3.0 detected in commercial container image",
                Detail = "AGPL-3.0 requires network-accessible derivative source disclosure unless a commercial license is recorded.",
                Snippet = "GNU AFFERO GENERAL PUBLIC LICENSE Version 3",
                Source = "Syft / Trivy License",
                FixVersion = "Replace with Apache-2.0 or MIT renderer",
                ControlCode = "CM-11",
                Status = "Open",
                DetectedAt = now.AddDays(-11),
            },
            new SecurityScanFinding
            {
                Id = Guid.NewGuid(),
                Kind = "License",
                Severity = "Medium",
                AssetName = "harborline-warehouse-api",
                Location = "packages/csv-stream/package.json",
                LineNumber = 8,
                RuleId = "OSS-LICENSE-GPL-3.0",
                Title = "GPL-3.0 transitive dependency detected in API bundle",
                Detail = "Transitive CLI helper declares GPL-3.0-only license.",
                Snippet = "\"license\": \"GPL-3.0-only\"",
                Source = "SBOM License Audit",
                FixVersion = "Move to devDependencies or replace package",
                ControlCode = "CM-11",
                Status = "Open",
                DetectedAt = now.AddDays(-9),
            },
            new SecurityScanFinding
            {
                Id = Guid.NewGuid(),
                Kind = "Secret",
                Severity = "Critical",
                AssetName = "ecr/harborline-worker:2.14.0",
                Location = "app/config/staging-etl.env",
                LineNumber = 42,
                RuleId = "SECRET-CLOUD-ACCESS-KEY",
                Title = "Baked-in cloud access credential found in container image layer",
                Detail = "An unrotated staging ingestion key was copied into the Docker build context via COPY . .",
                Snippet = "CLOUD_INGEST_KEY=EXAMPLE_REDACTED_CREDENTIAL_01",
                Source = "Container Secret Scanner",
                FixVersion = "Rotate credential and add .env to .dockerignore",
                ControlCode = "CR-02",
                Status = "Open",
                DetectedAt = now.AddDays(-2),
            },
            new SecurityScanFinding
            {
                Id = Guid.NewGuid(),
                Kind = "Secret",
                Severity = "High",
                AssetName = "harborline-warehouse-api",
                Location = "scripts/seed-warehouse.sh",
                LineNumber = 19,
                RuleId = "SECRET-DB-CONNECTION-URI",
                Title = "Hardcoded PostgreSQL password literal in helper script",
                Detail = "Staging database URI contains an embedded credential instead of reading AWS Secrets Manager.",
                Snippet = "DATABASE_URL=postgres://etl_svc:[REDACTED]@warehouse-stg.internal:5432/analytics",
                Source = "SARIF Secret Scan",
                FixVersion = "Read secret from IAM / Secrets Manager at runtime",
                ControlCode = "CR-02",
                Status = "Open",
                DetectedAt = now.AddDays(-5),
            });
    }

    private static void AddDataStores(TenantDbContext db, DateTimeOffset now)
    {
        var domain = ClientSeed.Company.Domain;
        db.DataStoreAssets.AddRange(
            Store("harborline-staging-exports", "aws", "AWS S3", "us-east-1", "Restricted", "PII,Email,SSN", 480_000, false, true, false, "CFG-01", "priya.nair@" + domain, now.AddHours(-4)),
            Store("harborline-warehouse-prod", "aws", "AWS RDS PostgreSQL", "us-east-1", "Restricted", "PII,PCI,Email,BillingId", 4_200_000, true, false, true, "CR-02", "marcus.lee@" + domain, now.AddHours(-3)),
            Store("harborline-eu-customers", "azure", "Azure Blob Storage", "westeurope", "Restricted", "PII,GDPR,Email,Phone", 1_150_000, true, false, false, "CR-02", "lena.cho@" + domain, now.AddHours(-6)),
            Store("harborline-analytics-lake", "gcp", "Google Cloud Storage", "us-central1", "Confidential", "PII,CustomerId,UsageTelemetry", 18_500_000, true, false, true, "CR-02", "priya.nair@" + domain, now.AddHours(-5)),
            Store("harborline-audit-archive", "aws", "AWS S3 (Object Lock)", "us-east-1", "Confidential", "AuditLogs,EvidenceHashChain", 920_000, true, false, true, "LG-03", "chandra.vell@" + domain, now.AddHours(-2)));
    }

    private static DataStoreAsset Store(
        string name,
        string provider,
        string engine,
        string region,
        string classification,
        string dataTypes,
        long records,
        bool encrypted,
        bool publicAccess,
        bool retentionCompliant,
        string controlCode,
        string ownerEmail,
        DateTimeOffset scanned) => new()
    {
        Id = Guid.NewGuid(),
        StoreName = name,
        Provider = provider,
        Engine = engine,
        Region = region,
        Classification = classification,
        SensitiveDataTypes = dataTypes,
        RecordCountEstimate = records,
        Encrypted = encrypted,
        PublicAccess = publicAccess,
        RetentionCompliant = retentionCompliant,
        ControlCode = controlCode,
        RiskScore = DspmScorer.Score(classification, publicAccess, encrypted, dataTypes, retentionCompliant),
        OwnerEmail = ownerEmail,
        LastScannedAt = scanned,
    };

    private static void AddCosts(TenantDbContext db, DateTimeOffset now)
    {
        db.CostRecommendations.AddRange(
            Cost("aws", "harborline-staging-exports", "S3 Bucket", "us-east-1", "LifecyclePolicy", 640m, "CR-02", "Missing lifecycle expiration retains stale PII exports beyond 90-day GDPR Art.5 limit.", "Attach 30-day lifecycle expiration and Glacier/delete rule to staging exports bucket.", "Open", now.AddDays(-5)),
            Cost("aws", "vol-0a89f31d22c14e990", "Unattached EBS Volume (2 TB)", "us-east-1", "OrphanedStorage", 215m, "CR-02", "Orphaned unencrypted EBS volume fails encryption-at-rest and data disposal controls.", "Snapshot if needed with KMS encryption, then delete unattached volume.", "Open", now.AddDays(-10)),
            Cost("aws", "bastion-edge-i-049b12", "Idle EC2 m6i.xlarge", "us-east-1", "IdleCompute", 185m, "CFG-01", "Legacy SSH bastion runs 24/7 with <3% CPU while widening internet port-22 exposure.", "Terminate bastion and standardize on AWS SSM Session Manager.", "Open", now.AddDays(-8)),
            Cost("azure", "harborline-eu-dev-pg", "Overprovisioned PostgreSQL", "westeurope", "OverprovisionedDb", 890m, "BC-01", "Dev cluster provisioned at 16 vCores with 7% peak load and unverified backup retention.", "Right-size to 4 vCores Burstable tier and align 35-day backup retention.", "Open", now.AddDays(-6)),
            Cost("gcp", "gke-preview-stale-ip", "Unattached Static IP & Disks", "us-central1", "UnattachedIp", 145m, "CFG-01", "Unattached public IP risks dangling DNS takeover and unnecessary spend.", "Release static IP and prune 14 orphaned persistent disk snapshots.", "Open", now.AddDays(-4)),
            Cost("aws", "nat-gw-unused-az-c", "Idle NAT Gateway", "us-west-2", "IdleCompute", 405m, "", "Unused secondary-region NAT gateway had zero egress traffic in 45 days.", "Removed route table reference and deleted idle NAT gateway.", "Applied", now.AddDays(-15)));
    }

    private static CostRecommendation Cost(
        string provider,
        string name,
        string type,
        string region,
        string category,
        decimal monthly,
        string controlCode,
        string impact,
        string remediation,
        string status,
        DateTimeOffset detected) => new()
    {
        Id = Guid.NewGuid(),
        Provider = provider,
        ResourceName = name,
        ResourceType = type,
        Region = region,
        Category = category,
        MonthlySavingsUsd = monthly,
        AnnualSavingsUsd = monthly * 12m,
        LinkedControlCode = controlCode,
        ComplianceImpact = impact,
        RemediationSummary = remediation,
        Status = status,
        DetectedAt = detected,
        ActionedAt = status == "Applied" ? detected.AddDays(3) : null,
    };
}
