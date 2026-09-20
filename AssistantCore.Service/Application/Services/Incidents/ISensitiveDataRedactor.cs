namespace AssistantCore.Service.Application.Services.Incidents;

/// <summary>
/// Transformation de chaine pure (aucune dependance HttpContext/EF) afin de rester utilisable
/// depuis AssistantCore.Ingestion.Worker, qui n'a pas de contexte HTTP.
/// </summary>
public interface ISensitiveDataRedactor
{
    string Redact(string? text);
}
