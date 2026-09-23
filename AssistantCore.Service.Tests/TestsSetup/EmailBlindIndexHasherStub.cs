using AssistantCore.Repository.Persistence;

namespace AssistantCore.Service.Tests;

/// <summary>
/// Deterministic test double: same input always yields the same output, without any
/// real secret. Never wire this into production DI - it exists only so repository
/// tests can construct <c>OrganizationMemberQueries</c> without real HMAC key config.
/// </summary>
internal sealed class StubEmailBlindIndexHasher : IEmailBlindIndexHasher
{
    public string ComputeHash(string email) => email.Trim().ToLowerInvariant();
}
