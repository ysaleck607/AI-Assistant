using System.Text;
using AssistantCore.ExternalServices.Entities.Microsoft;

namespace AssistantCore.ExternalServices.Services.Microsoft;

public sealed class MicrosoftCsvContentExtractorClient
{
    public async Task<MicrosoftCsvExtractionResult> ExtractAsync(
        string fileName,
        Stream content,
        long? contentLength,
        long maximumFileSizeBytes,
        int maximumExtractedCharacters,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFileSizeBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumExtractedCharacters);

        if (contentLength.HasValue
            && (contentLength.Value < 0 || contentLength.Value > maximumFileSizeBytes))
        {
            return new(MicrosoftCsvExtractionStatus.TooLarge, []);
        }

        try
        {
            await using var boundedContent = await CopyWithinLimitAsync(
                content,
                maximumFileSizeBytes,
                cancellationToken);
            if (boundedContent is null)
            {
                return new(MicrosoftCsvExtractionStatus.TooLarge, []);
            }

            using var reader = new StreamReader(
                boundedContent,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: false);
            var text = await reader.ReadToEndAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(text))
            {
                return new(MicrosoftCsvExtractionStatus.EmptyDocument, []);
            }

            var rows = ParseRows(text);
            if (rows.Count == 0)
            {
                return new(MicrosoftCsvExtractionStatus.EmptyDocument, []);
            }

            var headers = rows[0];
            var units = new List<MicrosoftCsvExtractedUnit>();
            var characterCount = 0;
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = rows[rowIndex];
                var values = row.Select((value, columnIndex) =>
                {
                    var header = columnIndex < headers.Count ? headers[columnIndex] : string.Empty;
                    return !string.IsNullOrWhiteSpace(header)
                        ? $"{header} : {value}"
                        : $"Colonne {columnIndex + 1} : {value}";
                });
                var unitText = string.Join(" | ", values);
                if (string.IsNullOrWhiteSpace(unitText))
                {
                    continue;
                }

                characterCount = checked(characterCount + unitText.Length);
                if (characterCount > maximumExtractedCharacters)
                {
                    return new(MicrosoftCsvExtractionStatus.TooLarge, []);
                }

                units.Add(new(units.Count, unitText, $"row:{rowIndex + 1}"));
            }

            return units.Count == 0
                ? new(MicrosoftCsvExtractionStatus.EmptyDocument, [])
                : new(MicrosoftCsvExtractionStatus.Success, units);
        }
        catch (DecoderFallbackException)
        {
            return new(MicrosoftCsvExtractionStatus.CorruptedDocument, []);
        }
        catch (FormatException)
        {
            return new(MicrosoftCsvExtractionStatus.CorruptedDocument, []);
        }
    }

    private static async Task<MemoryStream?> CopyWithinLimitAsync(
        Stream source,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var destination = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                destination.Position = 0;
                return destination;
            }

            total += read;
            if (total > maximumBytes)
            {
                await destination.DisposeAsync();
                return null;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static IReadOnlyList<IReadOnlyList<string>> ParseRows(string text)
    {
        var delimiter = DetectDelimiter(text);
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                if (quoted && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (!quoted && character == delimiter)
            {
                row.Add(field.ToString().Trim());
                field.Clear();
            }
            else if (!quoted && (character == '\n' || character == '\r'))
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }
                row.Add(field.ToString().Trim());
                field.Clear();
                if (row.Any(value => !string.IsNullOrWhiteSpace(value)))
                {
                    rows.Add(row);
                }
                row = new List<string>();
            }
            else
            {
                field.Append(character);
            }
        }

        if (quoted)
        {
            throw new FormatException("The CSV contains an unterminated quoted field.");
        }

        row.Add(field.ToString().Trim());
        if (row.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            rows.Add(row);
        }
        return rows;
    }

    private static char DetectDelimiter(string text)
    {
        var firstLine = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? text;
        return new[] { ',', ';', '\t', '|' }
            .Select(delimiter => (delimiter, count: firstLine.Count(character => character == delimiter)))
            .OrderByDescending(candidate => candidate.count)
            .First().delimiter;
    }
}
