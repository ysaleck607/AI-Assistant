using AssistantCore.Repository.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssistantCore.Repository.Persistence.Configurations;

public sealed class Microsoft365SiteConfiguration : IEntityTypeConfiguration<Microsoft365Site>
{
    public void Configure(EntityTypeBuilder<Microsoft365Site> builder)
    {
        builder.ToTable("Microsoft365Site");

        builder.Property(site => site.OrganizationId).IsRequired();
        builder.Property(site => site.OrganizationConnectorId).IsRequired();
        builder.Property(site => site.SiteId).HasMaxLength(400).IsRequired();

        builder.HasOne(site => site.Organization)
            .WithMany()
            .HasForeignKey(site => site.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(site => site.OrganizationConnector)
            .WithMany()
            .HasForeignKey(site => site.OrganizationConnectorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(site => new
        {
            site.OrganizationId,
            site.OrganizationConnectorId,
            site.SiteId
        }).IsUnique();

        builder.HasIndex(site => new
        {
            site.OrganizationId,
            site.SiteId
        }).HasDatabaseName("IX_Microsoft365Site_OrganizationId_SiteId");
    }
}
