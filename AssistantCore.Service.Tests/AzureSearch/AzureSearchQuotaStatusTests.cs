using AssistantCore.Service.Application.Services.AzureSearch;

namespace AssistantCore.Service.Tests.AzureSearch;

public sealed class AzureSearchQuotaStatusTests
{
    [Theory]
    [InlineData(44_215_502L, 52_428_800L, 0.8433438)]
    [InlineData(0L, 52_428_800L, 0d)]
    [InlineData(52_428_800L, 52_428_800L, 1d)]
    public void Given_UsageAndQuota_When_UsageRatio_Then_ComputesTheExpectedRatio(
        long usage,
        long quota,
        double expectedRatio)
    {
        // Given
        var status = new AzureSearchQuotaStatus(usage, quota);

        // When
        var ratio = status.UsageRatio;

        // Then
        Assert.Equal(expectedRatio, ratio, precision: 5);
    }

    [Fact]
    public void Given_AZeroQuota_When_UsageRatio_Then_ReturnsZeroInsteadOfDividingByZero()
    {
        // Given
        var status = new AzureSearchQuotaStatus(1_000L, 0L);

        // When
        var ratio = status.UsageRatio;

        // Then
        Assert.Equal(0d, ratio);
    }
}
