using System.Text.Json;
using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Queries;
using AssistantCore.Service.Application.Models.Messages.Tools;
using Microsoft.Extensions.Caching.Memory;

namespace AssistantCore.Service.Application.Services.Messages.Tools;

public sealed class AiToolRegistry(
    IOrganizationConnectorQueries organizationConnectorQueries,
    IEnumerable<IAiToolExecutionHandler> toolHandlers,
    IMemoryCache? memoryCache = null) : IAiToolRegistry
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    public async Task<IReadOnlyCollection<AiToolDefinition>> GetAvailableToolsAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(organizationId, Guid.Empty);

        var cacheKey = $"ai-tools:{organizationId:D}";
        if (memoryCache is not null
            && memoryCache.TryGetValue(cacheKey, out IReadOnlyCollection<AiToolDefinition>? cachedTools)
            && cachedTools is not null)
        {
            return cachedTools;
        }

        var connectors = await organizationConnectorQueries.GetActiveConfiguredConnectors(
            organizationId,
            cancellationToken);
        var executableTools = toolHandlers
            .Select(handler => handler.ToolName)
            .ToHashSet(StringComparer.Ordinal);
        var tools = connectors
            .SelectMany(connector => CreateToolDefinitions(connector, executableTools))
            .ToArray();

        if (tools.Length > 0)
        {
            memoryCache?.Set(cacheKey, tools, CacheDuration);
        }
        return tools;
    }

    public void InvalidateCache(Guid organizationId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(organizationId, Guid.Empty);
        memoryCache?.Remove($"ai-tools:{organizationId:D}");
    }

    private static IReadOnlyCollection<AiToolDefinition> CreateToolDefinitions(
        OrganizationConnector connector,
        IReadOnlySet<string> executableTools)
    {
        if (connector.Type != ConnectorType.Microsoft365)
        {
            return [];
        }

        var tools = new List<AiToolDefinition>();
        if (executableTools.Contains(AiToolNames.SearchMicrosoft365))
        {
            var searchTool = CreateMicrosoft365SearchTool(connector);
            if (searchTool is not null)
            {
                tools.Add(searchTool);
            }
        }

        if (executableTools.Contains(AiToolNames.QueryOutlookMailbox))
        {
            tools.Add(CreateOutlookMailboxQueryTool());
        }

        if (executableTools.Contains(AiToolNames.AnalyzeMicrosoft365Spreadsheet)
            && connector.Sources.Any(source => source.SourceType is
                Microsoft365SourceType.SharePoint or Microsoft365SourceType.OneDrive))
        {
            tools.Add(CreateMicrosoft365SpreadsheetAnalysisTool());
        }

        return tools;
    }

    private static AiToolDefinition? CreateMicrosoft365SearchTool(OrganizationConnector connector)
    {
        var allowedSourceTypes = connector.Sources
            .Select(source => source.SourceType switch
            {
                Microsoft365SourceType.SharePoint => "sharepoint",
                Microsoft365SourceType.OneDrive => "onedrive",
                Microsoft365SourceType.Outlook => "outlook",
                _ => null
            })
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(sourceType => sourceType, StringComparer.Ordinal)
            .ToArray();

        if (allowedSourceTypes.Length == 0)
        {
            return null;
        }

        return new AiToolDefinition(
            AiToolNames.SearchMicrosoft365,
            "Rechercher semantiquement et faire une synthese dans les contenus Microsoft 365 autorises et deja indexes, y compris plusieurs documents ou courriels.",
            CreateObjectSchema(
                new Dictionary<string, object>
                {
                    ["query"] = StringProperty(
                        "Termes a rechercher dans les contenus Microsoft 365."),
                    ["sourceTypes"] = NullableProperty(new
                    {
                        type = "array",
                        items = new { type = "string", @enum = allowedSourceTypes },
                        description = "Sources a limiter, ou null pour toutes les sources autorisees."
                    }),
                    ["dateFrom"] = NullableDateProperty(
                        "Date minimale de modification des contenus. Ne filtre pas les dates mentionnees dans leur contenu."),
                    ["dateTo"] = NullableDateProperty(
                        "Date maximale de modification des contenus. Ne filtre pas les dates mentionnees dans leur contenu.")
                }));
    }

    private static AiToolDefinition CreateMicrosoft365SpreadsheetAnalysisTool() =>
        new(
            AiToolNames.AnalyzeMicrosoft365Spreadsheet,
            "Analyser exhaustivement un classeur Excel Microsoft 365 autorise avec des calculs et filtres deterministes.",
            CreateObjectSchema(
                new Dictionary<string, object>
                {
                    ["fileName"] = StringProperty("Nom exact du fichier XLSX ou XLSM, avec son extension."),
                    ["worksheetName"] = NullableProperty(new
                    {
                        type = "string",
                        description = "Nom de la feuille. Obligatoire lorsque le classeur contient plusieurs feuilles."
                    }),
                    ["aggregations"] = NullableProperty(new
                    {
                        type = "array",
                        description = "Calculs executes sur toutes les lignes avant les filtres, ou null si aucun calcul n'est requis.",
                        items = CreateObjectSchema(new Dictionary<string, object>
                        {
                            ["alias"] = StringProperty("Nom unique permettant de reutiliser le resultat dans un filtre."),
                            ["operation"] = EnumStringProperty(
                                "Operation mathematique.",
                                "average", "sum", "minimum", "maximum", "count"),
                            ["column"] = StringProperty("Nom exact de la colonne numerique.")
                        })
                    }),
                    ["filters"] = NullableProperty(new
                    {
                        type = "array",
                        description = "Filtres combines avec AND et appliques a toutes les lignes, ou null pour ne pas filtrer.",
                        items = CreateObjectSchema(new Dictionary<string, object>
                        {
                            ["column"] = StringProperty("Nom exact de la colonne a filtrer."),
                            ["operator"] = EnumStringProperty(
                                "Operateur de comparaison.",
                                "equals", "not_equals", "greater_than", "greater_than_or_equal",
                                "less_than", "less_than_or_equal", "contains"),
                            ["value"] = NullableScalarProperty(
                                "Valeur litterale. Null si aggregationAlias est utilise."),
                            ["aggregationAlias"] = NullableProperty(new
                            {
                                type = "string",
                                description = "Alias d'un calcul a comparer. Null si value est utilise."
                            })
                        })
                    }),
                    ["selectColumns"] = NullableProperty(new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "Colonnes a retourner, ou null pour retourner toutes les colonnes."
                    })
                }));

    private static AiToolDefinition CreateOutlookMailboxQueryTool() =>
        new(
            AiToolNames.QueryOutlookMailbox,
            "Localiser, lister et lire les messages actuels de la boite Outlook de l'utilisateur connecte. "
            + "Utiliser includeBody=true lorsque la reponse depend du contenu exact d'un message. Une recherche "
            + "vide permet de lister les messages les plus recents.",
            CreateObjectSchema(
                new Dictionary<string, object>
                {
                    ["query"] = NullableProperty(new
                    {
                        type = "string",
                        description = "Texte a rechercher dans l'expediteur, l'objet ou le corps, ou null pour un listing."
                    }),
                    ["sender"] = NullableProperty(new
                    {
                        type = "string",
                        description = "Nom, alias ou adresse de l'expediteur, ou null."
                    }),
                    ["recipient"] = NullableProperty(new
                    {
                        type = "string",
                        description = "Nom, alias ou adresse du destinataire, ou null."
                    }),
                    ["scope"] = EnumStringProperty(
                        "Portee de la boite : received pour les messages recus, sent pour les messages envoyes, all pour tous.",
                        "received", "sent", "all"),
                    ["dateFrom"] = NullableDateProperty(
                        "Date minimale de reception ou d'envoi. Null pour ne pas limiter la date minimale."),
                    ["dateTo"] = NullableDateProperty(
                        "Date maximale inclusive de reception ou d'envoi. Null pour ne pas limiter la date maximale."),
                    ["includeBody"] = new
                    {
                        type = "boolean",
                        description = "True lorsque la reponse exige le contenu exact, un montant ou un detail du message; false pour seulement identifier ou lister les courriels."
                    },
                    ["limit"] = new
                    {
                        type = "integer",
                        minimum = 1,
                        maximum = 20,
                        description = "Nombre maximal de courriels a retourner. Utiliser 5 par defaut."
                    }
                }));

    private static JsonElement CreateObjectSchema(IReadOnlyDictionary<string, object> properties) =>
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties,
            required = properties.Keys.ToArray(),
            additionalProperties = false
        });

    private static object StringProperty(string description) => new
    {
        type = "string",
        description
    };

    private static object EnumStringProperty(string description, params string[] values) => new
    {
        type = "string",
        @enum = values,
        description
    };

    private static object NullableDateProperty(string description) =>
        NullableProperty(new
        {
            type = "string",
            description = $"{description} Format YYYY-MM-DD."
        });

    private static object NullableScalarProperty(string description) => new
    {
        anyOf = new object[]
        {
            new { type = "string", description },
            new { type = "number", description },
            new { type = "boolean", description },
            new { type = "null" }
        }
    };

    private static object NullableProperty(object property) => new
    {
        anyOf = new object[]
        {
            property,
            new { type = "null" }
        }
    };
}
