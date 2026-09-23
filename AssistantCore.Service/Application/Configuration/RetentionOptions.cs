namespace AssistantCore.Service.Application.Configuration;

public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>
    /// Delai, en jours, pendant lequel une conversation supprimee reste recuperable
    /// avant que le travail de purge devienne eligible.
    /// </summary>
    public int ConversationRecoveryDays { get; init; }

    /// <summary>
    /// Duree, en jours, pendant laquelle un contenu Microsoft 365 indexe est conserve
    /// apres avoir cesse d'etre disponible a la source avant de devenir eligible a la purge.
    /// </summary>
    public int SearchRetentionDays { get; init; }

    /// <summary>
    /// Duree, en jours, pendant laquelle un travail d'ingestion termine (document ou
    /// element de liste) est conserve avant de devenir eligible a la purge.
    /// </summary>
    public int IngestionRetentionDays { get; init; }

    /// <summary>
    /// Duree, en jours, pendant laquelle les journaux techniques sont conserves avant
    /// de devenir eligibles a la purge.
    /// </summary>
    public int LogsRetentionDays { get; init; }

    /// <summary>
    /// Duree, en jours, pendant laquelle une entree d'audit administratif est conservee
    /// avant de devenir eligible a la purge. Purger une entree d'audit ne doit jamais
    /// effacer la trace qu'une action a eu lieu, seulement son contenu detaille.
    /// </summary>
    public int AuditRetentionDays { get; init; }

    public bool IsValid() =>
        ConversationRecoveryDays > 0
        && SearchRetentionDays > 0
        && IngestionRetentionDays > 0
        && LogsRetentionDays > 0
        && AuditRetentionDays > 0;
}
