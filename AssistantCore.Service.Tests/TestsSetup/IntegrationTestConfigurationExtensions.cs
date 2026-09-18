using Microsoft.Extensions.Configuration;

namespace AssistantCore.Service.Tests;

internal static class IntegrationTestConfigurationExtensions
{
    public static IConfigurationBuilder AddIntegrationTestDefaults(
        this IConfigurationBuilder configuration) =>
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Microsoft365:ClientSecret"] = "integration-test-secret",
            ["Microsoft365:ClientStateHmacKey"] = "integration-test-client-state-hmac-key",
            ["MemberPii:EmailLookupHmacKey"] = "integration-test-member-email-lookup-hmac-key",
            ["Microsoft365:ConsentCallbackUrl"] =
                "https://localhost:7292/api/microsoft365/consent/callback",
            ["Microsoft365:ConsentSuccessRedirectUrl"] =
                "https://localhost:4200/microsoft365/consent/success",
            ["Microsoft365:ConsentErrorRedirectUrl"] =
                "https://localhost:4200/microsoft365/consent/error",
            ["Microsoft365:WebhookBaseUrl"] = "https://localhost:7292",
            ["AzureSearch:VectorSearchMetric"] = "cosine",
            ["AzureSearch:KnowledgeBaseRetrievalReasoningEffort"] = "minimal",
            ["AzureSearch:VectorizerName"] = "m365-azure-openai-vectorizer",
            ["AzureSearch:MinimumSemanticRelevanceScore"] = "1.5",
            ["AzureSearch:KnowledgeBaseMaxRuntimeInSeconds"] = "30",
            ["AzureSearch:KnowledgeBaseMaxOutputSizeInTokens"] = "6000",
            ["AzureSearch:KnowledgeSourceName"] = "synaptix-m365-test-knowledge-source",
            ["AzureSearch:KnowledgeBaseName"] = "synaptix-m365-test-knowledge-base",
            ["AzureSearch:PlanningModelEndpoint"] = "",
            ["AzureSearch:PlanningModelDeploymentName"] = "",
            ["AzureSearch:PlanningModelName"] = "",
            ["AzureSearch:PlanningModelApiKey"] = ""
        });
}
