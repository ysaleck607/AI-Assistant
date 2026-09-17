using AssistantCore.Repository.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssistantCore.Repository.Persistence.Configurations;

public sealed class MessageConfiguration(IFieldEncryptor contentEncryptor) : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Message");

        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id)
            .ValueGeneratedNever();

        builder.Property(message => message.ConversationId)
            .IsRequired();

        builder.Property(message => message.Role)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(message => message.Content)
            .HasConversion(new EncryptedStringConverter(contentEncryptor))
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Property(message => message.ProcessingStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(message => message.Model)
            .HasMaxLength(100);

        builder.Property(message => message.ProcessingErrorCode)
            .HasMaxLength(100);

        builder.Property(message => message.CreatedAt)
            .HasColumnType("datetimeoffset")
            .IsRequired();

        builder.Property(message => message.UpdatedAt)
            .HasColumnType("datetimeoffset")
            .IsRequired();

        builder.HasMany(message => message.Sources)
            .WithOne(source => source.Message)
            .HasForeignKey(source => source.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(message => message.Warnings)
            .WithOne(warning => warning.Message)
            .HasForeignKey(warning => warning.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(message => new
        {
            message.ConversationId,
            message.CreatedAt,
            message.Id
        });
    }
}
