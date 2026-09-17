using AssistantCore.Repository.Persistence;
using AssistantCore.Service.Application;
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
                    && options.MaximumDocumentsPerCycle > 0,
                $"{Microsoft365WorkerOptions.SectionName} batch sizes must be greater than zero.")
            .ValidateOnStart();

        builder.Services.AddMicrosoft365WorkerApplication();
        builder.Services.AddMicrosoft365Infrastructure(builder.Configuration);
        builder.Services.AddPersistenceEncryption();
        builder.Services.AddPersistence(builder.Configuration);
        builder.Services.AddHostedService<Microsoft365IngestionWorker>();
        builder.Services.AddHostedService<ConversationPurgeWorker>();

        await builder.Build().RunAsync();
    }
}
