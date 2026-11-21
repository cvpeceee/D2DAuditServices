output "alb_dns_name" {
  description = "Public HTTPS DNS hostname of the D2D Audit Application Load Balancer."
  value       = aws_lb.main.dns_name
}

output "ecs_cluster_name" {
  description = "ECS Fargate cluster name hosting d2d-api, d2d-web, and d2d-temporal-worker."
  value       = aws_ecs_cluster.main.name
}

output "rds_control_plane_endpoint" {
  description = "Multi-AZ RDS PostgreSQL 16 control-plane endpoint."
  value       = aws_db_instance.control_plane.endpoint
}

output "evidence_s3_bucket_id" {
  description = "Primary S3 Object-Locked evidence bucket name."
  value       = aws_s3_bucket.evidence.id
}

output "evidence_s3_bucket_arn" {
  description = "Primary S3 Object-Locked evidence bucket ARN."
  value       = aws_s3_bucket.evidence.arn
}

output "evidence_kms_key_arn" {
  description = "Customer-managed KMS key ARN protecting primary evidence storage and audit logs."
  value       = aws_kms_key.evidence.arn
}

output "eu_residency_evidence_bucket_id" {
  description = "Sovereign EU data-residency S3 Object-Locked evidence bucket in eu-central-1 (when enabled)."
  value       = local.deploy_eu_replica ? aws_s3_bucket.eu_residency_evidence[0].id : (local.is_primary_eu ? aws_s3_bucket.evidence.id : null)
}

output "eu_residency_kms_key_arn" {
  description = "Sovereign EU data-residency KMS key ARN in eu-central-1 (when enabled)."
  value       = local.deploy_eu_replica ? aws_kms_key.eu_residency_evidence[0].arn : (local.is_primary_eu ? aws_kms_key.evidence.arn : null)
}
