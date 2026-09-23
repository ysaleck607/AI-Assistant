namespace AssistantCore.Repository.Domain.Entities;

/// <summary>
/// Consommation de tokens par modele pour une periode mensuelle donnee. Global a
/// l'application (pas par organisation) : un seul budget mensuel est attribue par
/// modele, partage par toutes les organisations, pour minimiser les couts.
/// </summary>
public sealed class LlmTokenConsumption
{
    public Guid Id { get; set; }

    public string Model { get; set; } = string.Empty;

    /// <summary>Premier jour du mois calendaire (UTC, heure a zero).</summary>
    public DateTimeOffset PeriodStart { get; set; }

    public long TokensConsumed { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
