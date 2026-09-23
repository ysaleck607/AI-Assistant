using AssistantCore.Repository.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssistantCore.Repository.Persistence.Configurations;

public sealed class OperationalIncidentConfiguration : IEntityTypeConfiguration<OperationalIncident>
{
    public void Configure(EntityTypeBuilder<OperationalIncident> builder)
    {
        builder.ToTable("OperationalIncident");
        builder.HasKey(incident => incident.Id);

        builder.Property(incident => incident.Id).ValueGeneratedNever();
        builder.Property(incident => incident.OccurredAt).IsRequired();

        builder.Property(incident => incident.Subsystem)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(incident => incident.Severity)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(incident => incident.CorrelationId)
            .HasMaxLength(100)
            .IsRequired();

        // Nullable a la difference de AdministrativeAuditEntry.OrganizationId : certains
        // incidents (demarrage worker, panne Foundry globale) ne sont rattaches a aucune
        // organisation.
        builder.Property(incident => incident.OrganizationId);

        builder.Property(incident => incident.OrganizationMemberId);

        builder.Property(incident => incident.Summary)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(incident => incident.SafeDetail)
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Property(incident => incident.RelatedResourceType)
            .HasMaxLength(50);

        builder.Property(incident => incident.RelatedResourceId);

        builder.Property(incident => incident.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(incident => incident.ResolvedAt);

        builder.Property(incident => incident.ResolvedByEmail)
            .HasMaxLength(320);

        builder.Property(incident => incident.ResolutionNotes)
            .HasColumnType("nvarchar(max)");

        builder.HasOne(incident => incident.Organization)
            .WithMany()
            .HasForeignKey(incident => incident.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasIndex(incident => new { incident.OrganizationId, incident.OccurredAt })
            .HasDatabaseName("IX_OperationalIncident_OrganizationId_OccurredAt");

        builder.HasIndex(incident => incident.CorrelationId)
            .HasDatabaseName("IX_OperationalIncident_CorrelationId");

        builder.HasIndex(incident => new { incident.Subsystem, incident.Severity, incident.OccurredAt })
            .HasDatabaseName("IX_OperationalIncident_Subsystem_Severity_OccurredAt");

        builder.HasIndex(incident => new { incident.RelatedResourceType, incident.RelatedResourceId })
            .HasDatabaseName("IX_OperationalIncident_RelatedResourceType_RelatedResourceId");
    }
}
