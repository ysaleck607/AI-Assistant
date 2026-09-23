using AssistantCore.Repository.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssistantCore.Repository.Persistence.Configurations;

public sealed class Microsoft365DriveConfiguration : IEntityTypeConfiguration<Microsoft365Drive>
{
    public void Configure(EntityTypeBuilder<Microsoft365Drive> builder)
    {
        builder.ToTable("Microsoft365Drive");

        builder.Property(drive => drive.OrganizationId).IsRequired();
        builder.Property(drive => drive.OrganizationConnectorId).IsRequired();
        builder.Property(drive => drive.SiteId).HasMaxLength(400);
        builder.Property(drive => drive.DriveId).HasMaxLength(400).IsRequired();
        builder.Property(drive => drive.OwnerUserObjectId).HasMaxLength(400);
        builder.Property(drive => drive.OwnerUserPrincipalName).HasMaxLength(320);

        builder.HasOne(drive => drive.Organization)
            .WithMany()
            .HasForeignKey(drive => drive.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(drive => drive.OrganizationConnector)
            .WithMany()
            .HasForeignKey(drive => drive.OrganizationConnectorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(drive => new
        {
            drive.OrganizationId,
            drive.DriveId
        }).IsUnique();

        builder.HasIndex(drive => new
        {
            drive.OrganizationId,
            drive.OwnerUserObjectId
        });

        builder.HasIndex(drive => new
        {
            drive.OrganizationId,
            drive.SiteId
        }).HasDatabaseName("IX_Microsoft365Drive_OrganizationId_SiteId");
    }
}
