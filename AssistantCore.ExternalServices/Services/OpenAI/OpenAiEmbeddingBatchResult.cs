namespace AssistantCore.ExternalServices.Services.OpenAI;

public sealed record OpenAiEmbeddingBatchResult(
    IReadOnlyList<IReadOnlyList<float>> Vectors,
    int TotalTokens);
