using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AssistantCore.ExternalServices.Entities.Azure;
using AssistantCore.ExternalServices.Services.Azure;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class AzureAiSearchPassageAclClientTests
{
    [Theory, AutoDomainData]
    public async Task Given_APassageWithResolvedAcl_When_MergeOrUploadAsync_Then_WritesEverySecurityField(
        string apiKey,
        string chunkId,
        Guid organizationId,
        string title,
        string content,
        string userId,
        string groupId,
        string sharePointGroupId,
        string fingerprint)
    {
        // Given
        string? payload = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[{\"key\":\"chunk\",\"status\":true,\"statusCode\":201}]}")
            };
        }));
        var client = new AzureAiSearchPassageAclClient(httpClient);
        var passage = new AzureAiSearchPassageDocument(
            chunkId,
            organizationId.ToString("D"),
            title,
            content,
            [userId],
            [groupId],
            [sharePointGroupId],
            true,
            true,
            fingerprint,
            false);

        // When
        await client.MergeOrUploadAsync(
            "https://search.example",
            "content-index",
            apiKey,
            [passage]);

        // Then
        using var document = JsonDocument.Parse(payload!);
        var action = document.RootElement.GetProperty("value").EnumerateArray().Single();
        Assert.Equal("mergeOrUpload", action.GetProperty("@search.action").GetString());
        Assert.Equal(organizationId.ToString("D"), action.GetProperty("organizationId").GetString());
        Assert.Equal("sharepoint", action.GetProperty("sourceType").GetString());
        Assert.Equal(userId, action.GetProperty("allowedUserIds").EnumerateArray().Single().GetString());
        Assert.Equal(groupId, action.GetProperty("allowedGroupIds").EnumerateArray().Single().GetString());
        Assert.Equal(
            sharePointGroupId,
            action.GetProperty("allowedSharePointGroupIds").EnumerateArray().Single().GetString());
        Assert.True(action.GetProperty("hasAnonymousLink").GetBoolean());
        Assert.True(action.GetProperty("hasOrganizationLink").GetBoolean());
        Assert.False(action.GetProperty("isAvailable").GetBoolean());
    }

    [Theory, AutoDomainData]
    public void Given_TheMicrosoft365IndexDefinition_When_CreateFields_Then_AclFieldsAreFilterableAndNotRetrievable(
        string unusedValue)
    {
        // Given
        var securityFieldNames = new[]
        {
            "organizationId",
            "allowedUserIds",
            "allowedGroupIds",
            "allowedSharePointGroupIds",
            "hasAnonymousLink",
            "hasOrganizationLink"
        };

        // When
        var fields = AzureAiSearchMicrosoft365IndexDefinition.CreateFields();

        // Then
        Assert.False(string.IsNullOrWhiteSpace(unusedValue));
        Assert.All(
            fields.Where(field => securityFieldNames.Contains(field.Name, StringComparer.Ordinal)),
            field =>
            {
                Assert.True(field.Filterable);
                Assert.False(field.Retrievable);
            });
        Assert.Equal(
            securityFieldNames.OrderBy(name => name, StringComparer.Ordinal),
            fields
                .Where(field => securityFieldNames.Contains(field.Name, StringComparer.Ordinal))
                .Select(field => field.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    [Theory, AutoDomainData]
    public async Task Given_AResolvedAcl_When_UpdateAclAsync_Then_MergesSecurityFieldsWhileKeepingPassageUnavailable(
        string apiKey,
        string chunkId,
        string userId,
        string groupId,
        string sharePointGroupId,
        string fingerprint)
    {
        // Given
        HttpRequestMessage? receivedRequest = null;
        string? payload = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            receivedRequest = request;
            payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[{\"key\":\"chunk\",\"status\":true,\"statusCode\":200}]}" )
            };
        }));
        var client = new AzureAiSearchPassageAclClient(httpClient);
        var update = new AzureAiSearchPassageAclUpdate(
            chunkId,
            [userId],
            [groupId],
            [sharePointGroupId],
            false,
            true,
            fingerprint);

        // When
        await client.UpdateAclAsync(
            "https://search.example",
            "content-index",
            apiKey,
            [update]);

        // Then
        Assert.Equal(HttpMethod.Post, receivedRequest?.Method);
        Assert.Contains("api-version=2025-09-01", receivedRequest?.RequestUri?.Query, StringComparison.Ordinal);
        Assert.Equal(apiKey, Assert.Single(receivedRequest!.Headers.GetValues("api-key")));
        using var document = JsonDocument.Parse(payload!);
        var action = document.RootElement.GetProperty("value").EnumerateArray().Single();
        Assert.Equal("merge", action.GetProperty("@search.action").GetString());
        Assert.Equal(chunkId, action.GetProperty("chunkId").GetString());
        Assert.Equal(fingerprint, action.GetProperty("aclFingerprint").GetString());
        Assert.True(action.GetProperty("hasOrganizationLink").GetBoolean());
        Assert.False(action.GetProperty("isAvailable").GetBoolean());
        Assert.Equal(userId, action.GetProperty("allowedUserIds").EnumerateArray().Single().GetString());
    }

    [Theory, AutoDomainData]
    public async Task Given_ARejectedPassageUpdate_When_SetAvailabilityAsync_Then_ThrowsExternalException(
        string apiKey,
        string chunkId)
    {
        // Given
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[{\"key\":\"chunk\",\"status\":false,\"statusCode\":400}]}" )
            }));
        var client = new AzureAiSearchPassageAclClient(httpClient);

        // When
        var action = () => client.SetAvailabilityAsync(
            "https://search.example",
            "content-index",
            apiKey,
            [chunkId],
            false);

        // Then
        await Assert.ThrowsAsync<AzureAiSearchExternalException>(action);
    }

    [Theory, AutoDomainData]
    public async Task Given_ChunkIdsAndOrganization_When_DeleteAsync_Then_DeletesOnlyMatchingOrganizationChunks(
        string apiKey,
        Guid organizationId,
        string chunkId)
    {
        // Given
        var requests = new List<(string Path, string Body)>();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            requests.Add((request.RequestUri!.AbsolutePath, body));
            return request.RequestUri.AbsolutePath.EndsWith("/search", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{{\"value\":[{{\"chunkId\":\"{chunkId}\"}}]}}")
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"value\":[{\"key\":\"chunk\",\"status\":true}]}")
                };
        }));
        var client = new AzureAiSearchPassageAclClient(httpClient);

        // When
        await client.DeleteAsync(
            "https://search.example",
            "content-index",
            apiKey,
            organizationId,
            [chunkId]);

        // Then
        Assert.Equal(2, requests.Count);
        using var searchPayload = JsonDocument.Parse(requests[0].Body);
        var filter = searchPayload.RootElement.GetProperty("filter").GetString()!;
        Assert.Contains($"organizationId eq '{organizationId:D}'", filter, StringComparison.Ordinal);
        Assert.Contains(chunkId, filter, StringComparison.Ordinal);
        using var deletePayload = JsonDocument.Parse(requests[1].Body);
        var action = deletePayload.RootElement.GetProperty("value").EnumerateArray().Single();
        Assert.Equal("delete", action.GetProperty("@search.action").GetString());
        Assert.Equal(chunkId, action.GetProperty("chunkId").GetString());
    }

    [Theory, AutoDomainData]
    public async Task Given_ATransientAzureResponse_When_MergeOrUploadAsync_Then_RetriesTheBatch(
        string apiKey,
        string chunkId,
        Guid organizationId,
        string title,
        string content)
    {
        // Given
        var requestCount = 0;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            requestCount++;
            if (requestCount < 2)
            {
                var retry = new HttpResponseMessage((HttpStatusCode)429);
                retry.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                return retry;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[{\"key\":\"chunk\",\"status\":true}]}")
            };
        }));
        var client = new AzureAiSearchPassageAclClient(httpClient);
        var passage = new AzureAiSearchPassageDocument(
            chunkId,
            organizationId.ToString("D"),
            title,
            content,
            [],
            [],
            [],
            false,
            false,
            new string('a', 64),
            false);

        // When
        await client.MergeOrUploadAsync(
            "https://search.example",
            "content-index",
            apiKey,
            [passage]);

        // Then
        Assert.Equal(2, requestCount);
    }
}
