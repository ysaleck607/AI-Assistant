using AssistantCore.Repository.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssistantCore.Repository.Persistence.Configurations;

public sealed class OrganizationConnectorSourceConfiguration : IEntityTypeConfiguration<OrganizationConnectorSource>
{
    public void Configure(EntityTypeBuilder<OrganizationConnectorSource> builder)
    {
        builder.ToTable("OrganizationConnectorSource");

        builder.HasKey(source => new { source.OrganizationConnectorId, source.SourceType });

        builder.Property(source => source.SourceType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(source => source.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(source => source.IsIndexed)
            .IsRequired();
    }
}
