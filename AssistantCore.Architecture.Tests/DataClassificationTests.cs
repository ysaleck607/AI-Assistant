using System.Collections;
using System.Reflection;
using AssistantCore.Repository.Domain.Entities;
using Xunit;

namespace AssistantCore.Architecture.Tests;

/// <summary>
/// Rend executable la regle de gouvernance de docs/security/data-classification.md :
/// toute colonne persistee est classifiee avant d'entrer dans le schema. Une nouvelle
/// propriete ajoutee a une entite sans ligne correspondante dans le document fait
/// echouer le build plutot que de passer inapercue jusqu'a la production.
///
/// La verification porte sur le nom de colonne, pas sur le couple table + colonne :
/// une colonne nommee comme une colonne deja classifiee ailleurs passe. C'est un
/// garde-fou contre l'oubli, pas une preuve de classification correcte, et la revue
/// du document reste necessaire.
/// </summary>
public sealed class DataClassificationTests
{
    private const string ClassificationDocumentPath = "docs/security/data-classification.md";

    [Fact]
    public void Given_PersistedEntities_When_ValidateDataClassification_Then_EveryColumnIsDocumented()
    {
        // Given
        var document = ReadClassificationDocument();
        var entities = GetPersistedEntityTypes();

        // When
        var violations = new List<string>();
        foreach (var entity in entities)
        {
            if (!document.Contains(entity.Name, StringComparison.Ordinal))
            {
                violations.Add($"{entity.Name} is absent from {ClassificationDocumentPath}.");
                continue;
            }

            foreach (var column in GetColumnNames(entity))
            {
                if (!document.Contains($"`{column}`", StringComparison.Ordinal))
                {
                    violations.Add(
                        $"{entity.Name}.{column} is not classified in {ClassificationDocumentPath}.");
                }
            }
        }

        // Then
        Assert.Empty(violations);
    }

    [Fact]
    public void Given_TheClassificationDocument_When_ValidateStructure_Then_EveryLevelIsDefined()
    {
        // Given
        var document = ReadClassificationDocument();

        // When
        var violations = new[]
            {
                "Niveau A",
                "Niveau B",
                "Niveau C",
                "Niveau D",
                "Chiffrement applicatif",
                "Blind index HMAC"
            }
            .Where(section => !document.Contains(section, StringComparison.Ordinal))
            .Select(section => $"The classification document must define '{section}'.")
            .ToArray();

        // Then
        Assert.Empty(violations);
    }

    private static IReadOnlyCollection<Type> GetPersistedEntityTypes() =>
        typeof(Organization).Assembly
            .GetTypes()
            .Where(type =>
                type.IsClass
                && type.IsPublic
                && type.Namespace == typeof(Organization).Namespace)
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Ne retient que les colonnes : les proprietes de navigation et les collections
    /// n'existent pas en base et n'ont donc rien a classifier. Les proprietes heritees
    /// sont classifiees avec le type qui les declare.
    /// </summary>
    private static IEnumerable<string> GetColumnNames(Type entity) =>
        entity.GetProperties(
                BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.DeclaredOnly)
            .Where(property => property.CanRead && property.CanWrite)
            .Where(property => !IsNavigation(property.PropertyType))
            .Select(property => property.Name);

    private static bool IsNavigation(Type type) =>
        type.Namespace == typeof(Organization).Namespace
        || (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(byte[]));

    private static string ReadClassificationDocument()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, ClassificationDocumentPath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"{ClassificationDocumentPath} was not found above {AppContext.BaseDirectory}.");
    }
}
