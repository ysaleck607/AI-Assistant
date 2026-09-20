using AssistantCore.Repository.Abstractions;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Microsoft365.ContentExtraction;
using AssistantCore.Service.Application.Services.Incidents;

namespace AssistantCore.Service.Tests.Incidents;

public sealed class OperationalIncidentSubsystemClassifierTests
{
    public static IEnumerable<object[]> ExceptionCases()
    {
        yield return
        [
            new Microsoft365GraphTransientException("Graph throttled."),
            OperationalIncidentSubsystem.MicrosoftGraph,
            OperationalIncidentSeverity.Warning
        ];
        yield return
        [
            new Microsoft365AclResolutionException(),
            OperationalIncidentSubsystem.MicrosoftGraph,
            OperationalIncidentSeverity.Error
        ];
        yield return
        [
            new Microsoft365ExternalException("Consent provider unavailable."),
            OperationalIncidentSubsystem.MicrosoftGraph,
            OperationalIncidentSeverity.Error
        ];
        yield return
        [
            new Microsoft365SourceAccessDeniedException("Access denied."),
            OperationalIncidentSubsystem.AuthAndAccess,
            OperationalIncidentSeverity.Warning
        ];
        yield return
        [
            new UnauthorizedAccessException("Not authenticated."),
            OperationalIncidentSubsystem.AuthAndAccess,
            OperationalIncidentSeverity.Warning
        ];
        yield return
        [
            new ForbiddenException("Forbidden."),
            OperationalIncidentSubsystem.AuthAndAccess,
            OperationalIncidentSeverity.Warning
        ];
        yield return
        [
            new Microsoft365ContentExtractionException(Microsoft365ContentExtractionStatus.CorruptedDocument),
            OperationalIncidentSubsystem.SharePoint,
            OperationalIncidentSeverity.Warning
        ];
        yield return
        [
            new AzureAiSearchUnavailableException("Search request failed."),
            OperationalIncidentSubsystem.AzureAiSearch,
            OperationalIncidentSeverity.Error
        ];
        yield return
        [
            new AiProviderTimeoutException("Foundry"),
            OperationalIncidentSubsystem.FoundryLlm,
            OperationalIncidentSeverity.Warning
        ];
        yield return
        [
            new AiProviderLimitException("Foundry"),
            OperationalIncidentSubsystem.FoundryLlm,
            OperationalIncidentSeverity.Warning
        ];
        yield return
        [
            new AiProviderUnavailableException("Foundry"),
            OperationalIncidentSubsystem.FoundryLlm,
            OperationalIncidentSeverity.Error
        ];
        yield return
        [
            new AiProviderInvalidResponseException("Foundry"),
            OperationalIncidentSubsystem.FoundryLlm,
            OperationalIncidentSeverity.Error
        ];
        yield return
        [
            new ExternalSourcesUnavailableException(),
            OperationalIncidentSubsystem.Application,
            OperationalIncidentSeverity.Error
        ];
        yield return
        [
            new InvalidOperationException("Unhandled."),
            OperationalIncidentSubsystem.Application,
            OperationalIncidentSeverity.Critical
        ];
    }

    [Theory]
    [MemberData(nameof(ExceptionCases))]
    public void Given_AnException_When_Classifying_Then_TheExpectedSubsystemAndSeverityAreReturned(
        Exception exception,
        OperationalIncidentSubsystem expectedSubsystem,
        OperationalIncidentSeverity expectedSeverity)
    {
        // When
        var (subsystem, severity) = OperationalIncidentSubsystemClassifier.Classify(exception);

        // Then
        Assert.Equal(expectedSubsystem, subsystem);
        Assert.Equal(expectedSeverity, severity);
    }
}
