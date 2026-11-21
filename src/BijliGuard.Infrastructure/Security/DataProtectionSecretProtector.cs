using BijliGuard.Application.Billing;
using Microsoft.AspNetCore.DataProtection;

namespace BijliGuard.Infrastructure.Security;

/// <summary>
/// Encrypts consumer references and meter numbers with ASP.NET Data Protection.
/// The database only ever holds ciphertext for these values.
/// </summary>
public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("BijliGuard.Secrets.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
