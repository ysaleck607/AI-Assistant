using AssistantCore.Repository.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssistantCore.Repository.Persistence.Configurations;

public sealed class Microsoft365IndexedContentConfiguration(
    IFieldEncryptor siteUrlEncryptor,
    IFieldEncryptor titleEncryptor,
    IFieldEncryptor webUrlEncryptor) : IEntityTypeConfiguration<Microsoft365IndexedContent>
{
    public void Configure(EntityTypeBuilder<Microsoft365IndexedContent> builder)
    {
        builder.ToTable("Microsoft365IndexedContent");
        builder.HasKey(content => content.Id);
        builder.Property(content => content.Id).ValueGeneratedNever();
        builder.Property(content => content.ExternalContentId).HasMaxLength(400).IsRequired();
        builder.Property(content => content.SiteUrl)
            .HasConversion(new NullableEncryptedStringConverter(siteUrlEncryptor))
            .HasColumnType("nvarchar(max)");
        builder.Property(content => content.DocumentVersion).HasMaxLength(1000);
        builder.Property(content => content.Title)
            .HasConversion(new NullableEncryptedStringConverter(titleEncryptor))
            .HasColumnType("nvarchar(max)");
        builder.Property(content => content.WebUrl)
            .HasConversion(new NullableEncryptedStringConverter(webUrlEncryptor))
            .HasColumnType("nvarchar(max)");
        builder.Property(content => content.AclFingerprint).HasMaxLength(64);
        builder.HasOne(content => content.Organization)
            .WithMany()
            .HasForeignKey(content => content.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(content => content.Microsoft365Source)
            .WithMany()
            .HasForeignKey(content => content.Microsoft365SourceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(content => new
        {
            content.OrganizationId,
            content.Microsoft365SourceId,
            content.ExternalContentId
        }).IsUnique();
        builder.HasIndex(content => new
        {
            content.NextAclReconciliationAt,
            content.UpdatedAt
        }).HasDatabaseName("IX_Microsoft365IndexedContent_AclReconciliationDue");
    }
}
