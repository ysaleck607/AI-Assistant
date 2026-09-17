using System.Net;
using AssistantCore.Service.Application.Services.Microsoft365;
using AssistantCore.Service.Infrastructure.Microsoft365;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Integration;

public sealed class ApplicationStartupTests
{
    [Theory]
    [InlineAutoDomainData("MaximumExecutionTimeSeconds")]
    [InlineAutoDomainData("RetrievalCandidateLimit")]
    [InlineAutoDomainData("FinalEvidenceLimit")]
    public void Given_AnInvalidAgentRuntimeLimit_When_CreateClient_Then_StartupFails(
        string optionName)
    {
        // Given
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Development);
                builder.ConfigureAppConfiguration(configuration =>
                    configuration.AddIntegrationTestDefaults().AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            [$"Messages:AgentRuntime:{optionName}"] = "0",
                            ["Microsoft365:ClientSecret"] = "integration-test-secret",
                            ["Microsoft365:ClientStateHmacKey"] = "integration-test-client-state-hmac-key"
                        }));
            });

        // When
        var exception = Assert.Throws<OptionsValidationException>(() =>
            factory.CreateClient());

        // Then
        Assert.Contains(
            "Messages:AgentRuntime",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory, InlineAutoDomainData("medium")]
    public void Given_UnsupportedKnowledgeBaseReasoning_When_CreateClient_Then_StartupFails(
        string reasoningEffort)
    {
        // Given
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Development);
                builder.ConfigureAppConfiguration(configuration =>
                    configuration.AddIntegrationTestDefaults().AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["AzureSearch:KnowledgeBaseRetrievalReasoningEffort"] = reasoningEffort,
                            ["Microsoft365:ClientSecret"] = "integration-test-secret",
                            ["Microsoft365:ClientStateHmacKey"] = "integration-test-client-state-hmac-key"
                        }));
            });

        // When
        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        // Then
        Assert.Contains("minimal, low or auto retrieval reasoning", exception.Message, StringComparison.Ordinal);
    }

    [Theory, AutoDomainData]
    public void Given_InvalidOutlookRetention_When_CreateClient_Then_StartupFails(Guid _)
    {
        // Given
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Development);
                builder.ConfigureAppConfiguration(configuration =>
                    configuration.AddIntegrationTestDefaults().AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Microsoft365:ClientSecret"] = "integration-test-secret",
                            ["Microsoft365:ClientStateHmacKey"] = "integration-test-client-state-hmac-key",
                            ["Microsoft365:OutlookRetentionDays"] = "0"
                        }));
            });

        // When
        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        // Then
        Assert.Contains("Microsoft365", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineAutoDomainData("low")]
    [InlineAutoDomainData("auto")]
    public void Given_ModelBackedKnowledgeBaseReasoningWithPlanningModel_When_CreateClient_Then_StartupSucceeds(
        string reasoningEffort)
    {
        // Given
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Development);
                builder.ConfigureAppConfiguration(configuration =>
                    configuration.AddIntegrationTestDefaults().AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["AzureSearch:KnowledgeBaseRetrievalReasoningEffort"] = reasoningEffort,
                            ["AzureSearch:PlanningModelEndpoint"] = "https://planning.openai.azure.com",
                            ["AzureSearch:PlanningModelDeploymentName"] = "gpt-5-mini",
                            ["AzureSearch:PlanningModelName"] = "gpt-5-mini",
                            ["AzureSearch:PlanningModelApiKey"] = "integration-test-key",
                            ["Microsoft365:ClientSecret"] = "integration-test-secret",
                            ["Microsoft365:ClientStateHmacKey"] = "integration-test-client-state-hmac-key"
                        }));
            });

        // When
        using var client = factory.CreateClient();

        // Then
        Assert.NotNull(client);
    }

    [Theory]
    [InlineAutoDomainData(0)]
    [InlineAutoDomainData(-1)]
    public void Given_AnInvalidMaximumMessageLength_When_CreateClient_Then_StartupFailsWithTheInvalidField(
        int maximumMessageLength)
    {
        // Given
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Development);
                builder.ConfigureAppConfiguration(configuration =>
                    configuration.AddIntegrationTestDefaults().AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Messages:MaximumMessageLength"] = maximumMessageLength.ToString(),
                            ["Microsoft365:ClientSecret"] = "integration-test-secret",
                            ["Microsoft365:ClientStateHmacKey"] = "integration-test-client-state-hmac-key"
                        }));
            });

        // When
        var exception = Assert.Throws<OptionsValidationException>(() =>
            factory.CreateClient());

        // Then
        Assert.Contains(
            "Messages:MaximumMessageLength",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Given_AClientSecretMissing_When_CreateClient_Then_Microsoft365StartupFails()
    {
        // Given
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Development);
                builder.ConfigureAppConfiguration(configuration =>
                    configuration.AddIntegrationTestDefaults().AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Microsoft365:ClientSecret"] = string.Empty,
                            ["Microsoft365:ClientStateHmacKey"] = "integration-test-client-state-hmac-key"
                        }));
            });

        // When
        var exception = Assert.Throws<OptionsValidationException>(() =>
            factory.CreateClient());

        // Then
        Assert.Contains("Microsoft365", exception.Message, StringComparison.Ordinal);
    }

    [Theory, InlineAutoDomainData(0)]
    public void Given_AnInvalidSynchronizationLease_When_CreateClient_Then_Microsoft365StartupFails(
        int synchronizationLeaseMinutes)
    {
        // Given
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Development);
                builder.ConfigureAppConfiguration(configuration =>
                    configuration.AddIntegrationTestDefaults().AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Microsoft365:ClientSecret"] = "integration-test-secret",
                            ["Microsoft365:ClientStateHmacKey"] = "integration-test-client-state-hmac-key",
                            ["Microsoft365:SynchronizationLeaseMinutes"] = synchronizationLeaseMinutes.ToString()
                        }));
            });

        // When
        var exception = Assert.Throws<OptionsValidationException>(() =>
            factory.CreateClient());

        // Then
        Assert.Contains("Microsoft365", exception.Message, StringComparison.Ordinal);
    }

    [Theory, InlineAutoDomainData(0)]
    public void Given_AnInvalidSynchronizationInterval_When_CreateClient_Then_Microsoft365StartupFails(
        int synchronizationIntervalMinutes)
    {
        // Given
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Development);
                builder.ConfigureAppConfiguration(configuration =>
                    configuration.AddIntegrationTestDefaults().AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Microsoft365:ClientSecret"] = "integration-test-secret",
                            ["Microsoft365:ClientStateHmacKey"] = "integration-test-client-state-hmac-key",
                            ["Microsoft365:SynchronizationIntervalMinutes"] = synchronizationIntervalMinutes.ToString()
                        }));
            });

        // When
        var exception = Assert.Throws<OptionsValidationException>(() =>
            factory.CreateClient());

        // Then
        Assert.Contains("Microsoft365", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Given_ValidDevelopmentConfiguration_When_StartingApplication_Then_RootEndpointRespondsWithoutError()
    {
        // Given
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Development);
                builder.ConfigureAppConfiguration(configuration =>
                    configuration.AddIntegrationTestDefaults().AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Microsoft365:ClientSecret"] = "integration-test-secret",
                            ["Microsoft365:ClientStateHmacKey"] = "integration-test-client-state-hmac-key"
                        }));
            });

        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });

        // When
        using var response = await client.GetAsync("/");

        // Then
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/swagger", response.Headers.Location?.OriginalString);
    }

    [Theory, AutoDomainData]
    public void Given_LocalServiceBusDisabled_When_CreateClient_Then_LocalPublisherIsRegistered(
        bool _)
    {
        // Given
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Development);
                builder.ConfigureAppConfiguration(configuration =>
                    configuration.AddIntegrationTestDefaults().AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Microsoft365:ClientSecret"] = "integration-test-secret",
                            ["Microsoft365:ClientStateHmacKey"] = "integration-test-client-state-hmac-key",
                            ["ServiceBus:Enabled"] = "false"
                        }));
            });
        using var client = factory.CreateClient();

        // When
        var publisher = factory.Services.GetRequiredService<IMicrosoft365SynchronizationPublisher>();

        // Then
        Assert.IsType<Microsoft365LocalSynchronizationPublisherAdapter>(publisher);
    }

    [Theory, AutoDomainData]
    public void Given_AzureServiceBusEnabled_When_CreateClient_Then_AzurePublisherIsRegistered(
        bool _)
    {
        // Given
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Development);
                builder.ConfigureAppConfiguration(configuration =>
                    configuration.AddIntegrationTestDefaults().AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Microsoft365:ClientSecret"] = "integration-test-secret",
                            ["Microsoft365:ClientStateHmacKey"] = "integration-test-client-state-hmac-key",
                            ["ServiceBus:Enabled"] = "true",
                            ["ServiceBus:FullyQualifiedNamespace"] = "assistant-test.servicebus.windows.net"
                        }));
            });
        using var client = factory.CreateClient();

        // When
        var publisher = factory.Services.GetRequiredService<IMicrosoft365SynchronizationPublisher>();

        // Then
        Assert.IsType<Microsoft365SynchronizationPublisherAdapter>(publisher);
    }
}
