# Customer-Managed KMS Keys for Primary Region & EU Residency (eu-central-1)

data "aws_iam_policy_document" "kms_primary" {
  statement {
    sid       = "EnableAccountRootAdministration"
    effect    = "Allow"
    actions   = ["kms:*"]
    resources = ["*"]

    principals {
      type        = "AWS"
      identifiers = ["arn:${local.partition}:iam::${local.account_id}:root"]
    }
  }

  statement {
    sid    = "AllowCloudWatchLogsEncryption"
    effect = "Allow"
    actions = [
      "kms:Encrypt*",
      "kms:Decrypt*",
      "kms:ReEncrypt*",
      "kms:GenerateDataKey*",
      "kms:Describe*"
    ]
    resources = ["*"]

    principals {
      type        = "Service"
      identifiers = ["logs.${var.primary_region}.amazonaws.com"]
    }
  }
}

resource "aws_kms_key" "evidence" {
  description             = "${local.name_prefix} S3 Object Lock evidence & CloudWatch KMS key (${var.primary_region})"
  deletion_window_in_days = 30
  enable_key_rotation     = true
  multi_region            = false
  policy                  = data.aws_iam_policy_document.kms_primary.json
}

resource "aws_kms_alias" "evidence" {
  name          = "alias/${local.name_prefix}-evidence"
  target_key_id = aws_kms_key.evidence.key_id
}

resource "aws_kms_key" "rds" {
  description             = "${local.name_prefix} RDS PostgreSQL 16 & Secrets Manager encryption key (${var.primary_region})"
  deletion_window_in_days = 30
  enable_key_rotation     = true
  multi_region            = false
  policy                  = data.aws_iam_policy_document.kms_primary.json
}

resource "aws_kms_alias" "rds" {
  name          = "alias/${local.name_prefix}-rds"
  target_key_id = aws_kms_key.rds.key_id
}

# Sovereign EU Data-Residency KMS Key in eu-central-1 (GDPR / DORA / EU AI Act)
resource "aws_kms_key" "eu_residency_evidence" {
  count    = local.deploy_eu_replica ? 1 : 0
  provider = aws.eu_residency

  description             = "${local.name_prefix} EU Sovereign Evidence KMS Key (eu-central-1)"
  deletion_window_in_days = 30
  enable_key_rotation     = true
  multi_region            = false
}

resource "aws_kms_alias" "eu_residency_evidence" {
  count    = local.deploy_eu_replica ? 1 : 0
  provider = aws.eu_residency

  name          = "alias/${local.name_prefix}-eu-residency-evidence"
  target_key_id = aws_kms_key.eu_residency_evidence[0].key_id
}
