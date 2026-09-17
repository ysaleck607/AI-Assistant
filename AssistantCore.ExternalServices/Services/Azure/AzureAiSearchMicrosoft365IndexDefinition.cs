using AssistantCore.ExternalServices.Entities.Azure;

namespace AssistantCore.ExternalServices.Services.Azure;

public static class AzureAiSearchMicrosoft365IndexDefinition
{
    public static IReadOnlyCollection<AzureAiSearchIndexFieldDefinition> CreateFields() =>
    [
        new("chunkId", "Edm.String", Key: true, Filterable: true),
        new("organizationId", "Edm.String", Filterable: true, Retrievable: false),
        new("sourceType", "Edm.String", Filterable: true),
        new("title", "Edm.String", Searchable: true),
        new("content", "Edm.String", Searchable: true),
        new("siteId", "Edm.String", Filterable: true),
        new("driveId", "Edm.String", Filterable: true),
        new("driveItemId", "Edm.String", Filterable: true),
        new("documentVersion", "Edm.String", Filterable: true),
        new("archivePath", "Edm.String", Filterable: true),
        new("chunkNumber", "Edm.Int32", Filterable: true),
        new("url", "Edm.String"),
        new("modifiedAt", "Edm.DateTimeOffset", Filterable: true),
        new("contentVector", "Collection(Edm.Single)", Searchable: true, Retrievable: false),
        new("allowedUserIds", "Collection(Edm.String)", Filterable: true, Retrievable: false),
        new("allowedGroupIds", "Collection(Edm.String)", Filterable: true, Retrievable: false),
        new("allowedSharePointGroupIds", "Collection(Edm.String)", Filterable: true, Retrievable: false),
        new("hasAnonymousLink", "Edm.Boolean", Filterable: true, Retrievable: false),
        new("hasOrganizationLink", "Edm.Boolean", Filterable: true, Retrievable: false),
        new("aclFingerprint", "Edm.String", Filterable: true, Retrievable: false),
        new("isAvailable", "Edm.Boolean", Filterable: true)
    ];
}
