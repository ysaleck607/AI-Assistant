using AssistantCore.ExternalServices.Services.Email;
using AssistantCore.Repository.Persistence;
using AssistantCore.Service.Application;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Services.Conversations.Purge;
using AssistantCore.Service.Infrastructure.Microsoft365;
using AssistantCore.Service.Infrastructure.Persistence;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AssistantCore.Ingestion.Worker;

public static class WorkerProgram
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        if (builder.Environment.IsEnvironment("Certif")
            || builder.Environment.IsEnvironment("LocalLive"))
        {
            builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly());
        }

        if (string.IsNullOrWhiteSpace(
                builder.Configuration.GetConnectionString("AssistantCoreDatabase")))
        {
            throw new InvalidOperationException(
                "Connection string 'AssistantCoreDatabase' is required by the ingestion worker.");
        }

        builder.Services.AddOptions<Microsoft365WorkerOptions>()
            .Bind(builder.Configuration.GetSection(Microsoft365WorkerOptions.SectionName))
            .Validate(
                options => !options.RunStartupConnectionCheck || options.StartupConnectionId is not null,
                $"{Microsoft365WorkerOptions.SectionName}:StartupConnectionId is required when the startup connection check is enabled.")
            .Validate(
                options => options.MaintenanceIntervalSeconds > 0,
                $"{Microsoft365WorkerOptions.SectionName}:MaintenanceIntervalSeconds must be greater than zero.")
            .Validate(
                options => options.MaximumSynchronizationsPerCycle > 0
                    && options.MaximumDocumentsPerCycle > 0
                    && options.MaximumListItemsPerCycle > 0,
                $"{Microsoft365WorkerOptions.SectionName} batch sizes must be greater than zero.")
            .ValidateOnStart();

        builder.Services.AddOptions<RetentionOptions>()
            .Bind(builder.Configuration.GetSection(RetentionOptions.SectionName))
            .Validate(
                options => options.IsValid(),
                $"{RetentionOptions.SectionName} requires a positive retention duration for every category.")
            .ValidateOnStart();

        builder.Services.AddOptions<ConversationPurgeOptions>()
            .Bind(builder.Configuration.GetSection(ConversationPurgeOptions.SectionName))
            .Validate(
                options => options.IsValid(),
                $"{ConversationPurgeOptions.SectionName} requires positive lease, polling, attempt and backoff values.")
            .ValidateOnStart();

        builder.Services.AddScoped<IConversationPurgeService, ConversationPurgeService>();

        builder.Services.AddOptions<SmtpOptions>()
            .Bind(builder.Configuration.GetSection(SmtpOptions.SectionName));

        builder.Services.AddOptions<OperationalIncidentDigestOptions>()
            .Bind(builder.Configuration.GetSection(OperationalIncidentDigestOptions.SectionName))
            .Validate(
                options => !options.Enabled
                    || (options.IntervalMinutes > 0 && !string.IsNullOrWhiteSpace(options.RecipientAddress)),
                $"{OperationalIncidentDigestOptions.SectionName} requires a positive interval and a recipient address when enabled.")
            .ValidateOnStart();

        builder.Services.AddOptions<AzureSearchQuotaAlertOptions>()
            .Bind(builder.Configuration.GetSection(AzureSearchQuotaAlertOptions.SectionName))
            .Validate(
                options => options.IntervalMinutes > 0
                    && options.ThresholdRatio is > 0 and <= 1,
                $"{AzureSearchQuotaAlertOptions.SectionName} requires a positive interval and a threshold ratio between 0 and 1.")
            .ValidateOnStart();

        builder.Services.AddOptions<LlmQuotaAlertOptions>()
            .Bind(builder.Configuration.GetSection(LlmQuotaAlertOptions.SectionName))
            .Validate(
                options => options.IntervalMinutes > 0
                    && options.ThresholdRatio is > 0 and <= 1,
                $"{LlmQuotaAlertOptions.SectionName} requires a positive interval and a threshold ratio between 0 and 1.")
            .ValidateOnStart();

        builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

        builder.Services.AddMicrosoft365WorkerApplication();
        builder.Services.AddMicrosoft365Infrastructure(builder.Configuration);
        builder.Services.AddPersistenceEncryption(builder.Configuration);
        builder.Services.AddPersistence(builder.Configuration);
        builder.Services.AddHostedService<Microsoft365IngestionWorker>();
        builder.Services.AddHostedService<ConversationPurgeWorker>();
        builder.Services.AddHostedService<OperationalIncidentDigestWorker>();
        builder.Services.AddHostedService<AzureSearchQuotaAlertWorker>();
        builder.Services.AddHostedService<LlmQuotaAlertWorker>();

        await builder.Build().RunAsync();
    }
}
