using System.IO.Compression;
using System.Text;
using AssistantCore.ExternalServices.Entities.Microsoft;
using AssistantCore.ExternalServices.Services.Microsoft;
using Xunit;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class MicrosoftArchiveContentExtractorClientTests
{
    [Theory, AutoDomainData]
    public async Task Given_safe_zip_When_ExtractAsync_Then_returns_file_with_normalized_path(
        MicrosoftArchiveContentExtractorClient client)
    {
        // Given
        await using var content = CreateZip(("rh\\politique.txt", "Conditions de paiement"));

        // When
        var result = await ExtractAsync(client, "documents.zip", content);

        // Then
        var entry = Assert.Single(result.Entries);
        Assert.Equal(MicrosoftArchiveExtractionStatus.Success, result.Status);
        Assert.Equal("rh/politique.txt", entry.ArchivePath);
        Assert.Equal("Conditions de paiement", Encoding.UTF8.GetString(entry.Content));
    }

    [Theory, AutoDomainData]
    public async Task Given_zip_slip_entry_When_ExtractAsync_Then_ignores_unsafe_path(
        MicrosoftArchiveContentExtractorClient client)
    {
        // Given
        await using var content = CreateZip(("../../secrets.txt", "secret"));

        // When
        var result = await ExtractAsync(client, "documents.zip", content);

        // Then
        Assert.Empty(result.Entries);
        Assert.Equal(MicrosoftArchiveExtractionStatus.UnsafeArchivePath, result.Status);
        Assert.Contains(MicrosoftArchiveExtractionWarning.UnsafeArchivePath, result.Warnings);
    }

    [Theory, AutoDomainData]
    public async Task Given_gzip_content_When_ExtractAsync_Then_returns_single_entry(
        MicrosoftArchiveContentExtractorClient client)
    {
        // Given
        await using var content = new MemoryStream();
        await using (var gzip = new GZipStream(content, CompressionMode.Compress, leaveOpen: true))
        await using (var writer = new StreamWriter(gzip, Encoding.UTF8, leaveOpen: true))
        {
            await writer.WriteAsync("budget");
        }
        content.Position = 0;

        // When
        var result = await ExtractAsync(client, "budget.txt.gz", content);

        // Then
        var entry = Assert.Single(result.Entries);
        Assert.Equal(MicrosoftArchiveExtractionStatus.Success, result.Status);
        Assert.Equal("budget.txt", entry.FileName);
    }

    private static async Task<MicrosoftArchiveExtractionResult> ExtractAsync(
        MicrosoftArchiveContentExtractorClient client,
        string fileName,
        Stream content) => await client.ExtractAsync(
            fileName, content, content.Length, 1_000_000, 2_000_000, 10, 1000, CancellationToken.None);

    private static MemoryStream CreateZip(params (string Name, string Content)[] files)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                using var writer = new StreamWriter(
                    archive.CreateEntry(file.Name).Open(),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(file.Content);
            }
        }
        stream.Position = 0;
        return stream;
    }
}
