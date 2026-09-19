using AssistantCore.Repository.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssistantCore.Repository.Persistence.Configurations;

public sealed class Microsoft365ConnectionConfiguration : IEntityTypeConfiguration<Microsoft365Connection>
{
    public void Configure(EntityTypeBuilder<Microsoft365Connection> builder)
    {
        builder.ToTable("Microsoft365Connection");
        builder.HasKey(connection => connection.Id);

        builder.Property(connection => connection.Id).ValueGeneratedNever();
        builder.Property(connection => connection.TenantId).HasMaxLength(100);
        builder.Property(connection => connection.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(connection => connection.ConsentStateHash).HasMaxLength(64);
        builder.Property(connection => connection.OnboardingCompletedAt);
        builder.Property(connection => connection.LastErrorCode).HasMaxLength(100);
        builder.Property(connection => connection.CreatedAt).IsRequired();
        builder.Property(connection => connection.UpdatedAt).IsRequired();
        builder.Property(connection => connection.RowVersion).IsRowVersion();

        builder.HasOne(connection => connection.Organization)
            .WithMany(organization => organization.Microsoft365Connections)
            .HasForeignKey(connection => connection.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(connection => connection.OrganizationConnector)
            .WithOne(connector => connector.Microsoft365Connection)
            .HasForeignKey<Microsoft365Connection>(connection => connection.OrganizationConnectorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(connection => connection.OrganizationId).IsUnique();
        builder.HasIndex(connection => connection.OrganizationConnectorId).IsUnique();
        builder.HasIndex(connection => connection.TenantId)
            .IsUnique()
            .HasFilter("[TenantId] IS NOT NULL");
        builder.HasIndex(connection => connection.ConsentStateHash)
            .IsUnique()
            .HasFilter("[ConsentStateHash] IS NOT NULL");
    }
}
