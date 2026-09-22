using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AssistantCore.Repository.Queries;
using AssistantCore.Repository.Repositories;

namespace AssistantCore.Repository.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AssistantCoreDatabase")
            ?? throw new InvalidOperationException("Connection string 'AssistantCoreDatabase' is missing.");

        services.AddDbContext<AssistantCoreDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<IOrganizationQueries, OrganizationQueries>();
        services.AddScoped<IBackofficeOrganizationQueries, BackofficeOrganizationQueries>();
        services.AddScoped<IOrganizationMemberQueries, OrganizationMemberQueries>();
        services.AddScoped<IOrganizationConnectorQueries, OrganizationConnectorQueries>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IConversationPurgeRepository, ConversationPurgeRepository>();
        services.AddScoped<IPurgeOperationRepository, PurgeOperationRepository>();
        services.AddScoped<IMicrosoft365ConnectionRepository, Microsoft365ConnectionRepository>();
        services.AddScoped<IMicrosoft365DriveRepository, Microsoft365DriveRepository>();
        services.AddScoped<IMicrosoft365ListSynchronizationRepository, Microsoft365ListSynchronizationRepository>();
        services.AddScoped<IMicrosoft365DriveSynchronizationRepository, Microsoft365DriveSynchronizationRepository>();
        services.AddScoped<IMicrosoft365OutlookSynchronizationRepository, Microsoft365OutlookSynchronizationRepository>();
        services.AddScoped<IMicrosoft365SourceSynchronizationRepository, Microsoft365SourceSynchronizationRepository>();
        services.AddScoped<IMicrosoft365SourceDiscoveryRepository, Microsoft365SourceDiscoveryRepository>();
        services.AddScoped<IMicrosoft365SubscriptionRepository, Microsoft365SubscriptionRepository>();
        services.AddScoped<IMicrosoft365IndexedContentRepository, Microsoft365IndexedContentRepository>();
        services.AddScoped<
            IMicrosoft365DocumentWorkProcessingRepository,
            Microsoft365DocumentWorkProcessingRepository>();
        services.AddScoped<
            IMicrosoft365ListItemWorkProcessingRepository,
            Microsoft365ListItemWorkProcessingRepository>();
        services.AddScoped<
            IMicrosoft365IngestionRetentionRepository,
            Microsoft365IngestionRetentionRepository>();
        services.AddScoped<
            IMicrosoft365PendingSynchronizationRepository,
            Microsoft365PendingSynchronizationRepository>();
        services.AddScoped<
            IMicrosoft365ReindexOperationRepository,
            Microsoft365ReindexOperationRepository>();
        services.AddScoped<IAdministrativeAuditRepository, AdministrativeAuditRepository>();
        services.AddScoped<IOperationalIncidentRepository, OperationalIncidentRepository>();
        services.AddScoped<IOperationalIncidentQueries, OperationalIncidentQueries>();
        services.AddScoped<IBackofficeMessageWarningQueries, BackofficeMessageWarningQueries>();
        services.AddScoped<IBackofficeAuditQueries, BackofficeAuditQueries>();
        services.AddScoped<ILlmTokenConsumptionRepository, LlmTokenConsumptionRepository>();

        return services;
    }
}
