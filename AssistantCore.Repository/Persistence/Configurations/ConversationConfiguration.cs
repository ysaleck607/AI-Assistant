using AssistantCore.Repository.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssistantCore.Repository.Persistence.Configurations;

public sealed class ConversationConfiguration(IFieldEncryptor titleEncryptor) : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversation");

        builder.HasKey(conversation => conversation.Id);

        builder.Property(conversation => conversation.Id)
            .ValueGeneratedNever();

        builder.Property(conversation => conversation.OrganizationId)
            .IsRequired();

        builder.Property(conversation => conversation.OwnerMemberId)
            .IsRequired();

        builder.Property(conversation => conversation.Title)
            .HasConversion(new EncryptedStringConverter(titleEncryptor))
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Property(conversation => conversation.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(conversation => conversation.Version)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(conversation => conversation.CreatedAt)
            .HasColumnType("datetimeoffset")
            .IsRequired();

        builder.Property(conversation => conversation.UpdatedAt)
            .HasColumnType("datetimeoffset")
            .IsRequired();

        builder.Property(conversation => conversation.DeletedAt)
            .HasColumnType("datetimeoffset");

        builder.HasOne(conversation => conversation.Organization)
            .WithMany()
            .HasForeignKey(conversation => conversation.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(conversation => conversation.OwnerMember)
            .WithMany()
            .HasForeignKey(conversation => conversation.OwnerMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(conversation => conversation.Messages)
            .WithOne(message => message.Conversation)
            .HasForeignKey(message => message.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(conversation => new
        {
            conversation.OrganizationId,
            conversation.OwnerMemberId,
            conversation.Status,
            conversation.UpdatedAt,
            conversation.Id
        })
            .HasFilter("[DeletedAt] IS NULL")
            .HasDatabaseName("IX_Conversation_ActiveListing");
    }
}
