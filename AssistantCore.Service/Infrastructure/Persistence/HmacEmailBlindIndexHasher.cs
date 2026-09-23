using System.Security.Cryptography;
using System.Text;
using AssistantCore.Repository.Persistence;
using AssistantCore.Service.Application.Configuration;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Infrastructure.Persistence;

public sealed class HmacEmailBlindIndexHasher(IOptions<MemberPiiOptions> options) : IEmailBlindIndexHasher
{
    public string ComputeHash(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var normalized = email.Trim().ToLowerInvariant();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.Value.EmailLookupHmacKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash);
    }
}
