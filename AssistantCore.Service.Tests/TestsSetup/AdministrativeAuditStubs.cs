using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Abstractions;

namespace AssistantCore.Service.Tests;

internal sealed class StubAdministrativeAuditRepository : IAdministrativeAuditRepository
{
    public void Stage(AdministrativeAuditEntry entry)
    {
    }

    public Task PersistAsync(
        AdministrativeAuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

internal sealed class RecordingAdministrativeAuditRepository : IAdministrativeAuditRepository
{
    public List<AdministrativeAuditEntry> StagedEntries { get; } = [];

    public void Stage(AdministrativeAuditEntry entry)
    {
        StagedEntries.Add(entry);
    }

    public Task PersistAsync(
        AdministrativeAuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        StagedEntries.Add(entry);
        return Task.CompletedTask;
    }
}

internal sealed class StubCorrelationIdProvider : ICorrelationIdProvider
{
    public string CorrelationId { get; set; } = "request-test";

    public string GetCorrelationId() => CorrelationId;
}
