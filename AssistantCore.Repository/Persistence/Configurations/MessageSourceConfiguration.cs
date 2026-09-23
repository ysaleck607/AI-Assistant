using AssistantCore.Repository.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssistantCore.Repository.Persistence.Configurations;

public sealed class MessageSourceConfiguration(
    IFieldEncryptor titleEncryptor,
    IFieldEncryptor referenceEncryptor,
    IFieldEncryptor urlEncryptor) : IEntityTypeConfiguration<MessageSource>
{
    public void Configure(EntityTypeBuilder<MessageSource> builder)
    {
        builder.ToTable("MessageSource");

        builder.HasKey(source => source.Id);

        builder.Property(source => source.Id)
            .ValueGeneratedNever();

        builder.Property(source => source.MessageId)
            .IsRequired();

        builder.Property(source => source.SourceType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(source => source.Title)
            .HasConversion(new EncryptedStringConverter(titleEncryptor))
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Property(source => source.Reference)
            .HasConversion(new EncryptedStringConverter(referenceEncryptor))
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Property(source => source.Url)
            .HasConversion(new NullableEncryptedStringConverter(urlEncryptor))
            .HasColumnType("nvarchar(max)");

        builder.Property(source => source.SourceDate)
            .HasColumnType("datetimeoffset");

        builder.HasIndex(source => source.MessageId);
    }
}
