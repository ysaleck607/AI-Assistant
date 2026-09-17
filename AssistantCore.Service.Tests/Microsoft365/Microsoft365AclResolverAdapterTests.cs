using System.Net;
using AssistantCore.ExternalServices.Services.Microsoft;
using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Models.Microsoft365.Permissions;
using AssistantCore.Service.Application.Services.Microsoft365;
using AssistantCore.Service.Infrastructure.Microsoft365;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365AclResolverAdapterTests
{
    [Theory, AutoDomainData]
    public async Task Given_ADriveItem_When_ResolveAsync_Then_UsesGraphAndMapsStableIdentities(
        Guid organizationId,
        Guid userObjectId,
        Guid groupObjectId)
    {
        // Given
        var graphRequestCount = 0;
        var sharePointRequestCount = 0;
        using var identityHttpClient = CreateTokenHttpClient();
        using var graphHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            graphRequestCount++;
            return CreateJsonResponse($$$"""
                {"value":[{
                  "id":"permission-1",
                  "roles":["read"],
                  "grantedToIdentitiesV2":[
                    {"user":{"id":"{{{userObjectId:D}}}","displayName":"Profile name"}},
                    {"group":{"id":"{{{groupObjectId:D}}}","displayName":"Group name"}},
                    {"siteGroup":{"id":"17","displayName":"Site members"}}
                  ]
                }]}
                """);
        }));
        using var sharePointHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            sharePointRequestCount++;
            return CreateJsonResponse("{}");
        }));
        var adapter = CreateAdapter(
            identityHttpClient,
            graphHttpClient,
            sharePointHttpClient,
            new CapturingLogger());
        var organization = CreateOrganization(organizationId);
        var reference = new Microsoft365ContentReference(
            Microsoft365ContentReferenceKind.DriveItem,
            "contoso.sharepoint.com,site-collection-id,web-id",
            "drive-id",
            null,
            "item-id");

        // When
        var result = await adapter.ResolveAsync(organization, reference, CancellationToken.None);

        // Then
        var acl = Assert.IsType<Microsoft365AclResolution.ResolvedAcl>(result).Acl;
        Assert.Equal([userObjectId.ToString("D")], acl.AllowedEntraUserIds);
        Assert.Equal([groupObjectId.ToString("D")], acl.AllowedEntraGroupIds);
        Assert.Equal(
            ["spg:contoso.sharepoint.com,site-collection-id,web-id:17"],
            acl.AllowedSharePointGroupIds);
        Assert.Equal(1, graphRequestCount);
        Assert.Equal(0, sharePointRequestCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_ASiteGroupAndAnUnusableSharePointGroup_When_ResolveAsync_Then_UsesTheSiteGroup(
        Guid organizationId)
    {
        // Given
        using var identityHttpClient = CreateTokenHttpClient();
        using var graphHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            CreateJsonResponse("""
                {"value":[{
                  "id":"permission-1",
                  "roles":["read"],
                  "grantedToV2":{
                    "siteGroup":{"id":"17","displayName":"Site members"},
                    "sharePointGroup":{"id":"unusable-id","displayName":"Site members"}
                  }
                }]}
                """)));
        using var sharePointHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            CreateJsonResponse("{}")));
        var adapter = CreateAdapter(
            identityHttpClient,
            graphHttpClient,
            sharePointHttpClient,
            new CapturingLogger());
        var reference = new Microsoft365ContentReference(
            Microsoft365ContentReferenceKind.DriveItem,
            "contoso.sharepoint.com,site-collection-id,web-id",
            "drive-id",
            null,
            "item-id");

        // When
        var result = await adapter.ResolveAsync(
            CreateOrganization(organizationId),
            reference,
            CancellationToken.None);

        // Then
        var acl = Assert.IsType<Microsoft365AclResolution.ResolvedAcl>(result).Acl;
        Assert.Equal(
            ["spg:contoso.sharepoint.com,site-collection-id,web-id:17"],
            acl.AllowedSharePointGroupIds);
    }

    [Theory, AutoDomainData]
    public async Task Given_AListItem_When_ResolveAsync_Then_UsesSharePointRestAndMapsStableIdentities(
        Guid organizationId,
        Guid listId,
        Guid userObjectId,
        Guid groupObjectId)
    {
        // Given
        string? tokenRequestBody = null;
        var graphRequestCount = 0;
        var sharePointResponses = new Queue<HttpResponseMessage>(
        [
            CreateJsonResponse("{\"HasUniqueRoleAssignments\":true}"),
            CreateJsonResponse($$$"""
                {"value":[
                  {
                    "Member":{"Id":11,"Title":"User profile","PrincipalType":1,"AadObjectId":{"NameId":"{{{userObjectId:D}}}"}},
                    "RoleDefinitionBindings":[{"Id":1,"Name":"Read","RoleTypeKind":2}]
                  },
                  {
                    "Member":{"Id":12,"Title":"Entra group","PrincipalType":4,"AadObjectId":{"NameId":"{{{groupObjectId:D}}}"}},
                    "RoleDefinitionBindings":[{"Id":2,"Name":"Contribute","RoleTypeKind":3}]
                  },
                  {
                    "Member":{"Id":17,"Title":"Site members","PrincipalType":8},
                    "RoleDefinitionBindings":[{"Id":3,"Name":"Read","RoleTypeKind":2}]
                  }
                ]}
                """)
        ]);
        using var identityHttpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            tokenRequestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return CreateTokenResponse();
        }));
        using var graphHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            graphRequestCount++;
            return CreateJsonResponse("{}");
        }));
        using var sharePointHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            sharePointResponses.Dequeue()));
        var adapter = CreateAdapter(
            identityHttpClient,
            graphHttpClient,
            sharePointHttpClient,
            new CapturingLogger());
        var reference = new Microsoft365ContentReference(
            Microsoft365ContentReferenceKind.ListItem,
            "contoso.sharepoint.com,site-collection-id,web-id",
            null,
            listId.ToString("D"),
            "42",
            "https://contoso.sharepoint.com/sites/engineering");

        // When
        var result = await adapter.ResolveAsync(
            CreateOrganization(organizationId),
            reference,
            CancellationToken.None);

        // Then
        var acl = Assert.IsType<Microsoft365AclResolution.ResolvedAcl>(result).Acl;
        Assert.Equal([userObjectId.ToString("D")], acl.AllowedEntraUserIds);
        Assert.Equal([groupObjectId.ToString("D")], acl.AllowedEntraGroupIds);
        Assert.Equal(
            ["spg:contoso.sharepoint.com,site-collection-id,web-id:17"],
            acl.AllowedSharePointGroupIds);
        Assert.Equal(Microsoft365AclInheritance.Unique, acl.Inheritance);
        Assert.Contains(
            "scope=https%3A%2F%2Fcontoso.sharepoint.com%2F.default",
            tokenRequestBody,
            StringComparison.Ordinal);
        Assert.Equal(0, graphRequestCount);
        Assert.Empty(sharePointResponses);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnOrganizationLinkAndAnExplicitUser_When_ResolveAsync_Then_PreservesTheExplicitGrant(
        Guid organizationId,
        Guid userObjectId)
    {
        // Given
        using var identityHttpClient = CreateTokenHttpClient();
        using var graphHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            CreateJsonResponse($$$"""
                {"value":[
                  {
                    "id":"organization-link",
                    "roles":["write"],
                    "link":{"type":"edit","scope":"organization"}
                  },
                  {
                    "id":"user-grant",
                    "roles":["owner"],
                    "grantedToV2":{"user":{"id":"{{{userObjectId:D}}}"}}
                  }
                ]}
                """)));
        using var sharePointHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            CreateJsonResponse("{}")));
        var adapter = CreateAdapter(
            identityHttpClient,
            graphHttpClient,
            sharePointHttpClient,
            new CapturingLogger());

        // When
        var result = await adapter.ResolveAsync(
            CreateOrganization(organizationId),
            CreateDriveItemReference(),
            CancellationToken.None);

        // Then
        var acl = Assert.IsType<Microsoft365AclResolution.ResolvedAcl>(result).Acl;
        Assert.True(acl.HasOrganizationLink);
        Assert.False(acl.HasAnonymousLink);
        Assert.Equal([userObjectId.ToString("D")], acl.AllowedEntraUserIds);
    }

    [Theory, AutoDomainData]
    public async Task Given_AUsersLinkWithAStableRecipient_When_ResolveAsync_Then_MapsTheRecipient(
        Guid organizationId,
        Guid userObjectId)
    {
        // Given
        using var identityHttpClient = CreateTokenHttpClient();
        using var graphHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            CreateJsonResponse($$$"""
                {"value":[{
                  "id":"users-link",
                  "roles":["read"],
                  "link":{"type":"view","scope":"users"},
                  "grantedToIdentitiesV2":[{"user":{"id":"{{{userObjectId:D}}}"}}]
                }]}
                """)));
        using var sharePointHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            CreateJsonResponse("{}")));
        var adapter = CreateAdapter(
            identityHttpClient,
            graphHttpClient,
            sharePointHttpClient,
            new CapturingLogger());

        // When
        var result = await adapter.ResolveAsync(
            CreateOrganization(organizationId),
            CreateDriveItemReference(),
            CancellationToken.None);

        // Then
        var acl = Assert.IsType<Microsoft365AclResolution.ResolvedAcl>(result).Acl;
        Assert.Equal([userObjectId.ToString("D")], acl.AllowedEntraUserIds);
        Assert.False(acl.HasOrganizationLink);
        Assert.False(acl.HasAnonymousLink);
    }

    [Theory, AutoDomainData]
    public async Task Given_AGroupOwnersClaimAndAnExplicitUser_When_ResolveAsync_Then_KeepsOwnerScopedPrincipal(
        Guid organizationId,
        Guid groupObjectId,
        Guid userObjectId)
    {
        // Given
        using var identityHttpClient = CreateTokenHttpClient();
        using var graphHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            CreateJsonResponse($$$"""
                {"value":[
                  {
                    "id":"group-owners-grant",
                    "roles":["owner"],
                    "grantedToV2":{
                      "group":{"id":"{{{groupObjectId:D}}}"},
                      "siteUser":{
                        "id":"6",
                        "loginName":"c:0o.c|federateddirectoryclaimprovider|{{{groupObjectId:D}}}_o"
                      }
                    }
                  },
                  {
                    "id":"user-grant",
                    "roles":["owner"],
                    "grantedToV2":{"user":{"id":"{{{userObjectId:D}}}"}}
                  }
                ]}
                """)));
        using var sharePointHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            CreateJsonResponse("{}")));
        var adapter = CreateAdapter(
            identityHttpClient,
            graphHttpClient,
            sharePointHttpClient,
            new CapturingLogger());

        // When
        var result = await adapter.ResolveAsync(
            CreateOrganization(organizationId),
            CreateDriveItemReference(),
            CancellationToken.None);

        // Then
        var acl = Assert.IsType<Microsoft365AclResolution.ResolvedAcl>(result).Acl;
        Assert.Equal([userObjectId.ToString("D")], acl.AllowedEntraUserIds);
        Assert.Equal(
            [$"{Microsoft365SecurityIdentityNormalizer.EntraGroupOwnerPrefix}{groupObjectId:D}"],
            acl.AllowedEntraGroupIds);
    }

    [Theory, AutoDomainData]
    public async Task Given_OnlyAGroupOwnersClaim_When_ResolveAsync_Then_ReturnsOwnerScopedPrincipal(
        Guid organizationId,
        Guid groupObjectId)
    {
        // Given
        using var identityHttpClient = CreateTokenHttpClient();
        using var graphHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            CreateJsonResponse($$$"""
                {"value":[{
                  "id":"group-owners-grant",
                  "roles":["owner"],
                  "grantedToV2":{
                    "group":{"id":"{{{groupObjectId:D}}}"},
                    "siteUser":{
                      "id":"6",
                      "loginName":"c:0o.c|federateddirectoryclaimprovider|{{{groupObjectId:D}}}_o"
                    }
                  }
                }]}
                """)));
        using var sharePointHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            CreateJsonResponse("{}")));
        var adapter = CreateAdapter(
            identityHttpClient,
            graphHttpClient,
            sharePointHttpClient,
            new CapturingLogger());

        // When
        var result = await adapter.ResolveAsync(
            CreateOrganization(organizationId),
            CreateDriveItemReference(),
            CancellationToken.None);

        // Then
        var acl = Assert.IsType<Microsoft365AclResolution.ResolvedAcl>(result).Acl;
        Assert.Equal(
            [$"{Microsoft365SecurityIdentityNormalizer.EntraGroupOwnerPrefix}{groupObjectId:D}"],
            acl.AllowedEntraGroupIds);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnUnsupportedGrantAndAStableUser_When_ResolveAsync_Then_UsesOnlyTheStableGrant(
        Guid organizationId,
        Guid userObjectId)
    {
        // Given
        using var identityHttpClient = CreateTokenHttpClient();
        using var graphHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            CreateJsonResponse("""
                {"value":[
                  {"id":"future-link","roles":["read"],"link":{"scope":"future-scope"}},
                  {"id":"user-grant","roles":["read"],"grantedToV2":{"user":{"id":"USER_OBJECT_ID"}
                  }
                  }
                ]}
                """.Replace(
                    "USER_OBJECT_ID",
                    userObjectId.ToString("D"),
                    StringComparison.Ordinal))));
        using var sharePointHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            CreateJsonResponse("{}")));
        var adapter = CreateAdapter(
            identityHttpClient,
            graphHttpClient,
            sharePointHttpClient,
            new CapturingLogger());

        // When
        var result = await adapter.ResolveAsync(
            CreateOrganization(organizationId),
            CreateDriveItemReference(),
            CancellationToken.None);

        // Then
        var acl = Assert.IsType<Microsoft365AclResolution.ResolvedAcl>(result).Acl;
        Assert.Equal([userObjectId.ToString("D")], acl.AllowedEntraUserIds);
        Assert.False(acl.HasOrganizationLink);
        Assert.False(acl.HasAnonymousLink);
    }

    [Theory, AutoDomainData]
    public async Task Given_OnlyAnUnsupportedGrant_When_ResolveAsync_Then_ReturnsUnsupportedPermission(
        Guid organizationId)
    {
        // Given
        using var identityHttpClient = CreateTokenHttpClient();
        using var graphHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            CreateJsonResponse("""
                {"value":[{
                  "id":"future-link",
                  "roles":["read"],
                  "link":{"scope":"future-scope"}
                }]}
                """)));
        using var sharePointHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            CreateJsonResponse("{}")));
        var adapter = CreateAdapter(
            identityHttpClient,
            graphHttpClient,
            sharePointHttpClient,
            new CapturingLogger());

        // When
        var result = await adapter.ResolveAsync(
            CreateOrganization(organizationId),
            CreateDriveItemReference(),
            CancellationToken.None);

        // Then
        var unresolved = Assert.IsType<Microsoft365AclResolution.Unresolved>(result);
        Assert.Equal(Microsoft365AclResolutionFailureReason.UnsupportedPermission, unresolved.Reason);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnUnrepresentablePrincipal_When_ResolveAsync_Then_ReturnsUnresolvedWithoutLoggingSecrets(
        Guid organizationId)
    {
        // Given
        const string accessToken = "sensitive-access-token";
        const string responseSecret = "sensitive-response-profile";
        var logger = new CapturingLogger();
        using var identityHttpClient = CreateTokenHttpClient(accessToken);
        using var graphHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            CreateJsonResponse($$$"""
                {"value":[{
                  "id":"permission-1",
                  "roles":["read"],
                  "grantedToV2":{"siteUser":{"id":"7","displayName":"{{{responseSecret}}}","loginName":"user@example.com"}}
                }]}
                """)));
        using var sharePointHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            CreateJsonResponse("{}")));
        var adapter = CreateAdapter(identityHttpClient, graphHttpClient, sharePointHttpClient, logger);
        var reference = new Microsoft365ContentReference(
            Microsoft365ContentReferenceKind.DriveItem,
            "site-id",
            "drive-id",
            null,
            "item-id");

        // When
        var result = await adapter.ResolveAsync(
            CreateOrganization(organizationId),
            reference,
            CancellationToken.None);

        // Then
        var unresolved = Assert.IsType<Microsoft365AclResolution.Unresolved>(result);
        Assert.Equal(Microsoft365AclResolutionFailureReason.UnknownPrincipal, unresolved.Reason);
        var loggedContent = string.Join(" ", logger.Messages);
        Assert.DoesNotContain(accessToken, loggedContent, StringComparison.Ordinal);
        Assert.DoesNotContain(responseSecret, loggedContent, StringComparison.Ordinal);
        Assert.DoesNotContain("user@example.com", loggedContent, StringComparison.Ordinal);
        Assert.Contains(nameof(Microsoft365AclResolutionFailureReason.UnknownPrincipal), loggedContent);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnUnredeemedGuestInvitation_When_ResolveAsync_Then_GrantsNoAccessWithoutLoggingTheEmail(
        Guid organizationId)
    {
        // Given
        // A share sent to an email address that has not yet accepted the guest invitation has
        // no Entra Object ID yet - Graph represents this as a "user" identity with a display
        // name/email but no id. It must never be silently treated as a valid, matchable
        // principal, and the email must never reach a log line.
        const string invitedEmail = "invited-guest@external.example";
        var logger = new CapturingLogger();
        using var identityHttpClient = CreateTokenHttpClient();
        using var graphHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            CreateJsonResponse($$$"""
                {"value":[{
                  "id":"permission-1",
                  "roles":["read"],
                  "grantedToIdentitiesV2":[
                    {"user":{"displayName":"{{{invitedEmail}}}"}}
                  ]
                }]}
                """)));
        using var sharePointHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            CreateJsonResponse("{}")));
        var adapter = CreateAdapter(identityHttpClient, graphHttpClient, sharePointHttpClient, logger);

        // When
        var result = await adapter.ResolveAsync(
            CreateOrganization(organizationId),
            CreateDriveItemReference(),
            CancellationToken.None);

        // Then
        var unresolved = Assert.IsType<Microsoft365AclResolution.Unresolved>(result);
        Assert.Equal(Microsoft365AclResolutionFailureReason.UnknownPrincipal, unresolved.Reason);
        Assert.DoesNotContain(invitedEmail, string.Join(" ", logger.Messages), StringComparison.Ordinal);
    }

    private static Microsoft365AclResolverAdapter CreateAdapter(
        HttpClient identityHttpClient,
        HttpClient graphHttpClient,
        HttpClient sharePointHttpClient,
        ILogger<Microsoft365AclResolverAdapter> logger) => new(
        new MicrosoftIdentityClient(identityHttpClient),
        new MicrosoftGraphDriveItemPermissionClient(graphHttpClient),
        new MicrosoftSharePointListItemPermissionClient(sharePointHttpClient),
        new Microsoft365SecurityIdentityNormalizer(),
        new Microsoft365PermissionRoleEvaluator(),
        Options.Create(new Microsoft365Options
        {
            AuthorityBaseUrl = "https://login.microsoftonline.com",
            GraphBaseUrl = "https://graph.microsoft.com",
            ClientId = "client-id",
            ClientSecret = "client-secret"
        }),
        logger);

    private static Organization CreateOrganization(Guid organizationId) => new()
    {
        Id = organizationId,
        ExternalTenantId = "tenant-id"
    };

    private static Microsoft365ContentReference CreateDriveItemReference() => new(
        Microsoft365ContentReferenceKind.DriveItem,
        "contoso.sharepoint.com,site-collection-id,web-id",
        "drive-id",
        null,
        "item-id");

    private static HttpClient CreateTokenHttpClient(string accessToken = "access-token") =>
        new(new StubHttpMessageHandler(_ => CreateTokenResponse(accessToken)));

    private static HttpResponseMessage CreateTokenResponse(string accessToken = "access-token") =>
        CreateJsonResponse($"{{\"access_token\":\"{accessToken}\",\"expires_in\":3600}}");

    private static HttpResponseMessage CreateJsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private sealed class CapturingLogger : ILogger<Microsoft365AclResolverAdapter>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
