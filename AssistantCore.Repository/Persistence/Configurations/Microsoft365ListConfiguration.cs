using AssistantCore.Repository.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssistantCore.Repository.Persistence.Configurations;

public sealed class Microsoft365ListConfiguration : IEntityTypeConfiguration<Microsoft365List>
{
    public void Configure(EntityTypeBuilder<Microsoft365List> builder)
    {
        builder.ToTable("Microsoft365List");

        builder.Property(list => list.OrganizationId).IsRequired();
        builder.Property(list => list.OrganizationConnectorId).IsRequired();
        builder.Property(list => list.SiteId).HasMaxLength(400).IsRequired();
        builder.Property(list => list.ListId).HasMaxLength(400).IsRequired();
        builder.Property(list => list.SchemaFingerprint).HasMaxLength(64);
        builder.Property(list => list.RequiresItemReprocessing).IsRequired();

        builder.HasOne(list => list.Organization)
            .WithMany()
            .HasForeignKey(list => list.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(list => list.OrganizationConnector)
            .WithMany()
            .HasForeignKey(list => list.OrganizationConnectorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(list => new
        {
            list.OrganizationId,
            list.OrganizationConnectorId,
            list.SiteId,
            list.ListId
        }).IsUnique();

        builder.HasIndex(list => new
        {
            list.OrganizationId,
            list.SiteId
        }).HasDatabaseName("IX_Microsoft365List_OrganizationId_SiteId");
    }
}
