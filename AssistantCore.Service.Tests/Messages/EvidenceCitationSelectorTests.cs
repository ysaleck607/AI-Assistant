using AssistantCore.Service.Application.Models.Messages;
using AssistantCore.Service.Application.Services.Messages.AgentRuntime;

namespace AssistantCore.Service.Tests.Messages;

public sealed class EvidenceCitationSelectorTests
{
    [Theory, AutoDomainData]
    public void Given_MultipleEmailsWithCommonMicrosoftWords_When_AnswerContainsExactInvoiceData_Then_SelectsOnlyMatchingEmail()
    {
        // Given
        var evidence = new[]
        {
            CreateEvidence(
                "digest",
                "Courriel : Weekly digest Microsoft service updates",
                "Microsoft 365 service information and account updates."),
            CreateEvidence(
                "subscription",
                "Courriel : Votre abonnement Microsoft 365 Business Basic",
                "Votre abonnement Microsoft 365 est maintenant actif."),
            CreateEvidence(
                "invoice",
                "Courriel : Votre G185096135 de facture Microsoft est prête",
                "Facture G185096135. Montant à payer : 26,22 $ CAD. Échéance : 16 septembre 2026.")
        };

        // When
        var citations = EvidenceCitationSelector.Select(
            "Tu dois payer 26,22 $ CAD à Microsoft pour la facture G185096135.",
            evidence,
            maximumResults: 8);

        // Then
        var citation = Assert.Single(citations);
        Assert.Equal("invoice", citation.EvidenceId);
    }

    [Theory, AutoDomainData]
    public void Given_NoStructuredNumber_When_AProjectNameDistinguishesOneDocument_Then_TfIdfSelectsTheRelevantDocument()
    {
        // Given
        var evidence = new[]
        {
            CreateEvidence(
                "atlas",
                "Projet Atlas",
                "Le projet Atlas utilise le code ORANGE et concerne Groupe Horizon."),
            CreateEvidence(
                "finance",
                "Politique financière",
                "Procédure interne de traitement des factures fournisseurs."),
            CreateEvidence(
                "hr",
                "Guide RH",
                "Informations concernant les congés et avantages sociaux.")
        };

        // When
        var citations = EvidenceCitationSelector.Select(
            "Le projet Atlas concerne Groupe Horizon.",
            evidence,
            maximumResults: 8);

        // Then
        var citation = Assert.Single(citations);
        Assert.Equal("atlas", citation.EvidenceId);
    }

    [Theory, AutoDomainData]
    public void Given_TwoIndependentClaims_When_EachIsSupportedByADifferentDocument_Then_ReturnsBothSupportingSourcesOnly()
    {
        // Given
        var evidence = new[]
        {
            CreateEvidence(
                "invoice",
                "Courriel : facture Microsoft G185096135",
                "Le montant de la facture G185096135 est 26,22 $ CAD."),
            CreateEvidence(
                "atlas",
                "Projet Atlas",
                "Le code du projet Atlas est ORANGE-7429."),
            CreateEvidence(
                "noise",
                "Guide RH",
                "Politique de vacances et avantages sociaux.")
        };

        // When
        var citations = EvidenceCitationSelector.Select(
            "La facture Microsoft G185096135 est de 26,22 $ CAD.\nLe code du projet Atlas est ORANGE-7429.",
            evidence,
            maximumResults: 8);

        // Then
        Assert.Equal(2, citations.Count);
        Assert.Contains(citations, citation => citation.EvidenceId == "invoice");
        Assert.Contains(citations, citation => citation.EvidenceId == "atlas");
        Assert.DoesNotContain(citations, citation => citation.EvidenceId == "noise");
    }

    [Theory, AutoDomainData]
    public void Given_ExactAmountExistsInOnlyOneSimilarEmail_When_SemanticScoreFavorsAnotherEmail_Then_ExactAnchorWins()
    {
        // Given
        var evidence = new[]
        {
            CreateEvidence(
                "wrong",
                "Courriel : facture Microsoft précédente",
                "Facture Microsoft. Montant 19,99 $ CAD.",
                relevanceScore: 4.0),
            CreateEvidence(
                "right",
                "Courriel : facture Microsoft courante",
                "Facture Microsoft. Montant 26,22 $ CAD.",
                relevanceScore: 2.5)
        };

        // When
        var citations = EvidenceCitationSelector.Select(
            "Le montant à payer est 26,22 $ CAD.",
            evidence,
            maximumResults: 8);

        // Then
        var citation = Assert.Single(citations);
        Assert.Equal("right", citation.EvidenceId);
    }

    [Theory, AutoDomainData]
    public void Given_AmbiguousAnswer_When_SemanticScoresDiffer_Then_PrefersTheBestRetrievedEvidence()
    {
        // Given
        var evidence = new[]
        {
            CreateEvidence("weak", "Document A", "Information générale.", relevanceScore: 1.8),
            CreateEvidence("strong", "Document B", "Information générale.", relevanceScore: 3.7)
        };

        // When
        var citations = EvidenceCitationSelector.Select(
            "Information générale.",
            evidence,
            maximumResults: 1);

        // Then
        var citation = Assert.Single(citations);
        Assert.Equal("strong", citation.EvidenceId);
    }

    private static RetrievedEvidence CreateEvidence(
        string id,
        string title,
        string content,
        double? relevanceScore = null) =>
        new(
            id,
            "Microsoft365",
            title,
            content,
            $"reference:{id}",
            $"https://example.test/{id}",
            null,
            relevanceScore);
}
