using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssistantCore.Repository.Persistence.Configurations;

public sealed class Microsoft365ReindexOperationConfiguration
    : IEntityTypeConfiguration<Microsoft365ReindexOperation>
{
    public void Configure(EntityTypeBuilder<Microsoft365ReindexOperation> builder)
    {
        builder.ToTable("Microsoft365ReindexOperation");
        builder.HasKey(operation => operation.Id);

        builder.Property(operation => operation.Id).ValueGeneratedNever();
        builder.Property(operation => operation.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(operation => operation.Reason).HasMaxLength(500);
        builder.Property(operation => operation.SourceCount).IsRequired();
        builder.Property(operation => operation.CompletedSourceCount).IsRequired();
        builder.Property(operation => operation.DiscoveredDocumentCount).IsRequired();
        builder.Property(operation => operation.ProcessedDocumentCount).IsRequired();
        builder.Property(operation => operation.IgnoredDocumentCount).IsRequired();
        builder.Property(operation => operation.FailedDocumentCount).IsRequired();
        builder.Property(operation => operation.LastErrorCode).HasMaxLength(100);

        builder.HasOne(operation => operation.Organization)
            .WithMany()
            .HasForeignKey(operation => operation.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(operation => operation.Microsoft365Connection)
            .WithMany()
            .HasForeignKey(operation => operation.Microsoft365ConnectionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(operation => new { operation.OrganizationId, operation.RequestedAt });

        // Deux reprises completes ne peuvent pas viser la meme organisation en meme temps.
        // La base refuse la seconde insertion, meme si deux operateurs cliquent simultanement.
        builder.HasIndex(operation => operation.OrganizationId)
            .IsUnique()
            .HasFilter(
                $"[Status] IN ('{Microsoft365ReindexOperationStatus.Pending}', "
                + $"'{Microsoft365ReindexOperationStatus.Running}')");
    }
}
