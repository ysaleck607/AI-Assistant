using AssistantCore.Repository.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssistantCore.Repository.Persistence.Configurations;

public sealed class PurgeOperationConfiguration : IEntityTypeConfiguration<PurgeOperation>
{
    public void Configure(EntityTypeBuilder<PurgeOperation> builder)
    {
        builder.ToTable("PurgeOperation");

        builder.HasKey(operation => operation.Id);

        builder.Property(operation => operation.Id)
            .ValueGeneratedNever();

        builder.Property(operation => operation.OrganizationId)
            .IsRequired();

        builder.Property(operation => operation.Scope)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(operation => operation.TargetId)
            .IsRequired();

        builder.Property(operation => operation.RequestedAt)
            .HasColumnType("datetimeoffset")
            .IsRequired();

        builder.Property(operation => operation.PurgeAfter)
            .HasColumnType("datetimeoffset")
            .IsRequired();

        builder.Property(operation => operation.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(operation => operation.Step)
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(operation => operation.LeaseExpiresAt)
            .HasColumnType("datetimeoffset");

        builder.Property(operation => operation.NextAttemptAt)
            .HasColumnType("datetimeoffset");

        builder.Property(operation => operation.CompletedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(operation => operation.LastErrorCode)
            .HasMaxLength(100);

        // Deux demandes identiques (meme organisation, meme categorie, meme cible) ne
        // doivent jamais ouvrir deux operations : c'est la contrainte d'idempotence.
        builder.HasIndex(operation => new
        {
            operation.OrganizationId,
            operation.Scope,
            operation.TargetId
        }).IsUnique();

        builder.HasIndex(operation => new
        {
            operation.Status,
            operation.PurgeAfter,
            operation.NextAttemptAt,
            operation.LeaseExpiresAt
        });
    }
}
