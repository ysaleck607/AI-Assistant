namespace AssistantCore.Service.Application.Configuration;

public sealed class Microsoft365Options
{
    public const string SectionName = "Microsoft365";

    public string AuthorityBaseUrl { get; init; } = "https://login.microsoftonline.com";

    public string GraphBaseUrl { get; init; } = "https://graph.microsoft.com";

    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    public string ClientStateHmacKey { get; init; } = string.Empty;

    public string SharePointCertificatePath { get; init; } = string.Empty;

    public string SharePointCertificateBase64 { get; init; } = string.Empty;

    public string SharePointCertificatePassword { get; init; } = string.Empty;

    public int SharePointGroupCacheMinutes { get; init; } = 5;

    public int OutlookRetentionDays { get; init; } = 180;

    public string ConsentCallbackUrl { get; init; } = string.Empty;

    public string ConsentSuccessRedirectUrl { get; init; } = string.Empty;

    public string ConsentErrorRedirectUrl { get; init; } = string.Empty;

    public int ConsentStateLifetimeMinutes { get; init; } = 10;

    public string WebhookBaseUrl { get; init; } = string.Empty;

    public int SubscriptionLifetimeHours { get; init; } = 48;

    public int SubscriptionRenewalLeadTimeHours { get; init; } = 24;

    public int SynchronizationLeaseMinutes { get; init; } = 15;

    public int SynchronizationIntervalMinutes { get; init; } = 15;

    public int AclReconciliationIntervalMinutes { get; init; } = 1440;

    public int AclReconciliationRetryMinutes { get; init; } = 15;

    public int AclReconciliationBatchSize { get; init; } = 100;

    public long MaximumExtractionFileSizeBytes { get; init; } = 25 * 1024 * 1024;

    public long MaximumExtractionExpandedSizeBytes { get; init; } = 100 * 1024 * 1024;

    public int MaximumExtractedCharacters { get; init; } = 2_000_000;

    public string OcrEndpoint { get; init; } = string.Empty;

    public string OcrApiKey { get; init; } = string.Empty;

    public int OcrTimeoutSeconds { get; init; } = 60;

    public int OcrPollIntervalMilliseconds { get; init; } = 500;

    public int OcrMinimumNativeCharactersPerPage { get; init; } = 20;

    public int MaximumPdfPages { get; init; } = 500;

    public long MaximumArchiveCompressedSizeBytes { get; init; } = 50 * 1024 * 1024;

    public long MaximumArchiveExpandedSizeBytes { get; init; } = 200 * 1024 * 1024;

    public int MaximumArchiveEntries { get; init; } = 1_000;

    public int MaximumArchiveDepth { get; init; } = 2;

    public double MaximumArchiveCompressionRatio { get; init; } = 100d;

    public int ArchiveExtractionTimeoutSeconds { get; init; } = 60;

    public int MaximumExcelSheets { get; init; } = 100;

    public int MaximumExcelCells { get; init; } = 100_000;

    public int MaximumPowerPointSlides { get; init; } = 1_000;

    public int ChunkMaximumTokens { get; init; } = 1200;

    public int ChunkOverlapTokens { get; init; } = 100;

    public int MaximumChunksPerDocument { get; init; } = 1000;

    public string EmbeddingEndpoint { get; init; } = "https://api.openai.com/v1";

    public string EmbeddingApiKey { get; init; } = string.Empty;

    public string EmbeddingDeploymentName { get; init; } = string.Empty;

    public string EmbeddingApiVersion { get; init; } = "2024-06-01";

    public string EmbeddingModel { get; init; } = "text-embedding-3-small";

    public int EmbeddingDimensions { get; init; } = 1536;

    public int EmbeddingBatchSize { get; init; } = 32;

    public int DocumentWorkLeaseMinutes { get; init; } = 10;

    public int DocumentWorkRetryMinutes { get; init; } = 5;

    public int DocumentWorkMaximumAttempts { get; init; } = 5;
}
