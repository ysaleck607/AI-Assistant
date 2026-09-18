using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssistantCore.Repository.Persistence.Configurations;

public sealed class OrganizationMemberConfiguration(
    IFieldEncryptor nameEncryptor,
    IFieldEncryptor emailEncryptor) : IEntityTypeConfiguration<OrganizationMember>
{
    public void Configure(EntityTypeBuilder<OrganizationMember> builder)
    {
        builder.ToTable("OrganizationMember");

        builder.HasKey(member => member.Id);

        builder.Property(member => member.Id)
            .ValueGeneratedNever();

        builder.Property(member => member.OrganizationId)
            .IsRequired();

        builder.Property(member => member.Name)
            .HasConversion(new EncryptedStringConverter(nameEncryptor))
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Property(member => member.Email)
            .HasConversion(new EncryptedStringConverter(emailEncryptor))
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        // Index aveugle HMAC : permet une recherche exacte sur l'email sans jamais le
        // dechiffrer. Unique par organisation - decision de l'equipe (2026-09-18) de
        // garder l'email unique pour ne pas complexifier les permissions avant le
        // deploiement client ; le cas d'un invite externe partageant un email avec un
        // membre interne (voir #31/#73) est reporte a plus tard, pas encore supporte.
        builder.Property(member => member.EmailLookupHash)
            .HasMaxLength(64);

        builder.HasIndex(member => new { member.OrganizationId, member.EmailLookupHash })
            .IsUnique();

        builder.Property(member => member.IdentityProvider)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(member => member.ExternalUserId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(member => member.Role)
            .HasConversion<string>()
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(member => member.Status)
            .HasConversion(
                status => status == RecordStatus.Active ? "Actif" : "Inactif",
                value => value == "Actif" ? RecordStatus.Active : RecordStatus.Inactive)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(member => member.Version)
            .IsConcurrencyToken()
            .IsRequired();

        // Email is never the identity key: a guest's client-tenant row can legitimately
        // share an email with an unrelated internal member, and the two must stay distinct.
        // (OrganizationId, IdentityProvider, ExternalUserId) below is the only identity key.
        builder.HasIndex(member => new { member.OrganizationId, member.IdentityProvider, member.ExternalUserId })
            .IsUnique();
    }
}
