using AssistantCore.Repository.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssistantCore.Repository.Persistence.Configurations;

public sealed class Microsoft365SourceConfiguration(
    IFieldEncryptor displayNameEncryptor,
    IFieldEncryptor webUrlEncryptor,
    IFieldEncryptor deltaLinkEncryptor) : IEntityTypeConfiguration<Microsoft365Source>
{
    public void Configure(EntityTypeBuilder<Microsoft365Source> builder)
    {
        builder.UseTptMappingStrategy();
        builder.ToTable("Microsoft365Source");
        builder.HasKey(source => source.Id);

        builder.Property(source => source.Id).ValueGeneratedNever();
        builder.Property(source => source.Kind).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(source => source.ExternalResourceId).HasMaxLength(400).IsRequired();
        builder.Property(source => source.ParentExternalResourceId).HasMaxLength(400);
        builder.Property(source => source.DisplayName)
            .HasConversion(new EncryptedStringConverter(displayNameEncryptor))
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        builder.Property(source => source.WebUrl)
            .HasConversion(new NullableEncryptedStringConverter(webUrlEncryptor))
            .HasColumnType("nvarchar(max)");
        builder.Property(source => source.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(source => source.StatusBeforeUnavailable).HasConversion<string>().HasMaxLength(30);
        builder.Property(source => source.DeltaLink)
            .HasConversion(new NullableEncryptedStringConverter(deltaLinkEncryptor))
            .HasColumnType("nvarchar(max)");
        builder.Property(source => source.LastErrorCode).HasMaxLength(100);

        builder.HasOne(source => source.Microsoft365Connection)
            .WithMany(connection => connection.Sources)
            .HasForeignKey(source => source.Microsoft365ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(source => new
        {
            source.Microsoft365ConnectionId,
            source.Kind,
            source.ExternalResourceId,
            source.ParentExternalResourceId
        }).IsUnique()
            .HasDatabaseName("IX_Microsoft365Source_Connection_Type_Resource_Parent");
    }
}
