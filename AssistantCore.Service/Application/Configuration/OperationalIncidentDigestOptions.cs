namespace AssistantCore.Service.Application.Configuration;

public sealed class OperationalIncidentDigestOptions
{
    public const string SectionName = "OperationalIncidentDigest";

    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Delai entre deux envois. Chaque envoi ne couvre que les incidents survenus
    /// depuis le dernier envoi reussi, jamais un incident deux fois.
    /// </summary>
    public int IntervalMinutes { get; init; } = 60;

    public string RecipientAddress { get; init; } = string.Empty;
}
