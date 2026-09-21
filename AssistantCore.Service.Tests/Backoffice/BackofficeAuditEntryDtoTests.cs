using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Queries;
using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Tests.Backoffice;

public sealed class BackofficeAuditEntryDtoTests
{
    [Theory, AutoDomainData]
    public void Given_OldAndNewValues_When_FromData_Then_BuildsOneChangePerField(
        Guid id,
        DateTimeOffset occurredAt,
        Guid actorId,
        Guid organizationId,
        Guid targetId,
        string correlationId)
    {
        // Given
        var data = new BackofficeAuditEntryData(
            id,
            occurredAt,
            AdministrativeAuditAction.MemberRoleChanged,
            actorId,
            organizationId,
            "MetalPro",
            "Member",
            targetId,
            correlationId,
            OldValuesJson: """{"role":"User"}""",
            NewValuesJson: """{"role":"Admin"}""");

        // When
        var dto = BackofficeAuditEntryDto.FromData(data);

        // Then
        var change = Assert.Single(dto.Changes);
        Assert.Equal("role", change.Field);
        Assert.Equal("User", change.Before);
        Assert.Equal("Admin", change.After);
        Assert.Equal("MemberRoleChanged", dto.Action);
        Assert.Equal("Succeeded", dto.Result);
        Assert.Null(dto.ActorEmail);
    }

    [Theory, AutoDomainData]
    public void Given_AFieldOnlyPresentInNewValues_When_FromData_Then_BeforeIsNull(
        Guid id,
        DateTimeOffset occurredAt,
        Guid actorId,
        Guid organizationId,
        Guid targetId,
        string correlationId)
    {
        // Given
        var data = new BackofficeAuditEntryData(
            id,
            occurredAt,
            AdministrativeAuditAction.ConnectorStatusChanged,
            actorId,
            organizationId,
            "MetalPro",
            "Microsoft365Connection",
            targetId,
            correlationId,
            OldValuesJson: "{}",
            NewValuesJson: """{"isIndexed":true}""");

        // When
        var dto = BackofficeAuditEntryDto.FromData(data);

        // Then
        var change = Assert.Single(dto.Changes);
        Assert.Equal("isIndexed", change.Field);
        Assert.Null(change.Before);
        Assert.Equal("True", change.After);
    }

    [Theory, AutoDomainData]
    public void Given_EmptyValues_When_FromData_Then_ChangesIsEmpty(
        Guid id,
        DateTimeOffset occurredAt,
        Guid actorId,
        Guid organizationId,
        Guid targetId,
        string correlationId)
    {
        // Given
        var data = new BackofficeAuditEntryData(
            id,
            occurredAt,
            AdministrativeAuditAction.UserAccessReevaluated,
            actorId,
            organizationId,
            "MetalPro",
            "Member",
            targetId,
            correlationId,
            OldValuesJson: "{}",
            NewValuesJson: "{}");

        // When
        var dto = BackofficeAuditEntryDto.FromData(data);

        // Then
        Assert.Empty(dto.Changes);
    }
}
