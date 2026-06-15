using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace OrderFlow.Platform.Security;

/// <summary>
/// Self-contained HS256 JWT implementation built only on System.Security.Cryptography,
/// so the whole auth stack compiles with zero NuGet packages. It produces and
/// validates standard, interoperable JWTs (RFC 7519).
///
/// Production deployments can swap this for Microsoft.AspNetCore.Authentication.JwtBearer
/// + Microsoft.IdentityModel.Tokens without changing any controller/authorization code —
/// the issued tokens are wire-compatible.
/// </summary>
public sealed class JwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options) => _options = options.Value;

    public (string Token, int ExpiresInSeconds) CreateToken(string userId, string username, string role)
    {
        var now = DateTimeOffset.UtcNow;
        var exp = now.AddMinutes(_options.ExpiryMinutes);

        var header = new Dictionary<string, object> { ["alg"] = "HS256", ["typ"] = "JWT" };
        var payload = new Dictionary<string, object>
        {
            ["sub"] = userId,
            ["name"] = username,
            ["role"] = role,
            ["iss"] = _options.Issuer,
            ["aud"] = _options.Audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = exp.ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N")
        };

        var encodedHeader = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var encodedPayload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signature = Sign($"{encodedHeader}.{encodedPayload}");

        return ($"{encodedHeader}.{encodedPayload}.{signature}", _options.ExpiryMinutes * 60);
    }

    public bool TryValidate(string token, out ClaimsPrincipal? principal)
    {
        principal = null;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var parts = token.Split('.');
        if (parts.Length != 3) return false;

        var expectedSig = Sign($"{parts[0]}.{parts[1]}");
        // Constant-time comparison to avoid timing side channels.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expectedSig),
                Encoding.ASCII.GetBytes(parts[2])))
            return false;

        Dictionary<string, JsonElement>? claims;
        try
        {
            claims = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Base64UrlDecode(parts[1]));
        }
        catch
        {
            return false;
        }
        if (claims is null) return false;

        if (claims.TryGetValue("exp", out var expEl) &&
            expEl.TryGetInt64(out var exp) &&
            DateTimeOffset.FromUnixTimeSeconds(exp) < DateTimeOffset.UtcNow)
            return false;

        if (claims.TryGetValue("iss", out var issEl) && issEl.GetString() != _options.Issuer) return false;
        if (claims.TryGetValue("aud", out var audEl) && audEl.GetString() != _options.Audience) return false;

        var identity = new ClaimsIdentity(authenticationType: "jwt", nameType: ClaimTypes.Name, roleType: ClaimTypes.Role);
        if (claims.TryGetValue("sub", out var sub)) identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, sub.GetString() ?? ""));
        if (claims.TryGetValue("name", out var name)) identity.AddClaim(new Claim(ClaimTypes.Name, name.GetString() ?? ""));
        if (claims.TryGetValue("role", out var role)) identity.AddClaim(new Claim(ClaimTypes.Role, role.GetString() ?? ""));

        principal = new ClaimsPrincipal(identity);
        return true;
    }

    private string Sign(string input)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.Secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Base64Url(hash);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
