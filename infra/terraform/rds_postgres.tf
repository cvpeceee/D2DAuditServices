# Multi-AZ Encrypted RDS PostgreSQL 16 Control-Plane & Tenant Database Cluster

resource "aws_db_subnet_group" "postgres" {
  name        = "${local.name_prefix}-postgres-subnets"
  description = "Isolated private database subnets for D2D Audit RDS PostgreSQL 16"
  subnet_ids  = aws_subnet.private[*].id
}

resource "aws_db_parameter_group" "postgres16" {
  name        = "${local.name_prefix}-pg16-params"
  family      = "postgres16"
  description = "Hardened PostgreSQL 16 parameter group enforcing TLS and audit logging"

  parameter {
    name  = "rds.force_ssl"
    value = "1"
  }

  parameter {
    name  = "log_connections"
    value = "1"
  }

  parameter {
    name  = "log_disconnections"
    value = "1"
  }
}

resource "aws_security_group" "rds_postgres" {
  name        = "${local.name_prefix}-rds-pg-sg"
  description = "Allow PostgreSQL 5432 strictly from D2D Audit ECS Fargate tasks"
  vpc_id      = aws_vpc.main.id

  ingress {
    description     = "PostgreSQL 5432 from ECS Fargate tasks only"
    from_port       = 5432
    to_port         = 5432
    protocol        = "tcp"
    security_groups = [aws_security_group.ecs_tasks.id]
  }

  egress {
    description = "No outbound traffic required from RDS"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["127.0.0.1/32"]
  }
}

resource "aws_db_instance" "control_plane" {
  identifier     = "${local.name_prefix}-postgres16"
  engine         = "postgres"
  engine_version = "16.4"
  instance_class = var.db_instance_class

  allocated_storage     = var.db_allocated_storage
  max_allocated_storage = var.db_max_allocated_storage
  storage_type          = "gp3"
  storage_encrypted     = true
  kms_key_id            = aws_kms_key.rds.arn

  db_name  = "d2d_control"
  username = var.db_master_username
  password = var.db_master_password
  port     = 5432

  multi_az               = true
  publicly_accessible    = false
  db_subnet_group_name   = aws_db_subnet_group.postgres.name
  vpc_security_group_ids = [aws_security_group.rds_postgres.id]
  parameter_group_name   = aws_db_parameter_group.postgres16.name

  backup_retention_period         = 35
  backup_window                   = "03:00-04:00"
  maintenance_window              = "Sun:04:30-Sun:05:30"
  copy_tags_to_snapshot           = true
  deletion_protection             = true
  skip_final_snapshot             = false
  final_snapshot_identifier       = "${local.name_prefix}-postgres16-final"
  auto_minor_version_upgrade      = true
  performance_insights_enabled    = true
  performance_insights_kms_key_id = aws_kms_key.rds.arn
  enabled_cloudwatch_logs_exports = ["postgresql", "upgrade"]
}

resource "aws_secretsmanager_secret" "control_plane_db" {
  name                    = "${local.name_prefix}/control-plane-db-connection"
  description             = "Npgsql connection string for the D2D Audit PostgreSQL 16 control plane"
  kms_key_id              = aws_kms_key.rds.arn
  recovery_window_in_days = 30
}

resource "aws_secretsmanager_secret_version" "control_plane_db" {
  secret_id     = aws_secretsmanager_secret.control_plane_db.id
  secret_string = "Host=${aws_db_instance.control_plane.address};Port=5432;Database=d2d_control;Username=${var.db_master_username};Password=${var.db_master_password};SSL Mode=VerifyFull"
}
