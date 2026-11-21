using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BijliGuard.Application.Abstractions;
using BijliGuard.Domain.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BijliGuard.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "BijliGuard";
    public string Audience { get; set; } = "BijliGuard.Clients";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}

public sealed class OtpOptions
{
    public const string SectionName = "Otp";

    /// <summary>Server-side secret used to keyed-hash OTP codes. Must come from secrets, never source control.</summary>
    public string Secret { get; set; } = string.Empty;
}

public sealed class OtpService : IOtpService
{
    private readonly byte[] _key;

    public OtpService(IOptions<OtpOptions> options)
    {
        _key = Encoding.UTF8.GetBytes(options.Value.Secret);
    }

    public string GenerateCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString($"D{OtpPolicy.CodeLength}");

    public string HashCode(Guid userId, OtpPurpose purpose, string code)
    {
        var message = Encoding.UTF8.GetBytes($"{userId:N}:{(int)purpose}:{code}");
        return Convert.ToBase64String(HMACSHA256.HashData(_key, message));
    }

    public bool Matches(Guid userId, OtpPurpose purpose, string code, string storedHash)
    {
        var computed = Encoding.UTF8.GetBytes(HashCode(userId, purpose, code));
        var stored = Encoding.UTF8.GetBytes(storedHash);
        return CryptographicOperations.FixedTimeEquals(computed, stored);
    }
}

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _time;
    private readonly JwtSecurityTokenHandler _handler = new();

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
    }

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshTokenDays);

    public (string Token, DateTimeOffset ExpiresAt) IssueAccessToken(UserAccount user, IReadOnlyList<string> roles)
    {
        var now = _time.GetUtcNow();
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        if (!string.IsNullOrWhiteSpace(user.FullName))
            claims.Add(new Claim("name", user.FullName));
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };

        var token = _handler.CreateToken(descriptor);
        return (_handler.WriteToken(token), expires);
    }

    public string GenerateRefreshToken() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(64));

    public string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
