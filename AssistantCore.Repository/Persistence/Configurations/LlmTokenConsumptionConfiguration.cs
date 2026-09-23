using AssistantCore.Repository.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssistantCore.Repository.Persistence.Configurations;

public sealed class LlmTokenConsumptionConfiguration : IEntityTypeConfiguration<LlmTokenConsumption>
{
    public void Configure(EntityTypeBuilder<LlmTokenConsumption> builder)
    {
        builder.ToTable("LlmTokenConsumption");
        builder.HasKey(consumption => consumption.Id);

        builder.Property(consumption => consumption.Id).ValueGeneratedNever();

        builder.Property(consumption => consumption.Model)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(consumption => consumption.PeriodStart).IsRequired();

        builder.Property(consumption => consumption.TokensConsumed).IsRequired();

        builder.Property(consumption => consumption.UpdatedAt).IsRequired();

        builder.HasIndex(consumption => new { consumption.Model, consumption.PeriodStart })
            .IsUnique()
            .HasDatabaseName("IX_LlmTokenConsumption_Model_PeriodStart");
    }
}
