using AssistantCore.Service.Application.Models.Messages.Connectors.Microsoft365;
using AssistantCore.Service.Application.Services.Microsoft365;

namespace AssistantCore.Service.Infrastructure.Connectors.Microsoft365;

internal static class Microsoft365SearchFilterBuilder
{
    private static readonly IReadOnlySet<string> SupportedSourceTypes =
        new HashSet<string>(["sharepoint", "onedrive", "outlook"], StringComparer.OrdinalIgnoreCase);

    public static string Build(Microsoft365SearchParameters parameters)
    {
        ValidateParameters(parameters);

        var clauses = new List<string>
        {
            BuildSecurityFilter(parameters.SecurityContext)
        };
        if (parameters.SourceTypes is { Count: > 0 })
        {
            var sourceTypes = parameters.SourceTypes
                .Select(sourceType => sourceType.ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(sourceType => sourceType, StringComparer.Ordinal);
            clauses.Add($"sourceType eq '{string.Join("' or sourceType eq '", sourceTypes)}'");
        }

        if (parameters.DateFrom is { } dateFrom)
        {
            clauses.Add($"modifiedAt ge {FormatDate(dateFrom)}");
        }

        if (parameters.DateTo is { } dateTo)
        {
            clauses.Add($"modifiedAt lt {FormatDate(dateTo.AddDays(1))}");
        }

        return string.Join(" and ", clauses.Select(clause => $"({clause})"));
    }

    internal static string BuildSecurityFilter(Microsoft365SearchSecurityContext securityContext)
    {
        ArgumentNullException.ThrowIfNull(securityContext);
        var organizationId = NormalizeIdentifier(
            securityContext.OrganizationId.ToString("D"),
            "organization");
        var userId = NormalizeIdentifier(securityContext.EntraUserId, "Microsoft Entra user");
        var groupIds = securityContext.EntraGroupIds
            .Select(NormalizeMicrosoftEntraGroupSecurityIdentifier)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(groupId => groupId, StringComparer.Ordinal)
            .ToArray();

        var accessClauses = new List<string>
        {
            $"allowedUserIds/any(id: id eq '{userId}')"
        };
        if (groupIds.Length > 0)
        {
            accessClauses.Add(
                $"allowedGroupIds/any(id: search.in(id, '{string.Join(',', groupIds)}', ','))");
        }

        var sharePointGroupIds = securityContext.SharePointGroupIds
            .Select(NormalizeSharePointGroupIdentifier)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(groupId => groupId, StringComparer.Ordinal)
            .ToArray();
        if (sharePointGroupIds.Length > 0)
        {
            accessClauses.Add(
                $"allowedSharePointGroupIds/any(id: search.in(id, '{string.Join('|', sharePointGroupIds)}', '|'))");
        }

        return $"organizationId eq '{organizationId}' and isAvailable eq true and ({string.Join(" or ", accessClauses)})";
    }

    private static void ValidateParameters(Microsoft365SearchParameters parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters.Query);
        ArgumentNullException.ThrowIfNull(parameters.SecurityContext);
        ArgumentNullException.ThrowIfNull(parameters.SecurityContext.EntraGroupIds);
        ArgumentNullException.ThrowIfNull(parameters.SecurityContext.SharePointGroupIds);
        if (parameters.MaximumResults <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters));
        }

        if (parameters.DateFrom > parameters.DateTo)
        {
            throw new ArgumentException(
                "The start date cannot be after the end date.",
                nameof(parameters));
        }

        if (parameters.SourceTypes?.Any(sourceType => !SupportedSourceTypes.Contains(sourceType)) == true)
        {
            throw new ArgumentException(
                "An unsupported Microsoft 365 source type was requested.",
                nameof(parameters));
        }
    }

    private static string NormalizeIdentifier(string value, string name)
    {
        if (!Guid.TryParse(value, out var identifier) || identifier == Guid.Empty)
        {
            throw new ArgumentException($"A valid {name} identifier is required.");
        }

        return identifier.ToString("D");
    }

    private static string NormalizeSharePointGroupIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !value.StartsWith("spg:", StringComparison.Ordinal)
            || value.Contains('\'')
            || value.Contains('|'))
        {
            throw new ArgumentException(
                "A valid Microsoft SharePoint group identifier is required.",
                nameof(value));
        }

        return value;
    }

    private static string NormalizeMicrosoftEntraGroupSecurityIdentifier(string value)
    {
        if (Guid.TryParse(value, out var groupId) && groupId != Guid.Empty)
        {
            return groupId.ToString("D");
        }

        if (value.StartsWith(
                Microsoft365SecurityIdentityNormalizer.EntraGroupOwnerPrefix,
                StringComparison.Ordinal)
            && Guid.TryParse(
                value[Microsoft365SecurityIdentityNormalizer.EntraGroupOwnerPrefix.Length..],
                out var ownerGroupId)
            && ownerGroupId != Guid.Empty)
        {
            return $"{Microsoft365SecurityIdentityNormalizer.EntraGroupOwnerPrefix}{ownerGroupId:D}";
        }

        throw new ArgumentException(
            "A valid Microsoft Entra group security identifier is required.");
    }

    private static string FormatDate(DateOnly date) =>
        $"{date:yyyy-MM-dd}T00:00:00Z";
}
