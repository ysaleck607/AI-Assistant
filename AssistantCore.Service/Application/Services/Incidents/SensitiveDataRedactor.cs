using System.Text.RegularExpressions;

namespace AssistantCore.Service.Application.Services.Incidents;

/// <summary>
/// Redige les secrets (tokens, headers Authorization, connection strings) avant qu'un texte
/// ne soit persiste dans un OperationalIncident. Ne vise pas la PII (organisation/membre sont
/// deja identifies par des colonnes dediees, pas par du texte libre).
/// </summary>
public sealed partial class SensitiveDataRedactor : ISensitiveDataRedactor
{
    private const string Redacted = "[REDACTED]";

    // Ordre volontaire : les patterns les plus specifiques (Authorization/Bearer, JWT,
    // connection strings) d'abord, le filet de securite generique en dernier - pour qu'il ne
    // remplace pas une valeur deja marquee [REDACTED].
    private static readonly Regex[] Patterns =
    [
        AuthorizationBearerPattern(),
        BearerTokenPattern(),
        JwtPattern(),
        ConnectionStringSecretPattern(),
        ApiKeyPattern(),
        LongOpaqueTokenPattern()
    ];

    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var redacted = text;
        foreach (var pattern in Patterns)
        {
            redacted = pattern.Replace(redacted, Redacted);
        }

        return redacted;
    }

    [GeneratedRegex(@"Authorization\s*:\s*Bearer\s+\S+", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorizationBearerPattern();

    [GeneratedRegex(@"\bBearer\s+\S+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenPattern();

    [GeneratedRegex(@"\bey[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b")]
    private static partial Regex JwtPattern();

    [GeneratedRegex(
        @"(AccountKey|SharedAccessSignature|sig|client_secret|Password)\s*=\s*[^;&\s]+",
        RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionStringSecretPattern();

    [GeneratedRegex(@"(api[-_]?key|x-api-key)\s*[:=]\s*\S+", RegexOptions.IgnoreCase)]
    private static partial Regex ApiKeyPattern();

    // Filet de securite : longues sequences base64 opaques (JWT/GUID deja geres par des
    // patterns dedies et non captures ici - un GUID a 32 ou 36 caracteres reste en dessous
    // du seuil de 40).
    [GeneratedRegex(@"\b[A-Za-z0-9+/]{40,}={0,2}\b")]
    private static partial Regex LongOpaqueTokenPattern();
}
