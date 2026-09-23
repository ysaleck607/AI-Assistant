using AssistantCore.Service.Application.Models.Messages.Connectors.Microsoft365;
using AssistantCore.Service.Infrastructure.Connectors.Microsoft365;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365SearchFilterBuilderTests
{
    [Theory, AutoDomainData]
    public void Given_OnlyADirectUserGrant_When_BuildSecurityFilter_Then_FiltersByTheUserObjectIdAlone(
        Guid organizationId,
        Guid entraUserId)
    {
        // Given
        var securityContext = new Microsoft365SearchSecurityContext(
            organizationId,
            entraUserId.ToString("D"),
            []);

        // When
        var filter = Microsoft365SearchFilterBuilder.BuildSecurityFilter(securityContext);

        // Then
        Assert.Contains($"allowedUserIds/any(id: id eq '{entraUserId:D}')", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("allowedGroupIds", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("allowedSharePointGroupIds", filter, StringComparison.Ordinal);
    }

    [Theory, AutoDomainData]
    public void Given_AnEntraGroupMembership_When_BuildSecurityFilter_Then_AlsoFiltersByTheGroupObjectId(
        Guid organizationId,
        Guid entraUserId,
        Guid entraGroupId)
    {
        // Given: access granted only through group membership - the exact shape of a guest
        // who can see a document because of a group grant, never a direct per-user one.
        var securityContext = new Microsoft365SearchSecurityContext(
            organizationId,
            entraUserId.ToString("D"),
            [entraGroupId.ToString("D")]);

        // When
        var filter = Microsoft365SearchFilterBuilder.BuildSecurityFilter(securityContext);

        // Then
        Assert.Contains(
            $"allowedGroupIds/any(id: search.in(id, '{entraGroupId:D}', ','))",
            filter,
            StringComparison.Ordinal);
    }

    [Theory, AutoDomainData]
    public void Given_ASharePointLocalGroupMembership_When_BuildSecurityFilter_Then_AlsoFiltersBySharePointGroupId(
        Guid organizationId,
        Guid entraUserId,
        string siteId,
        string sharePointGroupId)
    {
        // Given
        var securityContext = new Microsoft365SearchSecurityContext(
            organizationId,
            entraUserId.ToString("D"),
            [],
            [$"spg:{siteId}:{sharePointGroupId}"]);

        // When
        var filter = Microsoft365SearchFilterBuilder.BuildSecurityFilter(securityContext);

        // Then
        Assert.Contains(
            $"allowedSharePointGroupIds/any(id: search.in(id, 'spg:{siteId}:{sharePointGroupId}', '|'))",
            filter,
            StringComparison.Ordinal);
    }

    [Theory, AutoDomainData]
    public void Given_AGuestAndAnInternalMemberInTheSameGroup_When_BuildSecurityFilter_Then_ProducesIdenticalGroupFiltering(
        Guid organizationId,
        Guid guestEntraUserId,
        Guid memberEntraUserId,
        Guid sharedGroupId)
    {
        // Given: the filter must not know or care whether a userId belongs to a guest or an
        // internal member - the ticket's "recognize userType Guest without changing the
        // security identifier" requirement, proven at the query layer.
        var guestContext = new Microsoft365SearchSecurityContext(
            organizationId,
            guestEntraUserId.ToString("D"),
            [sharedGroupId.ToString("D")]);
        var memberContext = new Microsoft365SearchSecurityContext(
            organizationId,
            memberEntraUserId.ToString("D"),
            [sharedGroupId.ToString("D")]);

        // When
        var guestFilter = Microsoft365SearchFilterBuilder.BuildSecurityFilter(guestContext);
        var memberFilter = Microsoft365SearchFilterBuilder.BuildSecurityFilter(memberContext);

        // Then
        var guestGroupClause = ExtractGroupClause(guestFilter);
        var memberGroupClause = ExtractGroupClause(memberFilter);
        Assert.Equal(memberGroupClause, guestGroupClause);
    }

    [Theory, AutoDomainData]
    public void Given_AnEmptyEntraUserId_When_BuildSecurityFilter_Then_Throws(
        Guid organizationId)
    {
        // Given
        var securityContext = new Microsoft365SearchSecurityContext(
            organizationId,
            string.Empty,
            []);

        // When
        var action = () => Microsoft365SearchFilterBuilder.BuildSecurityFilter(securityContext);

        // Then
        Assert.Throws<ArgumentException>(action);
    }

    private static string ExtractGroupClause(string filter)
    {
        const string marker = "allowedGroupIds/any(id: search.in(id, '";
        var start = filter.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected filter to contain a group clause: {filter}");
        var end = filter.IndexOf("'", start + marker.Length, StringComparison.Ordinal);
        return filter[(start + marker.Length)..end];
    }
}
