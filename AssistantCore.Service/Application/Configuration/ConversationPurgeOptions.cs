namespace AssistantCore.Service.Application.Configuration;

public sealed class ConversationPurgeOptions
{
    public const string SectionName = "ConversationPurge";

    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Duree du bail pendant laquelle une demande reclamee n'est pas reprise par
    /// un autre worker. Passe ce delai, un worker disparu ne bloque plus rien.
    /// </summary>
    public int LeaseMinutes { get; init; } = 10;

    /// <summary>
    /// Delai entre deux balayages lorsqu'il n'y a plus rien a purger.
    /// </summary>
    public int PollingIntervalSeconds { get; init; } = 300;

    public int MaximumAttempts { get; init; } = 5;

    /// <summary>
    /// Delai de base du reessai, double a chaque tentative.
    /// </summary>
    public int RetryBackoffMinutes { get; init; } = 5;

    public bool IsValid() =>
        LeaseMinutes > 0
        && PollingIntervalSeconds > 0
        && MaximumAttempts > 0
        && RetryBackoffMinutes > 0;
}
