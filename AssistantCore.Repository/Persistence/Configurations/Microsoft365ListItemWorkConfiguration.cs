using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssistantCore.Repository.Persistence.Configurations;

public sealed class Microsoft365ListItemWorkConfiguration(
    IFieldEncryptor webUrlEncryptor,
    IFieldEncryptor fieldsJsonEncryptor) : IEntityTypeConfiguration<Microsoft365ListItemWork>
{
    public void Configure(EntityTypeBuilder<Microsoft365ListItemWork> builder)
    {
        builder.ToTable("Microsoft365ListItemWork");
        builder.HasKey(work => work.Id);

        builder.Property(work => work.Id).ValueGeneratedNever();
        builder.Property(work => work.WorkType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(work => work.SiteId).HasMaxLength(400).IsRequired();
        builder.Property(work => work.ListId).HasMaxLength(400).IsRequired();
        builder.Property(work => work.ListItemId).HasMaxLength(400).IsRequired();
        builder.Property(work => work.ETag).HasMaxLength(1000);
        builder.Property(work => work.WebUrl)
            .HasConversion(new NullableEncryptedStringConverter(webUrlEncryptor))
            .HasColumnType("nvarchar(max)");
        builder.Property(work => work.FieldsJson)
            .HasConversion(new NullableEncryptedStringConverter(fieldsJsonEncryptor))
            .HasColumnType("nvarchar(max)");
        builder.Property(work => work.DeduplicationKey).HasMaxLength(64).IsRequired();
        builder.Property(work => work.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(work => work.LastErrorCode).HasMaxLength(200);

        builder.HasOne(work => work.Organization)
            .WithMany()
            .HasForeignKey(work => work.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(work => work.Microsoft365Source)
            .WithMany()
            .HasForeignKey(work => work.Microsoft365SourceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(work => work.Microsoft365Synchronization)
            .WithMany()
            .HasForeignKey(work => work.Microsoft365SynchronizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(work => work.DeduplicationKey).IsUnique();
        builder.HasIndex(work => new { work.Microsoft365SourceId, work.CreatedAt });
        builder.HasIndex(work => work.CreatedAt)
            .HasDatabaseName("IX_Microsoft365ListItemWork_Pending_CreatedAt")
            .HasFilter($"[Status] = '{Microsoft365ListItemWorkStatus.Pending}'");
        builder.HasIndex(work => new { work.NextAttemptAt, work.CreatedAt })
            .HasDatabaseName("IX_Microsoft365ListItemWork_RetryDue")
            .HasFilter($"[Status] = '{Microsoft365ListItemWorkStatus.TemporaryFailure}'");
        builder.HasIndex(work => new { work.LeaseExpiresAt, work.CreatedAt })
            .HasDatabaseName("IX_Microsoft365ListItemWork_ExpiredLease")
            .HasFilter($"[Status] = '{Microsoft365ListItemWorkStatus.Processing}'");
        builder.HasIndex(work => new { work.Status, work.CompletedAt })
            .HasDatabaseName("IX_Microsoft365ListItemWork_TerminalRetention");
    }
}
