using AssistantCore.ExternalServices.Services.Microsoft;
using AssistantCore.ExternalServices.Services.Azure;
using AssistantCore.ExternalServices.Services.OpenAI;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Services.Microsoft365;
using AssistantCore.Service.Application.Services.Messages.Connectors.Microsoft365;
using AssistantCore.Service.Application.Services.Messages.Tabular;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace AssistantCore.Service.Infrastructure.Microsoft365;

public static class Microsoft365ServiceCollectionExtensions
{
    private static readonly string[] SensitiveHttpHeaders =
        ["Authorization", "Cookie", "Set-Cookie"];

    public static IServiceCollection AddMicrosoft365Infrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<Microsoft365Options>()
            .Bind(configuration.GetSection(Microsoft365Options.SectionName))
            .Validate(options =>
                    IsHttpsUrl(options.AuthorityBaseUrl)
                    && IsHttpsUrl(options.GraphBaseUrl)
                    && Guid.TryParse(options.ClientId, out var clientId)
                    && clientId != Guid.Empty
                    && !string.IsNullOrWhiteSpace(options.ClientSecret)
                    && options.ClientStateHmacKey.Length >= 32
                    && HasValidSharePointCertificateConfiguration(options)
                    && IsHttpsUrl(options.ConsentCallbackUrl)
                    && IsSecureOrLoopbackUrl(options.ConsentSuccessRedirectUrl)
                    && IsSecureOrLoopbackUrl(options.ConsentErrorRedirectUrl)
                    && IsHttpsUrl(options.WebhookBaseUrl)
                    && options.ConsentStateLifetimeMinutes is > 0 and <= 60
                    && options.SubscriptionLifetimeHours is > 1 and <= 48
                    && options.SubscriptionRenewalLeadTimeHours > 0
                    && options.SubscriptionRenewalLeadTimeHours < options.SubscriptionLifetimeHours
                    && options.SynchronizationLeaseMinutes is > 0 and <= 60
                    && options.SynchronizationIntervalMinutes > 0
                    && options.AclReconciliationIntervalMinutes > 0
                    && options.AclReconciliationRetryMinutes > 0
                    && options.AclReconciliationBatchSize is > 0 and <= 1000
                    && options.MaximumExtractionFileSizeBytes > 0
                    && options.MaximumExtractionExpandedSizeBytes >= options.MaximumExtractionFileSizeBytes
                    && options.MaximumExtractedCharacters > 0
                    && options.MaximumArchiveCompressedSizeBytes > 0
                    && options.MaximumArchiveExpandedSizeBytes > 0
                    && options.MaximumArchiveEntries > 0
                    && options.MaximumArchiveDepth >= 0
                    && double.IsFinite(options.MaximumArchiveCompressionRatio)
                    && options.MaximumArchiveCompressionRatio > 0
                    && options.ArchiveExtractionTimeoutSeconds > 0
                    && options.MaximumExcelSheets > 0
                    && options.MaximumExcelCells > 0
                    && options.MaximumPowerPointSlides > 0
                    && options.SharePointGroupCacheMinutes > 0
                    && options.ChunkMaximumTokens > 0
                    && options.ChunkOverlapTokens >= 0
                    && options.ChunkOverlapTokens < options.ChunkMaximumTokens
                    && options.MaximumChunksPerDocument > 0
                    && IsHttpsUrl(options.EmbeddingEndpoint)
                    && !string.IsNullOrWhiteSpace(options.EmbeddingModel)
                    && HasValidAzureOpenAiEmbeddingConfiguration(options)
                    && options.EmbeddingDimensions > 0
                    && options.EmbeddingBatchSize is > 0 and <= 2048
                    && options.DocumentWorkLeaseMinutes > 0
                    && options.DocumentWorkRetryMinutes > 0
                    && options.DocumentWorkMaximumAttempts > 0,
                "Microsoft365 requires HTTPS URLs, credentials, a client-state HMAC key of at least 32 characters, paired SharePoint certificate settings, valid lifetimes, and valid limits.")
            .ValidateOnStart();

        services.AddOptions<ServiceBusOptions>()
            .Bind(configuration.GetSection(ServiceBusOptions.SectionName))
            .Validate(options =>
                    !options.Enabled
                    || (IsServiceBusNamespace(options.FullyQualifiedNamespace)
                        && !string.IsNullOrWhiteSpace(options.DriveSyncQueue)
                        && !string.IsNullOrWhiteSpace(options.ListSyncQueue)),
                "ServiceBus requires a valid namespace and queue names when enabled.")
            .ValidateOnStart();

        services.AddOptions<AzureAiSearchOptions>()
            .Bind(configuration.GetSection(AzureAiSearchOptions.SectionName))
            .Validate(options =>
                    options.VectorSearchMetric == "cosine"
                    && IsSupportedReasoningEffort(options.KnowledgeBaseRetrievalReasoningEffort)
                    && HasValidPlanningModelConfiguration(options)
                    && IsAzureSearchKnowledgeResourceName(options.VectorizerName)
                    && double.IsFinite(options.MinimumSemanticRelevanceScore)
                    && options.MinimumSemanticRelevanceScore is >= 0d and <= 4d
                    && options.KnowledgeBaseMaxRuntimeInSeconds > 0
                    && (options.KnowledgeBaseMaxOutputSizeInTokens is null or > 0)
                    && IsAzureSearchKnowledgeResourceName(options.KnowledgeSourceName)
                    && IsAzureSearchKnowledgeResourceName(options.KnowledgeBaseName),
                "AzureSearch requires cosine similarity, minimal, low or auto retrieval reasoning, a planning model for low or auto reasoning, valid semantic relevance, knowledge base limits and knowledge resource names.")
            .ValidateOnStart();

        services.AddDataProtection();
        services.AddMemoryCache();
        AddProtectedHttpClient<MicrosoftIdentityClient>(services);
        services.AddHttpClient<MicrosoftGraphClient>();
        services.AddHttpClient<MicrosoftGraphListSchemaClient>();
        services.AddHttpClient<MicrosoftGraphListItemDeltaClient>();
        services.AddHttpClient<MicrosoftGraphDriveItemDeltaClient>();
        services.AddHttpClient<MicrosoftGraphSharedDriveItemSearchClient>();
        AddProtectedHttpClient<MicrosoftGraphDriveContentClient>(services);
        services.AddHttpClient<MicrosoftGraphSiteSourcesClient>();
        AddProtectedHttpClient<MicrosoftGraphSiteClient>(services);
        services.AddHttpClient<MicrosoftGraphSubscriptionClient>();
        AddProtectedHttpClient<MicrosoftGraphUserGroupClient>(services);
        AddProtectedHttpClient<MicrosoftGraphUserProfileClient>(services);
        AddProtectedHttpClient<MicrosoftGraphDriveItemPermissionClient>(services);
        AddProtectedHttpClient<MicrosoftSharePointListItemPermissionClient>(services);
        services.AddSingleton<MicrosoftWordContentExtractorClient>();
        services.AddSingleton<MicrosoftExcelContentExtractorClient>();
        services.AddSingleton<MicrosoftExcelTableReaderClient>();
        services.AddSingleton<MicrosoftPdfContentExtractorClient>();
        services.AddSingleton<MicrosoftPowerPointContentExtractorClient>();
        services.AddSingleton<MicrosoftArchiveContentExtractorClient>();
        services.AddSingleton<MicrosoftCertificateIdentityClient>();
        AddProtectedHttpClient<MicrosoftSharePointUserGroupClient>(services);
        services.AddHttpClient<AzureAiSearchPassageAclClient>()
            .RedactLoggedHeaders(["api-key", "Authorization"]);
        services.AddHttpClient<AzureAiSearchKnowledgeBaseRetrievalClient>()
            .RedactLoggedHeaders(["api-key", "Authorization"]);
        services.AddHttpClient<AzureAiSearchIndexClient>()
            .RedactLoggedHeaders(["api-key", "Authorization"]);
        services.AddHttpClient<OpenAiEmbeddingsClient>()
            .RedactLoggedHeaders(["api-key", "Authorization"]);
        services.AddHttpClient<MicrosoftVisionReadClient>()
            .RedactLoggedHeaders(["Ocp-Apim-Subscription-Key"]);
        services.AddScoped<IMicrosoft365ConsentClient, Microsoft365ConsentClientAdapter>();
        services.AddScoped<IMicrosoft365ListItemDeltaClient, Microsoft365ListItemDeltaClientAdapter>();
        services.AddScoped<IMicrosoft365DriveItemDeltaClient, Microsoft365DriveItemDeltaClientAdapter>();
        services.AddScoped<IMicrosoft365DriveContentClient, Microsoft365DriveContentClientAdapter>();
        services.AddScoped<ISpreadsheetWorkbookReader, SpreadsheetWorkbookReaderAdapter>();
        services.AddScoped<IMicrosoft365ListSchemaClient, Microsoft365ListSchemaClientAdapter>();
        services.AddScoped<IMicrosoft365SiteSourcesClient, Microsoft365SiteSourcesClientAdapter>();
        services.AddScoped<IMicrosoft365SiteClient, Microsoft365SiteClientAdapter>();
        services.AddScoped<IMicrosoft365ApplicationTokenClient, Microsoft365ApplicationTokenClientAdapter>();
        services.AddScoped<IMicrosoft365SubscriptionClient, Microsoft365SubscriptionClientAdapter>();
        services.AddScoped<IMicrosoft365AclResolver, Microsoft365AclResolverAdapter>();
        services.AddScoped<IMicrosoft365UserGroupResolver, Microsoft365UserGroupResolverAdapter>();
        services.AddScoped<IMicrosoft365UserProfileResolver, Microsoft365UserProfileResolverAdapter>();
        services.AddScoped<IMicrosoft365SharePointGroupResolver, Microsoft365SharePointGroupResolverAdapter>();
        services.AddScoped<IMicrosoft365PassageAclWriter, Microsoft365PassageAclWriterAdapter>();
        services.AddScoped<IMicrosoft365PassageIndexWriter, Microsoft365PassageIndexWriterAdapter>();
        services.AddScoped<IMicrosoft365ContentExtractor, Microsoft365WordContentExtractorAdapter>();
        services.AddScoped<IMicrosoft365ContentExtractor, Microsoft365ExcelContentExtractorAdapter>();
        services.AddScoped<IMicrosoft365ContentExtractor, Microsoft365PdfImageContentExtractorAdapter>();
        services.AddScoped<IMicrosoft365ContentExtractor, Microsoft365PowerPointContentExtractorAdapter>();
        services.AddScoped<IMicrosoft365ArchiveContentExtractor, Microsoft365ArchiveContentExtractorAdapter>();
        services.AddScoped<IMicrosoft365EmbeddingGenerator, Microsoft365EmbeddingGeneratorAdapter>();
        services.AddScoped<IMicrosoft365SearchIndexInitializer, Microsoft365SearchIndexInitializerAdapter>();
        services.AddSingleton<IMicrosoft365ClientStateProtector, Microsoft365ClientStateProtectorAdapter>();
        services.AddSingleton(serviceProvider =>
        {
            var serviceBusOptions = serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
            return new AzureServiceBusPublisherClient(serviceBusOptions.FullyQualifiedNamespace);
        });
        services.AddSingleton<Microsoft365SynchronizationPublisherAdapter>();
        services.AddSingleton<Microsoft365LocalSynchronizationPublisherAdapter>();
        services.AddSingleton<IMicrosoft365SynchronizationPublisher>(serviceProvider =>
        {
            var serviceBusOptions = serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
            if (serviceBusOptions.Enabled)
            {
                return serviceProvider.GetRequiredService<Microsoft365SynchronizationPublisherAdapter>();
            }

            return serviceProvider.GetRequiredService<Microsoft365LocalSynchronizationPublisherAdapter>();
        });
        services.AddSingleton<IMicrosoft365ConsentStateProtector, Microsoft365ConsentStateProtectorAdapter>();
        services.AddSingleton<IMicrosoft365TechnicalTokenStore, Microsoft365TechnicalTokenStoreAdapter>();

        return services;
    }

    private static bool IsHttpsUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;

    private static bool IsSecureOrLoopbackUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps
            || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

    private static bool IsServiceBusNamespace(string value) =>
        Uri.CheckHostName(value) == UriHostNameType.Dns
        && !value.Contains("://", StringComparison.Ordinal);

    private static bool IsAzureSearchKnowledgeResourceName(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length is >= 2 and <= 100
        && Regex.IsMatch(value, "^[a-z0-9](?:[a-z0-9-]*[a-z0-9])$", RegexOptions.CultureInvariant);

    private static bool HasValidAzureOpenAiEmbeddingConfiguration(Microsoft365Options options) =>
        string.IsNullOrWhiteSpace(options.EmbeddingDeploymentName)
        || (!string.IsNullOrWhiteSpace(options.EmbeddingApiKey)
            && !string.IsNullOrWhiteSpace(options.EmbeddingApiVersion));

    private static bool IsSupportedReasoningEffort(string value) =>
        string.Equals(value, "minimal", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "low", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase);

    private static bool HasValidPlanningModelConfiguration(AzureAiSearchOptions options)
    {
        var hasCompleteConfiguration = IsHttpsUrl(options.PlanningModelEndpoint)
            && !string.IsNullOrWhiteSpace(options.PlanningModelDeploymentName)
            && !string.IsNullOrWhiteSpace(options.PlanningModelName)
            && !string.IsNullOrWhiteSpace(options.PlanningModelApiKey);
        var hasAnyConfiguration = !string.IsNullOrWhiteSpace(options.PlanningModelEndpoint)
            || !string.IsNullOrWhiteSpace(options.PlanningModelDeploymentName)
            || !string.IsNullOrWhiteSpace(options.PlanningModelName)
            || !string.IsNullOrWhiteSpace(options.PlanningModelApiKey);

        return (!hasAnyConfiguration || hasCompleteConfiguration)
            && (!(string.Equals(options.KnowledgeBaseRetrievalReasoningEffort, "low", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(options.KnowledgeBaseRetrievalReasoningEffort, "auto", StringComparison.OrdinalIgnoreCase))
                || hasCompleteConfiguration);
    }

    private static bool HasValidSharePointCertificateConfiguration(Microsoft365Options options)
    {
        var hasCertificatePath = !string.IsNullOrWhiteSpace(options.SharePointCertificatePath);
        var hasCertificateBase64 = !string.IsNullOrWhiteSpace(options.SharePointCertificateBase64);
        var hasCertificatePassword = !string.IsNullOrWhiteSpace(options.SharePointCertificatePassword);

        return !(hasCertificatePath && hasCertificateBase64)
            && ((hasCertificatePath || hasCertificateBase64) == hasCertificatePassword);
    }

    private static void AddProtectedHttpClient<TClient>(IServiceCollection services)
        where TClient : class =>
        services.AddHttpClient<TClient>()
            .RedactLoggedHeaders(SensitiveHttpHeaders)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                UseCookies = false
            });
}
