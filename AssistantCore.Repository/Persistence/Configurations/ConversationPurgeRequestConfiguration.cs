using AssistantCore.Repository.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssistantCore.Repository.Persistence.Configurations;

public sealed class ConversationPurgeRequestConfiguration
    : IEntityTypeConfiguration<ConversationPurgeRequest>
{
    public void Configure(EntityTypeBuilder<ConversationPurgeRequest> builder)
    {
        builder.ToTable("ConversationPurgeRequest");

        builder.HasKey(request => request.Id);

        builder.Property(request => request.Id)
            .ValueGeneratedNever();

        builder.Property(request => request.ConversationId)
            .IsRequired();

        builder.Property(request => request.OrganizationId)
            .IsRequired();

        builder.Property(request => request.RequestedAt)
            .HasColumnType("datetimeoffset")
            .IsRequired();

        builder.Property(request => request.PurgeAfter)
            .HasColumnType("datetimeoffset")
            .IsRequired();

        builder.Property(request => request.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(request => request.Step)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(request => request.LeaseExpiresAt)
            .HasColumnType("datetimeoffset");

        builder.Property(request => request.NextAttemptAt)
            .HasColumnType("datetimeoffset");

        builder.Property(request => request.CompletedAt)
            .HasColumnType("datetimeoffset");

        builder.Property(request => request.LastErrorCode)
            .HasMaxLength(100);

        // Aucune clef etrangere vers Conversation : la demande porte la preuve de la
        // purge et doit survivre a la conversation qu'elle vient de supprimer. Une
        // cascade emporterait la preuve en meme temps que la donnee.

        builder.HasIndex(request => request.ConversationId)
            .IsUnique();

        builder.HasIndex(request => new
        {
            request.Status,
            request.PurgeAfter,
            request.NextAttemptAt,
            request.LeaseExpiresAt
        });
    }
}
