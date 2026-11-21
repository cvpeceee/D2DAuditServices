using D2DAudit.Domain;
using Microsoft.EntityFrameworkCore;

namespace D2DAudit.Infrastructure.Persistence;

internal static class AgentAcademyModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        var token = modelBuilder.Entity<DeviceEnrollmentToken>();
        token.ToTable("device_enrollment_tokens");
        token.HasKey(x => x.Id);
        token.Property(x => x.PersonEmail).HasMaxLength(320).IsRequired();
        token.Property(x => x.PersonName).HasMaxLength(200).IsRequired();
        token.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        token.Property(x => x.Hint).HasMaxLength(16).IsRequired();
        token.Property(x => x.CreatedByEmail).HasMaxLength(320).IsRequired();
        token.HasIndex(x => x.TokenHash).IsUnique();
        token.HasIndex(x => x.PersonEmail);

        var credential = modelBuilder.Entity<DeviceCredential>();
        credential.ToTable("device_credentials");
        credential.HasKey(x => x.Id);
        credential.Property(x => x.Serial).HasMaxLength(80).IsRequired();
        credential.Property(x => x.Hostname).HasMaxLength(120).IsRequired();
        credential.Property(x => x.Platform).HasMaxLength(16).IsRequired();
        credential.Property(x => x.OperatingSystem).HasMaxLength(80).IsRequired();
        credential.Property(x => x.AgentKind).HasMaxLength(16).IsRequired();
        credential.Property(x => x.AgentVersion).HasMaxLength(32).IsRequired();
        credential.Property(x => x.PersonEmail).HasMaxLength(320).IsRequired();
        credential.Property(x => x.PersonName).HasMaxLength(200).IsRequired();
        credential.Property(x => x.SecretHash).HasMaxLength(64).IsRequired();
        credential.HasIndex(x => x.Serial);
        credential.HasIndex(x => x.PersonEmail);

        var snapshot = modelBuilder.Entity<DevicePostureSnapshot>();
        snapshot.ToTable("device_posture_snapshots");
        snapshot.HasKey(x => x.Id);
        snapshot.Property(x => x.Serial).HasMaxLength(80).IsRequired();
        snapshot.Property(x => x.AgentKind).HasMaxLength(16).IsRequired();
        snapshot.Property(x => x.AgentVersion).HasMaxLength(32).IsRequired();
        snapshot.Property(x => x.Platform).HasMaxLength(16).IsRequired();
        snapshot.Property(x => x.ChecksJson).HasMaxLength(8000).IsRequired();
        snapshot.HasIndex(x => new { x.Serial, x.At });
        snapshot.HasIndex(x => x.DeviceId);

        var progress = modelBuilder.Entity<AcademyProgress>();
        progress.ToTable("academy_progress");
        progress.HasKey(x => x.Id);
        progress.Property(x => x.ModuleKey).HasMaxLength(64).IsRequired();
        progress.Property(x => x.PersonEmail).HasMaxLength(320).IsRequired();
        progress.Property(x => x.PersonName).HasMaxLength(200).IsRequired();
        progress.Property(x => x.CertificateNumber).HasMaxLength(40).IsRequired();
        progress.HasIndex(x => new { x.ModuleKey, x.PersonEmail }).IsUnique();

        var assignment = modelBuilder.Entity<AcademyAssignment>();
        assignment.ToTable("academy_assignments");
        assignment.HasKey(x => x.Id);
        assignment.Property(x => x.ModuleKey).HasMaxLength(64).IsRequired();
        assignment.Property(x => x.Audience).HasMaxLength(120).IsRequired();
        assignment.Property(x => x.UpdatedByEmail).HasMaxLength(320).IsRequired();
        assignment.HasIndex(x => new { x.ModuleKey, x.Audience }).IsUnique();
    }
}
