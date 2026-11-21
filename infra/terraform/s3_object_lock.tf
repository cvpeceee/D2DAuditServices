# Immutable WORM S3 Evidence Storage with Object Lock & EU Residency Vault

resource "aws_s3_bucket" "evidence" {
  bucket              = "${local.name_prefix}-evidence-${local.account_id}-${var.primary_region}"
  object_lock_enabled = true
  force_destroy       = false
}

resource "aws_s3_bucket_versioning" "evidence" {
  bucket = aws_s3_bucket.evidence.id

  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "evidence" {
  bucket = aws_s3_bucket.evidence.id

  rule {
    apply_server_side_encryption_by_default {
      kms_master_key_id = aws_kms_key.evidence.arn
      sse_algorithm     = "aws:kms"
    }
    bucket_key_enabled = true
  }
}

resource "aws_s3_bucket_public_access_block" "evidence" {
  bucket = aws_s3_bucket.evidence.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_ownership_controls" "evidence" {
  bucket = aws_s3_bucket.evidence.id

  rule {
    object_ownership = "BucketOwnerEnforced"
  }
}

resource "aws_s3_bucket_object_lock_configuration" "evidence" {
  bucket = aws_s3_bucket.evidence.id

  rule {
    default_retention {
      mode  = var.evidence_object_lock_mode
      years = var.evidence_retention_years
    }
  }

  depends_on = [aws_s3_bucket_versioning.evidence]
}

data "aws_iam_policy_document" "evidence_tls_only" {
  statement {
    sid     = "DenyInsecureTransport"
    effect  = "Deny"
    actions = ["s3:*"]
    resources = [
      aws_s3_bucket.evidence.arn,
      "${aws_s3_bucket.evidence.arn}/*"
    ]

    principals {
      type        = "*"
      identifiers = ["*"]
    }

    condition {
      test     = "Bool"
      variable = "aws:SecureTransport"
      values   = ["false"]
    }
  }
}

resource "aws_s3_bucket_policy" "evidence" {
  bucket = aws_s3_bucket.evidence.id
  policy = data.aws_iam_policy_document.evidence_tls_only.json
}

# Optional EU Data-Residency S3 Evidence Bucket in eu-central-1 (Frankfurt)
resource "aws_s3_bucket" "eu_residency_evidence" {
  count    = local.deploy_eu_replica ? 1 : 0
  provider = aws.eu_residency

  bucket              = "${local.name_prefix}-evidence-${local.account_id}-eu-central-1"
  object_lock_enabled = true
  force_destroy       = false
}

resource "aws_s3_bucket_versioning" "eu_residency_evidence" {
  count    = local.deploy_eu_replica ? 1 : 0
  provider = aws.eu_residency

  bucket = aws_s3_bucket.eu_residency_evidence[0].id

  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "eu_residency_evidence" {
  count    = local.deploy_eu_replica ? 1 : 0
  provider = aws.eu_residency

  bucket = aws_s3_bucket.eu_residency_evidence[0].id

  rule {
    apply_server_side_encryption_by_default {
      kms_master_key_id = aws_kms_key.eu_residency_evidence[0].arn
      sse_algorithm     = "aws:kms"
    }
    bucket_key_enabled = true
  }
}

resource "aws_s3_bucket_public_access_block" "eu_residency_evidence" {
  count    = local.deploy_eu_replica ? 1 : 0
  provider = aws.eu_residency

  bucket = aws_s3_bucket.eu_residency_evidence[0].id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_object_lock_configuration" "eu_residency_evidence" {
  count    = local.deploy_eu_replica ? 1 : 0
  provider = aws.eu_residency

  bucket = aws_s3_bucket.eu_residency_evidence[0].id

  rule {
    default_retention {
      mode  = var.evidence_object_lock_mode
      years = var.evidence_retention_years
    }
  }

  depends_on = [aws_s3_bucket_versioning.eu_residency_evidence]
}
