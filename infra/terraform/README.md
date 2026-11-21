# D2D Audit Services — Production AWS & EU Data-Residency Terraform (`infra/terraform`)

Production Terraform configuration for deploying **D2D Audit Services** on AWS ECS Fargate, Multi-AZ RDS PostgreSQL 16, and KMS-encrypted S3 Object Lock WORM storage with **US (`us-east-1`)** and **EU (`eu-central-1` Frankfurt)** data-residency support (closing Gap Tracker #17).

## Architecture Overview

| Module File | Resources Provisioned | Compliance Controls Enforced |
| --- | --- | --- |
| [`main.tf`](./main.tf) | Terraform `>= 1.6.0`, AWS provider (`var.primary_region`) + `aws.eu_residency` (`eu-central-1`), AZ discovery | Multi-region residency tagging (`GDPR-DORA-EU-AI-Act`) |
| [`variables.tf`](./variables.tf) | `environment`, `primary_region` (`us-east-1` / `eu-central-1`), `enable_eu_residency_replica`, `vpc_cidr`, `db_instance_class`, `evidence_retention_years` (`>= 3`, default `6` for HIPAA) | Input validation gates preventing < 3-year retention or unapproved regions |
| [`kms_eu_residency.tf`](./kms_eu_residency.tf) | Primary KMS keys (`alias/d2d-audit-<env>-evidence`, `alias/d2d-audit-<env>-rds`) + sovereign EU KMS key (`alias/d2d-audit-<env>-eu-residency-evidence` in `eu-central-1`) | Automatic annual key rotation (`enable_key_rotation = true`), 30-day deletion window |
| [`s3_object_lock.tf`](./s3_object_lock.tf) | Primary + optional `eu-central-1` S3 evidence buckets with `object_lock_enabled = true`, `COMPLIANCE`/`GOVERNANCE` retention rule (`years = var.evidence_retention_years`), KMS SSE, versioning, public access block, TLS-only policy | Immutable WORM evidence chain storage (`SOC 2 CC6.1`, `ISO 27001 A.8.15`, `HIPAA §164.316(b)(2)(i)`) |
| [`rds_postgres.tf`](./rds_postgres.tf) | Multi-AZ RDS PostgreSQL 16 (`engine_version = "16.4"`), KMS gp3 encryption, `rds.force_ssl = 1` parameter group, Secrets Manager secret, 35-day backups, SG locked strictly to ECS tasks | Zero public DB ingress, enforced TLS in transit, encrypted control-plane & tenant databases |
| [`ecs_fargate.tf`](./ecs_fargate.tf) | VPC, public/private subnets across 2 AZs, NAT GW, ALB with TLS 1.3 (`ELBSecurityPolicy-TLS13-1-2-2021-06`), ECS Fargate cluster (`containerInsights = enabled`), `d2d-api`, `d2d-web`, and `d2d-temporal-worker` services, KMS-encrypted CloudWatch logs, least-privilege IAM roles | Private-subnet compute isolation, `Storage__ObjectLock=true` runtime enforcement |
| [`outputs.tf`](./outputs.tf) | ALB DNS name, ECS cluster name, RDS endpoint, primary & EU residency S3 Object Lock bucket IDs and KMS key ARNs | Auditable deployment outputs |

## Usage

### 1. Deploy Primary US Control Plane with EU Data-Residency Vault (`us-east-1` + `eu-central-1`)

```bash
cd infra/terraform
terraform init
terraform validate
terraform plan \
  -var="environment=prod" \
  -var="primary_region=us-east-1" \
  -var="enable_eu_residency_replica=true" \
  -var="evidence_retention_years=6" \
  -var="db_master_password=$D2D_DB_MASTER_PASSWORD"
```

### 2. Deploy Sovereign EU-Only Control Plane (`eu-central-1` Frankfurt)

```bash
cd infra/terraform
terraform init
terraform plan \
  -var="environment=prod" \
  -var="primary_region=eu-central-1" \
  -var="enable_eu_residency_replica=false" \
  -var="evidence_retention_years=6" \
  -var="db_master_password=$D2D_DB_MASTER_PASSWORD"
```
