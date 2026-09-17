using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Microsoft365.ContentExtraction;
using AssistantCore.Service.Application.Services.Microsoft365;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365DocumentFailurePolicyTests
{
    [Theory, InlineAutoDomainData(5, 5, 1440)]
    public void Given_AnAclFailureAtTheAttemptLimit_When_Evaluate_Then_SchedulesLongTermRetry(
        int attemptCount,
        int maximumAttempts,
        int reconciliationIntervalMinutes,
        int documentRetryMinutes)
    {
        // Given
        var options = new Microsoft365Options
        {
            DocumentWorkMaximumAttempts = maximumAttempts,
            DocumentWorkRetryMinutes = documentRetryMinutes,
            AclReconciliationIntervalMinutes = reconciliationIntervalMinutes
        };

        // When
        var result = Microsoft365DocumentFailurePolicy.Evaluate(
            new Microsoft365AclResolutionException(),
            attemptCount,
            options);

        // Then
        Assert.False(result.IsPermanent);
        Assert.Equal(TimeSpan.FromMinutes(reconciliationIntervalMinutes), result.RetryDelay);
    }

    [Theory, AutoDomainData]
    public void Given_AnInvalidDocument_When_Evaluate_Then_ReturnsPermanentFailure(
        int documentRetryMinutes)
    {
        // Given
        var options = new Microsoft365Options
        {
            DocumentWorkMaximumAttempts = 5,
            DocumentWorkRetryMinutes = documentRetryMinutes,
            AclReconciliationIntervalMinutes = 1440
        };

        // When
        var result = Microsoft365DocumentFailurePolicy.Evaluate(
            new InvalidDataException(),
            attemptCount: 1,
            options);

        // Then
        Assert.True(result.IsPermanent);
        Assert.Equal(TimeSpan.FromMinutes(documentRetryMinutes), result.RetryDelay);
    }

    [Theory, AutoDomainData]
    public void Given_ATransientFailureBeforeTheAttemptLimit_When_Evaluate_Then_SchedulesNormalRetry(
        int documentRetryMinutes)
    {
        // Given
        var options = new Microsoft365Options
        {
            DocumentWorkMaximumAttempts = 5,
            DocumentWorkRetryMinutes = documentRetryMinutes,
            AclReconciliationIntervalMinutes = 1440
        };

        // When
        var result = Microsoft365DocumentFailurePolicy.Evaluate(
            new IOException(),
            attemptCount: 1,
            options);

        // Then
        Assert.False(result.IsPermanent);
        Assert.Equal(TimeSpan.FromMinutes(documentRetryMinutes), result.RetryDelay);
    }

    [Theory]
    [InlineAutoDomainData(Microsoft365ContentExtractionStatus.OcrUnavailable)]
    [InlineAutoDomainData(Microsoft365ContentExtractionStatus.OcrTimeout)]
    public void Given_ATransientOcrFailureBeforeTheAttemptLimit_When_Evaluate_Then_SchedulesNormalRetry(
        Microsoft365ContentExtractionStatus status,
        int documentRetryMinutes)
    {
        // Given
        var options = new Microsoft365Options
        {
            DocumentWorkMaximumAttempts = 5,
            DocumentWorkRetryMinutes = documentRetryMinutes,
            AclReconciliationIntervalMinutes = 1440
        };
        var exception = new Microsoft365ContentExtractionException(status);

        // When
        var result = Microsoft365DocumentFailurePolicy.Evaluate(
            exception,
            attemptCount: 1,
            options);

        // Then
        Assert.False(result.IsPermanent);
        Assert.Equal(TimeSpan.FromMinutes(documentRetryMinutes), result.RetryDelay);
        Assert.Equal(status.ToString(), exception.ErrorCode);
    }

    [Theory, AutoDomainData]
    public void Given_ANonRetryableExtractionFailure_When_Evaluate_Then_ReturnsPermanentFailure(
        int documentRetryMinutes)
    {
        // Given
        var options = new Microsoft365Options
        {
            DocumentWorkMaximumAttempts = 5,
            DocumentWorkRetryMinutes = documentRetryMinutes,
            AclReconciliationIntervalMinutes = 1440
        };
        var exception = new Microsoft365ContentExtractionException(
            Microsoft365ContentExtractionStatus.NoIndexableContent);

        // When
        var result = Microsoft365DocumentFailurePolicy.Evaluate(
            exception,
            attemptCount: 1,
            options);

        // Then
        Assert.True(result.IsPermanent);
        Assert.Equal(TimeSpan.FromMinutes(documentRetryMinutes), result.RetryDelay);
        Assert.Equal("NoIndexableContent", exception.ErrorCode);
    }
}
