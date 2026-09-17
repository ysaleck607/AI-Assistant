using AssistantCore.Service.Application.Models.Messages.Connectors.Microsoft365;
using AssistantCore.Service.Application.Services.Messages.Connectors;
using AssistantCore.Service.Application.Services.Messages.Connectors.Microsoft365;
using AssistantCore.Service.Application.Services.Messages.Evidence;
using AssistantCore.Service.Application.Services.Messages.Tools;
using AssistantCore.Service.Application.Services.Messages.Tabular;
using AssistantCore.Service.Infrastructure.Connectors.Microsoft365;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AssistantCore.Service.Infrastructure.Connectors;

public static class ConnectorServiceCollectionExtensions
{
    private const string Microsoft365SectionName = "Connectors:Microsoft365";
    private const int MaximumAllowedResults = 100;

    public static IServiceCollection AddConnectorInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var microsoft365Options = CreateMicrosoft365Options(configuration);

        services.AddSingleton(microsoft365Options);
        services.AddSingleton<IEvidenceNormalizer, EvidenceNormalizer>();
        services.AddScoped<IToolExecutionRouter, ScopedToolExecutionRouter>();
        services.AddScoped<IMicrosoft365SearchAccessVerifier, Microsoft365SearchAccessVerifierAdapter>();
        services.AddScoped<IAgenticRetrievalClient, AgenticRetrievalClientAdapter>();
        services.AddScoped<IMicrosoft365Connector, Microsoft365Connector>();
        services.AddScoped<IMicrosoft365OutlookMailboxQuery, Microsoft365OutlookMailboxQueryAdapter>();
        services.AddScoped<IMicrosoft365SpreadsheetDocumentResolver, Microsoft365SpreadsheetDocumentResolver>();
        services.AddScoped<IAiToolExecutionHandler, Microsoft365SearchToolExecutionHandler>();
        services.AddScoped<IAiToolExecutionHandler, Microsoft365OutlookMailboxToolExecutionHandler>();
        services.AddScoped<IAiToolExecutionHandler, Microsoft365SpreadsheetAnalysisToolExecutionHandler>();

        return services;
    }

    private static Microsoft365ConnectorOptions CreateMicrosoft365Options(
        IConfiguration configuration)
    {
        var section = configuration.GetSection(Microsoft365SectionName);
        var maximumResults = section.GetValue<int?>(
            nameof(Microsoft365ConnectorOptions.MaximumResults)) ?? 10;
        var maximumContentLength = section.GetValue<int?>(
            nameof(Microsoft365ConnectorOptions.MaximumContentLength)) ?? 4000;

        if (maximumResults is <= 0 or > MaximumAllowedResults)
        {
            throw new InvalidOperationException(
                $"Invalid configuration '{Microsoft365SectionName}': "
                + $"{nameof(Microsoft365ConnectorOptions.MaximumResults)} must be between 1 and {MaximumAllowedResults}.");
        }

        if (maximumContentLength <= 0)
        {
            throw new InvalidOperationException(
                $"Invalid configuration '{Microsoft365SectionName}': "
                + $"{nameof(Microsoft365ConnectorOptions.MaximumContentLength)} must be greater than zero.");
        }

        return new Microsoft365ConnectorOptions(maximumResults, maximumContentLength);
    }

}
