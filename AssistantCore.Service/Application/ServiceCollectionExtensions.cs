using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Services.Backoffice;
using AssistantCore.Service.Application.Services.AuthenticateUser;
using AssistantCore.Service.Application.Services.Conversations;
using AssistantCore.Service.Application.Services.Conversations.Purge;
using AssistantCore.Service.Application.Services.Conversations.Audit;
using AssistantCore.Service.Application.Services.Conversations.Pagination;
using AssistantCore.Service.Application.Services.Members;
using AssistantCore.Service.Application.Services.Messages;
using AssistantCore.Service.Application.Services.Messages.AgentRuntime;
using AssistantCore.Service.Application.Services.Messages.Authorization;
using AssistantCore.Service.Application.Services.Messages.Lifecycle;
using AssistantCore.Service.Application.Services.Messages.Responses;
using AssistantCore.Service.Application.Services.Messages.Streaming;
using AssistantCore.Service.Application.Services.Messages.Tabular;
using AssistantCore.Service.Application.Services.Messages.Tools;
using AssistantCore.Service.Application.Services.Messages.Validation;
using AssistantCore.Service.Application.Services.Microsoft365;
using AssistantCore.Service.Application.Services.Organizations;
using AssistantCore.Service.Application.Services.TenantAdmission;
using AssistantCore.Service.Application.Services.Usage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AssistantCore.Service.Application;

public static class ServiceCollectionExtensions
{
    private const int MaximumPersistedTitleLength = 200;

    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MessagesOptions>()
            .Bind(configuration.GetSection(MessagesOptions.SectionName))
            .Validate(
                options => options.MaximumMessageLength > 0,
                $"{MessagesOptions.SectionName}:{nameof(MessagesOptions.MaximumMessageLength)} must be greater than zero.")
            .ValidateOnStart();
        services.AddOptions<AgentRuntimeOptions>()
            .Bind(configuration.GetSection(AgentRuntimeOptions.SectionName))
            .Validate(
                options => options.MaximumExecutionTimeSeconds > 0
                    && options.RetrievalCandidateLimit is >= 50 and <= 200
                    && options.FinalEvidenceLimit is >= 1 and <= 50
                    && options.FinalEvidenceLimit <= options.RetrievalCandidateLimit,
                $"{AgentRuntimeOptions.SectionName} requires a candidate limit from 50 to 200 and a final evidence limit from 1 to 50 that does not exceed it.")
            .ValidateOnStart();
        services.AddOptions<ConversationListingOptions>()
            .Bind(configuration.GetSection(ConversationListingOptions.SectionName))
            .Validate(
                options => options.MaximumPreviewLength > 0,
                $"{ConversationListingOptions.SectionName}:{nameof(ConversationListingOptions.MaximumPreviewLength)} must be greater than zero.")
            .ValidateOnStart();
        services.AddOptions<ConversationOptions>()
            .Bind(configuration.GetSection(ConversationOptions.SectionName))
            .Validate(
                options => options.MaximumTitleLength is > 0 and <= MaximumPersistedTitleLength,
                $"{ConversationOptions.SectionName}:{nameof(ConversationOptions.MaximumTitleLength)} must be between 1 and {MaximumPersistedTitleLength}.")
            .ValidateOnStart();
        services.AddOptions<ConversationPurgeOptions>()
            .Bind(configuration.GetSection(ConversationPurgeOptions.SectionName))
            .Validate(
                options => options.IsValid(),
                $"{ConversationPurgeOptions.SectionName} requires positive lease, polling, attempt and backoff values.")
            .ValidateOnStart();
        services.AddOptions<RetentionOptions>()
            .Bind(configuration.GetSection(RetentionOptions.SectionName))
            .Validate(
                options => options.IsValid(),
                $"{RetentionOptions.SectionName} requires a positive retention duration for every category.")
            .ValidateOnStart();
        services.AddOptions<OrganizationRoleOptions>()
            .Bind(configuration.GetSection(OrganizationRoleOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.RequiredAdmissionRole)
                    && !string.IsNullOrWhiteSpace(options.TenantAdminRole),
                $"{OrganizationRoleOptions.SectionName}:{nameof(OrganizationRoleOptions.RequiredAdmissionRole)} and {nameof(OrganizationRoleOptions.TenantAdminRole)} are required.")
            .ValidateOnStart();
        services.AddOptions<UsageOptions>()
            .Bind(configuration.GetSection(UsageOptions.SectionName))
            .Validate(
                options => options.DefaultMonthlyTokenLimit > 0,
                $"{UsageOptions.SectionName}:{nameof(UsageOptions.DefaultMonthlyTokenLimit)} must be greater than zero.")
            .ValidateOnStart();
        services.AddScoped<IUsageTrackingService, UsageTrackingService>();
        services.AddScoped<IConversationPurgeService, ConversationPurgeService>();
        services.AddScoped<ISendMessageCommandValidator, SendMessageCommandValidator>();
        services.AddSingleton<IConversationCursorCodec, ConversationCursorCodec>();
        services.AddSingleton<IConversationMessageCursorCodec, ConversationMessageCursorCodec>();
        services.AddScoped<IConversationListingService, ConversationListingService>();
        services.AddScoped<IConversationMessageListingService, ConversationMessageListingService>();
        services.AddScoped<IConversationAuditWriter, LoggingConversationAuditWriter>();
        services.AddScoped<IConversationLifecycleService, ConversationLifecycleService>();
        services.AddScoped<IConversationEncryptionBackfillService, ConversationEncryptionBackfillService>();
        services.AddSingleton<ITenantAdmissionPolicy, TenantAdmissionPolicy>();
        services.AddScoped<IMicrosoft365OnboardingCompletionChecker, Microsoft365OnboardingCompletionChecker>();
        services.AddScoped<IMessageUserContextService, MessageUserContextService>();
        services.AddScoped<IAuthenticateUserService, AuthenticateUserService>();
        services.AddScoped<IOrganizationRoleResolver, OrganizationRoleResolver>();
        services.AddScoped<IMemberManagementService, MemberManagementService>();
        services.AddScoped<IOrganizationManagementService, OrganizationManagementService>();
        services.AddScoped<IBackofficeOrganizationService, BackofficeOrganizationService>();
        services.AddMicrosoft365Application();
        services.AddScoped<IMessageProcessingLifecycleService, MessageProcessingLifecycleService>();
        services.AddScoped<IAgentRuntime, FoundryAgentRuntime>();
        services.AddSingleton<ISendMessageResponseFactory, SendMessageResponseFactory>();
        services.AddScoped<IMessageStreamErrorReporter, MessageStreamErrorReporter>();
        services.AddScoped<IAiToolRegistry, AiToolRegistry>();
        services.AddScoped<IAiToolArgumentSchemaValidator, AiToolArgumentSchemaValidator>();
        services.AddScoped<IAiToolArgumentSecurityValidator, AiToolArgumentSecurityValidator>();
        services.AddScoped<IAiToolDateRangeValidator, AiToolDateRangeValidator>();
        services.AddScoped<IAiToolCallValidator, AiToolCallValidator>();
        services.AddScoped<IMicrosoft365SpreadsheetAnalysisService, Microsoft365SpreadsheetAnalysisService>();
        services.AddSingleton<ISpreadsheetAnalysisEngine, SpreadsheetAnalysisEngine>();

        return services;
    }

    public static IServiceCollection AddMicrosoft365Application(this IServiceCollection services)
    {
        services.AddMicrosoft365WorkerApplication();
        services.AddScoped<IMicrosoft365ConnectionService, Microsoft365ConnectionService>();
        services.AddScoped<IMicrosoft365OnboardingService, Microsoft365OnboardingService>();
        services.AddScoped<IMicrosoft365ListActivationService, Microsoft365ListActivationService>();
        services.AddScoped<IMicrosoft365ListConsultationService, Microsoft365ListConsultationService>();
        services.AddScoped<IMicrosoft365SiteSourcesDiscoveryService, Microsoft365SiteSourcesDiscoveryService>();
        services.AddScoped<IMicrosoft365SiteDiscoveryService, Microsoft365SiteDiscoveryService>();
        services.AddScoped<IMicrosoft365SiteSelectionService, Microsoft365SiteSelectionService>();
        services.AddScoped<IMicrosoft365DriveAdministrationService, Microsoft365DriveAdministrationService>();
        services.AddScoped<IMicrosoft365ReindexService, Microsoft365ReindexService>();
        services.AddScoped<IMicrosoft365ReindexStatusService, Microsoft365ReindexStatusService>();
        services.AddScoped<IMicrosoft365CurrentUserOneDriveIndexingService, Microsoft365CurrentUserOneDriveIndexingService>();
        services.AddScoped<IMicrosoft365CurrentUserOutlookIndexingService, Microsoft365CurrentUserOutlookIndexingService>();

        return services;
    }

    public static IServiceCollection AddMicrosoft365WorkerApplication(this IServiceCollection services)
    {
        services.AddScoped<IMicrosoft365IngestionOrchestrator, Microsoft365IngestionOrchestrator>();
        services.AddScoped<IMicrosoft365ListSynchronizationService, Microsoft365ListSynchronizationService>();
        services.AddScoped<IMicrosoft365DriveSynchronizationService, Microsoft365DriveSynchronizationService>();
        services.AddScoped<IMicrosoft365OutlookSynchronizationService, Microsoft365OutlookSynchronizationService>();
        services.AddScoped<IMicrosoft365OutlookMessageIndexingService, Microsoft365OutlookMessageIndexingService>();
        services.AddScoped<IMicrosoft365SubscriptionMaintenanceService, Microsoft365SubscriptionMaintenanceService>();
        services.AddScoped<IMicrosoft365ReconciliationService, Microsoft365ReconciliationService>();
        services.AddScoped<
            IMicrosoft365AclReconciliationService,
            Microsoft365AclReconciliationService>();
        services.AddScoped<IMicrosoftGraphNotificationService, MicrosoftGraphNotificationService>();
        services.AddSingleton<IMicrosoft365ListSchemaFingerprintGenerator, Microsoft365ListSchemaFingerprintGenerator>();
        services.AddSingleton<IMicrosoft365ListItemWorkFactory, Microsoft365ListItemWorkFactory>();
        services.AddSingleton<IMicrosoft365DocumentSupportPolicy, Microsoft365DocumentSupportPolicy>();
        services.AddSingleton<IMicrosoft365DocumentWorkFactory, Microsoft365DocumentWorkFactory>();
        services.AddScoped<
            IMicrosoft365ContentExtractionService,
            Microsoft365ContentExtractionService>();
        services.AddSingleton<IMicrosoft365DocumentChunkingService, Microsoft365DocumentChunkingService>();
        services.AddScoped<IMicrosoft365DocumentProcessingService, Microsoft365DocumentProcessingService>();
        services.AddScoped<
            IMicrosoft365PendingSynchronizationService,
            Microsoft365PendingSynchronizationService>();
        services.AddScoped<IMicrosoft365IndexCleanupService, Microsoft365IndexCleanupService>();
        services.AddScoped<IMicrosoft365ReindexIndexSweeper, Microsoft365ReindexIndexSweeper>();
        services.AddScoped<IMicrosoft365ReindexProgressService, Microsoft365ReindexProgressService>();
        services.AddScoped<
            IMicrosoft365ContentAclSynchronizationService,
            Microsoft365ContentAclSynchronizationService>();
        services.AddScoped<IMicrosoft365PassageIndexingService, Microsoft365PassageIndexingService>();
        services.AddSingleton<
            IMicrosoft365SecurityIdentityNormalizer,
            Microsoft365SecurityIdentityNormalizer>();
        services.AddSingleton<
            IMicrosoft365PermissionRoleEvaluator,
            Microsoft365PermissionRoleEvaluator>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }
}
