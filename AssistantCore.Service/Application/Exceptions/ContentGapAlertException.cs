namespace AssistantCore.Service.Application.Exceptions;

/// <summary>
/// Never thrown to a caller - exists only to carry a human-readable message into an
/// OperationalIncident when the model answered without finding any supporting evidence.
/// Excerpts are truncated so both the question and the model's response fit within the
/// 300-character incident summary shown in the digest email; the full conversation stays
/// reachable via the incident's related Conversation id for investigation.
/// </summary>
public sealed class ContentGapAlertException(string questionText, string modelResponseText)
    : Exception(
        $"Alerte : question sans reponse documentee. Q : \"{Truncate(questionText)}\" " +
        $"| Reponse : \"{Truncate(modelResponseText)}\".")
{
    private const int MaxExcerptLength = 100;

    private static string Truncate(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length > MaxExcerptLength ? trimmed[..MaxExcerptLength] + "…" : trimmed;
    }
}
