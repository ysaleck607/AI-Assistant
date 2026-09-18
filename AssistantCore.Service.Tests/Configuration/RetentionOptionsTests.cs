using AssistantCore.Service.Application.Configuration;

namespace AssistantCore.Service.Tests.Configuration;

public sealed class RetentionOptionsTests
{
    [Fact]
    public void Given_AllDurationsPositive_When_IsValid_Then_ReturnsTrue()
    {
        // Given
        var options = CreateValidOptions();

        // When / Then
        Assert.True(options.IsValid());
    }

    [Theory]
    [InlineData(0, 30, 30, 30, 30, 30)]
    [InlineData(30, 0, 30, 30, 30, 30)]
    [InlineData(30, 30, 0, 30, 30, 30)]
    [InlineData(30, 30, 30, 0, 30, 30)]
    [InlineData(30, 30, 30, 30, 0, 30)]
    [InlineData(30, 30, 30, 30, 30, 0)]
    [InlineData(-1, 30, 30, 30, 30, 30)]
    public void Given_ANonPositiveDurationForAnyCategory_When_IsValid_Then_ReturnsFalse(
        int conversationRecoveryDays,
        int usageRetentionDays,
        int searchRetentionDays,
        int ingestionRetentionDays,
        int logsRetentionDays,
        int auditRetentionDays)
    {
        // Given
        var options = new RetentionOptions
        {
            ConversationRecoveryDays = conversationRecoveryDays,
            UsageRetentionDays = usageRetentionDays,
            SearchRetentionDays = searchRetentionDays,
            IngestionRetentionDays = ingestionRetentionDays,
            LogsRetentionDays = logsRetentionDays,
            AuditRetentionDays = auditRetentionDays
        };

        // When / Then
        Assert.False(options.IsValid());
    }

    private static RetentionOptions CreateValidOptions() => new()
    {
        ConversationRecoveryDays = 30,
        UsageRetentionDays = 30,
        SearchRetentionDays = 30,
        IngestionRetentionDays = 30,
        LogsRetentionDays = 30,
        AuditRetentionDays = 30
    };
}
