using System.Security.Cryptography;
using System.Text;

namespace BijliGuard.Application.Common;

/// <summary>Produces short, non-reversible identifiers for logs. Raw personal values never reach logs.</summary>
public static class LogSafe
{
    public static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..12];
    }
}
