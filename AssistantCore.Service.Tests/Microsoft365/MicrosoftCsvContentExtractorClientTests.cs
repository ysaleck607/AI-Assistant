using System.Text;
using AssistantCore.ExternalServices.Entities.Microsoft;
using AssistantCore.ExternalServices.Services.Microsoft;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class MicrosoftCsvContentExtractorClientTests
{
    [Theory, AutoDomainData]
    public async Task Given_ACommaDelimitedCsv_When_ExtractAsync_Then_ReturnsRowsWithHeaders(
        string fileName)
    {
        // Given
        const string csv = "Name,Company\nAlice,Atlas\nBob,Northwind";
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var client = new MicrosoftCsvContentExtractorClient();

        // When
        var result = await client.ExtractAsync(
            $"{fileName}.csv",
            content,
            null,
            1024,
            10_000,
            CancellationToken.None);

        // Then
        Assert.Equal(MicrosoftCsvExtractionStatus.Success, result.Status);
        Assert.Contains(result.Units, unit => unit.Text == "Name : Alice | Company : Atlas");
        Assert.Contains(result.Units, unit => unit.Text == "Name : Bob | Company : Northwind");
    }

    [Theory, AutoDomainData]
    public async Task Given_AQuotedSemicolonDelimitedCsv_When_ExtractAsync_Then_PreservesQuotedValues(
        string fileName)
    {
        // Given
        const string csv = "Nom;Description\n\"Atlas\";\"Client; prioritaire\"";
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var client = new MicrosoftCsvContentExtractorClient();

        // When
        var result = await client.ExtractAsync(
            $"{fileName}.csv",
            content,
            null,
            1024,
            10_000,
            CancellationToken.None);

        // Then
        Assert.Equal(MicrosoftCsvExtractionStatus.Success, result.Status);
        Assert.Contains(
            result.Units,
            unit => unit.Text == "Nom : Atlas | Description : Client; prioritaire");
    }
}
