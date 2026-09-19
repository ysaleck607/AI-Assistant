using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssistantCore.Repository.Persistence.Configurations;

public sealed class PurgeOperationConfiguration : IEntityTypeConfiguration<PurgeOperation>
{
    public void Configure(EntityTypeBuilder<PurgeOperation> builder)
    {
        builder.ToTable("PurgeOperation");

        builder.HasKey(operation => operation.Id);
        builder.Property(operation => operation.Id).ValueGeneratedNever();
        builder.Property(operation => operation.OrganizationId).IsRequired();
        builder.Property(operation => operation.Scope).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(operation => operation.TargetId).IsRequired();
        builder.Property(operation => operation.RequestedAt).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(operation => operation.PurgeAfter).HasColumnType("datetimeoffset").IsRequired();
        builder.Property(operation => operation.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(operation => operation.Step).HasMaxLength(60).IsRequired();
        builder.Property(operation => operation.LeaseExpiresAt).HasColumnType("datetimeoffset");
        builder.Property(operation => operation.NextAttemptAt).HasColumnType("datetimeoffset");
        builder.Property(operation => operation.CompletedAt).HasColumnType("datetimeoffset");
        builder.Property(operation => operation.LastErrorCode).HasMaxLength(100);

        builder.HasIndex(operation => new
        {
            operation.OrganizationId,
            operation.Scope,
            operation.TargetId
        }).IsUnique();

        builder.HasIndex(operation => new { operation.PurgeAfter, operation.RequestedAt })
            .HasDatabaseName("IX_PurgeOperation_Pending_Due")
            .HasFilter($"[Status] = '{PurgeOperationStatus.Pending}'");
        builder.HasIndex(operation => new { operation.NextAttemptAt, operation.PurgeAfter, operation.RequestedAt })
            .HasDatabaseName("IX_PurgeOperation_Retry_Due")
            .HasFilter($"[Status] = '{PurgeOperationStatus.TemporaryFailure}'");
        builder.HasIndex(operation => new { operation.LeaseExpiresAt, operation.PurgeAfter, operation.RequestedAt })
            .HasDatabaseName("IX_PurgeOperation_ExpiredLease")
            .HasFilter($"[Status] = '{PurgeOperationStatus.Processing}'");
    }
}
