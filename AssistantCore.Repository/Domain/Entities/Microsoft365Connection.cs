using AssistantCore.Repository.Domain.Enums;

namespace AssistantCore.Repository.Domain.Entities;

public sealed class Microsoft365Connection
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid OrganizationConnectorId { get; set; }

    public string? TenantId { get; set; }

    public Microsoft365ConnectionStatus Status { get; set; }

    public string? ConsentStateHash { get; set; }

    public DateTimeOffset? ConsentStateExpiresAt { get; set; }

    public DateTimeOffset? ConsentStateConsumedAt { get; set; }

    public DateTimeOffset? ConsentValidatedAt { get; set; }

    public DateTimeOffset? OnboardingCompletedAt { get; set; }

    public string? LastErrorCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public Organization Organization { get; set; } = null!;

    public OrganizationConnector OrganizationConnector { get; set; } = null!;

    public ICollection<Microsoft365Source> Sources { get; set; } = [];

    public void PrepareConsent(
        string stateHash,
        DateTimeOffset stateExpiresAt,
        DateTimeOffset occurredAt)
    {
        Status = Microsoft365ConnectionStatus.PendingConsent;
        ConsentStateHash = stateHash;
        ConsentStateExpiresAt = stateExpiresAt;
        ConsentStateConsumedAt = null;
        LastErrorCode = null;
        UpdatedAt = occurredAt;
    }

    public void Activate(string tenantId, DateTimeOffset occurredAt)
    {
        EnsureStatus(Microsoft365ConnectionStatus.PendingConsent, Microsoft365ConnectionStatus.Active);
        TenantId = tenantId;
        Status = Microsoft365ConnectionStatus.Active;
        ConsentStateConsumedAt = occurredAt;
        ConsentValidatedAt = occurredAt;
        LastErrorCode = null;
        UpdatedAt = occurredAt;
    }

    public void CompleteOnboarding(DateTimeOffset occurredAt)
    {
        if (Status != Microsoft365ConnectionStatus.Active)
        {
            throw new InvalidOperationException("Only an active Microsoft 365 connection can complete onboarding.");
        }

        OnboardingCompletedAt ??= occurredAt;
        UpdatedAt = occurredAt;
    }

    public void MarkError(string errorCode, DateTimeOffset occurredAt)
    {
        if (Status == Microsoft365ConnectionStatus.Revoked)
        {
            throw new InvalidOperationException("A revoked Microsoft 365 connection cannot transition to Error.");
        }

        Status = Microsoft365ConnectionStatus.Error;
        LastErrorCode = errorCode;
        UpdatedAt = occurredAt;
    }

    public void Revoke(DateTimeOffset occurredAt)
    {
        Status = Microsoft365ConnectionStatus.Revoked;
        ConsentStateHash = null;
        ConsentStateExpiresAt = null;
        OnboardingCompletedAt = null;
        LastErrorCode = null;
        UpdatedAt = occurredAt;
    }

    private void EnsureStatus(
        Microsoft365ConnectionStatus expected,
        Microsoft365ConnectionStatus target)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException(
                $"Microsoft 365 connection cannot transition from {Status} to {target}.");
        }
    }
}
