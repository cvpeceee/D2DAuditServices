terraform {
  required_version = ">= 1.6.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }
}

provider "aws" {
  region = var.primary_region

  default_tags {
    tags = {
      Application        = var.project_name
      Environment        = var.environment
      ManagedBy          = "Terraform"
      DataClassification = "Confidential-Audit-Evidence"
      PrimaryRegion      = var.primary_region
      EUResidencyEnabled = tostring(var.enable_eu_residency_replica)
    }
  }
}

provider "aws" {
  alias  = "eu_residency"
  region = "eu-central-1"

  default_tags {
    tags = {
      Application        = var.project_name
      Environment        = var.environment
      ManagedBy          = "Terraform"
      DataClassification = "EU-Sovereign-Audit-Evidence"
      ResidencyRegime    = "GDPR-DORA-EU-AI-Act"
    }
  }
}

data "aws_caller_identity" "current" {}
data "aws_partition" "current" {}

data "aws_availability_zones" "available" {
  state = "available"
}

locals {
  name_prefix = "${var.project_name}-${var.environment}"
  account_id  = data.aws_caller_identity.current.account_id
  partition   = data.aws_partition.current.partition
  azs         = slice(data.aws_availability_zones.available.names, 0, 2)

  is_primary_eu = var.primary_region == "eu-central-1"
  deploy_eu_replica = (
    var.enable_eu_residency_replica && !local.is_primary_eu
  )
}
