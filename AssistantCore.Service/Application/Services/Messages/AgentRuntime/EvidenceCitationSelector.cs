using System.Globalization;
using System.Text.RegularExpressions;
using AssistantCore.Service.Application.Models.Messages;

namespace AssistantCore.Service.Application.Services.Messages.AgentRuntime;

/// <summary>
/// Selects the evidence items that most plausibly support the final answer without asking
/// the generative model to choose its own citations. Attribution is evaluated per answer
/// claim and combines:
/// - candidate-local TF-IDF cosine similarity (language agnostic, no stop-word list),
/// - exact structured anchors such as amounts, dates and identifiers,
/// - Azure AI Search semantic relevance when the connector provides it.
/// </summary>
internal static class EvidenceCitationSelector
{
    private static readonly Regex TokenRegex = new(
        @"[\p{L}\p{N}]+(?:[._:/,-][\p{L}\p{N}]+)*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ClaimSeparatorRegex = new(
        @"(?:\r?\n)+|(?<=[.!?;:])\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyCollection<RetrievedEvidence> Select(
        string answer,
        IReadOnlyCollection<RetrievedEvidence> evidence,
        int maximumResults)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(answer);
        ArgumentNullException.ThrowIfNull(evidence);

        if (maximumResults <= 0 || evidence.Count == 0)
        {
            return [];
        }

        if (evidence.Count == 1)
        {
            return evidence.Take(maximumResults).ToArray();
        }

        var candidates = evidence
            .Select(candidate => new CandidateTokens(
                candidate,
                Tokenize(candidate.Title),
                Tokenize(candidate.Content)))
            .ToArray();
        var inverseDocumentFrequency = BuildInverseDocumentFrequency(
            BuildDocumentFrequency(candidates),
            candidates.Length);

        var selected = new Dictionary<string, SelectedEvidence>(StringComparer.Ordinal);
        foreach (var claim in ExtractClaims(answer))
        {
            foreach (var item in SelectForClaim(claim, candidates, inverseDocumentFrequency))
            {
                if (!selected.TryGetValue(item.Evidence.EvidenceId, out var current)
                    || item.Score > current.Score)
                {
                    selected[item.Evidence.EvidenceId] = item;
                }
            }
        }

        if (selected.Count == 0)
        {
            // Very short or heavily paraphrased answers can have weak lexical support.
            // Fall back to one strongest item only; never expose the whole searched set.
            var fallback = ScoreClaim(answer, candidates, inverseDocumentFrequency)
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.Evidence.RelevanceScore.HasValue)
                .ThenByDescending(item => item.Evidence.RelevanceScore)
                .First();
            return [fallback.Evidence];
        }

        return selected.Values
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Evidence.RelevanceScore.HasValue)
            .ThenByDescending(item => item.Evidence.RelevanceScore)
            .Take(maximumResults)
            .Select(item => item.Evidence)
            .ToArray();
    }

    private static IReadOnlyCollection<SelectedEvidence> SelectForClaim(
        string claim,
        IReadOnlyCollection<CandidateTokens> candidates,
        IReadOnlyDictionary<string, double> inverseDocumentFrequency)
    {
        var scored = ScoreClaim(claim, candidates, inverseDocumentFrequency)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Evidence.RelevanceScore.HasValue)
            .ThenByDescending(item => item.Evidence.RelevanceScore)
            .ToArray();
        if (scored.Length == 0)
        {
            return [];
        }

        // If a claim contains an exact amount/date/code and at least one candidate contains
        // it, only candidates carrying such an anchor can support this claim. This prevents
        // a semantically similar invoice/email from being cited for the wrong exact value.
        var anchored = scored.Where(item => item.AnchorCoverage > 0d).ToArray();
        var eligible = anchored.Length > 0 ? anchored : scored;
        var bestScore = eligible[0].Score;
        if (bestScore < 0.10)
        {
            return [];
        }

        var relativeThreshold = bestScore * 0.80;
        return eligible
            .Where(item => item.Score >= relativeThreshold)
            .Take(2)
            .Select(item => new SelectedEvidence(item.Evidence, item.Score))
            .ToArray();
    }

    private static IReadOnlyCollection<ScoredEvidence> ScoreClaim(
        string claim,
        IReadOnlyCollection<CandidateTokens> candidates,
        IReadOnlyDictionary<string, double> inverseDocumentFrequency)
    {
        var claimTokens = Tokenize(claim);
        if (claimTokens.Length < 2)
        {
            return [];
        }

        var anchors = claimTokens
            .Where(IsStructuredAnchor)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return candidates
            .Select(candidate => ScoreCandidate(
                claimTokens,
                candidate,
                inverseDocumentFrequency,
                anchors))
            .ToArray();
    }

    private static ScoredEvidence ScoreCandidate(
        IReadOnlyList<string> claimTokens,
        CandidateTokens candidate,
        IReadOnlyDictionary<string, double> inverseDocumentFrequency,
        IReadOnlySet<string> anchors)
    {
        var combinedTokens = candidate.TitleTokens
            .Concat(candidate.ContentTokens)
            .ToArray();

        var contentSimilarity = TfIdfCosine(
            claimTokens,
            combinedTokens,
            inverseDocumentFrequency);
        var titleSimilarity = TfIdfCosine(
            claimTokens,
            candidate.TitleTokens,
            inverseDocumentFrequency);

        var evidenceTokenSet = combinedTokens.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matchedAnchors = anchors.Count == 0
            ? 0
            : anchors.Count(evidenceTokenSet.Contains);
        var anchorCoverage = anchors.Count == 0
            ? 0d
            : (double)matchedAnchors / anchors.Count;

        // Azure semantic reranker scores use a bounded relevance scale. Connectors that do
        // not expose one simply contribute no retrieval-score weight.
        var retrievalScore = candidate.Evidence.RelevanceScore is { } score
            ? Math.Clamp(score / 4d, 0d, 1d)
            : 0d;

        var anchorWeight = anchors.Count > 0 ? 0.45 : 0d;
        var retrievalWeight = candidate.Evidence.RelevanceScore.HasValue ? 0.15 : 0d;
        var lexicalWeight = 1d - anchorWeight - retrievalWeight;
        var lexicalScore = 0.80 * contentSimilarity + 0.20 * titleSimilarity;

        return new ScoredEvidence(
            candidate.Evidence,
            lexicalWeight * lexicalScore
                + anchorWeight * anchorCoverage
                + retrievalWeight * retrievalScore,
            anchorCoverage);
    }

    private static string[] ExtractClaims(string answer) =>
        ClaimSeparatorRegex.Split(answer)
            .Select(claim => claim.Trim().TrimStart('-', '*', '•', ' '))
            .Where(claim => Tokenize(claim).Length >= 2)
            .ToArray();

    private static bool IsStructuredAnchor(string token)
    {
        if (!token.Any(char.IsDigit) || token.Length < 2)
        {
            return false;
        }

        // Short standalone counters such as "1" or "2" are too weak. Everything else
        // containing digits is useful as an amount, date, invoice number, project code,
        // email fragment, percentage or other structured value.
        return token.Length >= 3 || token.Any(character => !char.IsDigit(character));
    }

    private static Dictionary<string, int> BuildDocumentFrequency(
        IReadOnlyCollection<CandidateTokens> candidates)
    {
        var frequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var uniqueTokens = candidate.TitleTokens
                .Concat(candidate.ContentTokens)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var token in uniqueTokens)
            {
                frequency[token] = frequency.GetValueOrDefault(token) + 1;
            }
        }

        return frequency;
    }

    private static Dictionary<string, double> BuildInverseDocumentFrequency(
        IReadOnlyDictionary<string, int> documentFrequency,
        int documentCount) =>
        documentFrequency.ToDictionary(
            pair => pair.Key,
            pair => Math.Log((documentCount + 1d) / (pair.Value + 1d)),
            StringComparer.OrdinalIgnoreCase);

    private static double TfIdfCosine(
        IReadOnlyList<string> leftTokens,
        IReadOnlyList<string> rightTokens,
        IReadOnlyDictionary<string, double> inverseDocumentFrequency)
    {
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return 0d;
        }

        var left = BuildVector(leftTokens, inverseDocumentFrequency);
        var right = BuildVector(rightTokens, inverseDocumentFrequency);
        if (left.Count == 0 || right.Count == 0)
        {
            return 0d;
        }

        var dot = 0d;
        foreach (var (term, leftWeight) in left)
        {
            if (right.TryGetValue(term, out var rightWeight))
            {
                dot += leftWeight * rightWeight;
            }
        }

        var leftNorm = Math.Sqrt(left.Values.Sum(weight => weight * weight));
        var rightNorm = Math.Sqrt(right.Values.Sum(weight => weight * weight));
        return leftNorm <= double.Epsilon || rightNorm <= double.Epsilon
            ? 0d
            : dot / (leftNorm * rightNorm);
    }

    private static Dictionary<string, double> BuildVector(
        IReadOnlyList<string> tokens,
        IReadOnlyDictionary<string, double> inverseDocumentFrequency)
    {
        var counts = tokens
            .GroupBy(token => token, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var total = Math.Max(1, tokens.Count);
        var vector = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var (token, count) in counts)
        {
            if (!inverseDocumentFrequency.TryGetValue(token, out var idf) || idf <= 0d)
            {
                continue;
            }

            vector[token] = ((double)count / total) * idf;
        }

        return vector;
    }

    private static string[] Tokenize(string value) =>
        TokenRegex.Matches(value.Normalize())
            .Select(match => match.Value.ToLower(CultureInfo.InvariantCulture))
            .Where(token => token.Length >= 2)
            .ToArray();

    private sealed record CandidateTokens(
        RetrievedEvidence Evidence,
        IReadOnlyList<string> TitleTokens,
        IReadOnlyList<string> ContentTokens);

    private sealed record ScoredEvidence(
        RetrievedEvidence Evidence,
        double Score,
        double AnchorCoverage);

    private sealed record SelectedEvidence(RetrievedEvidence Evidence, double Score);
}
