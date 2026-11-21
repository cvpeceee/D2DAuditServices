variable "project_name" {
  description = "Canonical application identifier prefix for D2D Audit Services resources."
  type        = string
  default     = "d2d-audit"
}

variable "environment" {
  description = "Deployment environment name (prod, staging)."
  type        = string
  default     = "prod"

  validation {
    condition     = contains(["prod", "staging"], var.environment)
    error_message = "environment must be either 'prod' or 'staging'."
  }
}

variable "primary_region" {
  description = "Primary AWS region for the control plane and default tenant residency (us-east-1 or eu-central-1)."
  type        = string
  default     = "us-east-1"

  validation {
    condition     = contains(["us-east-1", "eu-central-1"], var.primary_region)
    error_message = "primary_region must be either 'us-east-1' (US) or 'eu-central-1' (EU Frankfurt)."
  }
}

variable "enable_eu_residency_replica" {
  description = "When true, provisions sovereign EU KMS keys and an Object-Locked S3 evidence vault in eu-central-1 for EU tenant data residency (GDPR / DORA / EU AI Act)."
  type        = bool
  default     = true
}

variable "vpc_cidr" {
  description = "CIDR block for the D2D Audit VPC."
  type        = string
  default     = "10.42.0.0/16"
}

variable "db_instance_class" {
  description = "RDS PostgreSQL 16 instance class for the Multi-AZ control-plane and tenant database cluster."
  type        = string
  default     = "db.m6g.large"
}

variable "db_allocated_storage" {
  description = "Initial allocated storage in GiB for encrypted gp3 RDS PostgreSQL 16."
  type        = number
  default     = 100
}

variable "db_max_allocated_storage" {
  description = "Maximum autoscaled storage in GiB for RDS PostgreSQL 16."
  type        = number
  default     = 500
}

variable "db_master_username" {
  description = "Master username for the RDS PostgreSQL 16 cluster."
  type        = string
  default     = "d2d_admin"
}

variable "db_master_password" {
  description = "Master password for the RDS PostgreSQL 16 cluster (injected via CI/CD or TF_VAR_db_master_password)."
  type        = string
  sensitive   = true
  default     = "REPLACE_VIA_SECRET_MANAGER_AT_APPLY_TIME_32CHARS!"
}

variable "evidence_retention_years" {
  description = "Immutable S3 Object Lock retention period in years (>= 3 for SOC 2 / ISO 27001; set to 6 for HIPAA §164.316(b)(2)(i))."
  type        = number
  default     = 6

  validation {
    condition     = var.evidence_retention_years >= 3 && var.evidence_retention_years <= 10
    error_message = "evidence_retention_years must be at least 3 years (use 6 years for HIPAA compliance)."
  }
}

variable "evidence_object_lock_mode" {
  description = "S3 Object Lock retention mode: COMPLIANCE (immutable even by root) or GOVERNANCE."
  type        = string
  default     = "COMPLIANCE"

  validation {
    condition     = contains(["COMPLIANCE", "GOVERNANCE"], var.evidence_object_lock_mode)
    error_message = "evidence_object_lock_mode must be COMPLIANCE or GOVERNANCE."
  }
}

variable "api_container_image" {
  description = "Container image URI for the D2D Audit .NET 10 API service."
  type        = string
  default     = "ghcr.io/cvpeceee/d2d-audit-api:latest"
}

variable "web_container_image" {
  description = "Container image URI for the D2D Audit React/Nginx web service."
  type        = string
  default     = "ghcr.io/cvpeceee/d2d-audit-web:latest"
}

variable "temporal_address" {
  description = "Temporal cluster host:port for background collector workflows."
  type        = string
  default     = "temporal.d2d-audit.internal:7233"
}

variable "acm_certificate_arn" {
  description = "ACM TLS certificate ARN for the Application Load Balancer HTTPS listener."
  type        = string
  default     = "arn:aws:acm:us-east-1:000000000000:certificate/00000000-0000-0000-0000-000000000000"
}
