namespace OrderFlow.Platform.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Secret { get; set; } = "dev-only-super-secret-key-change-me-please-1234567890";
    public string Issuer { get; set; } = "orderflow";
    public string Audience { get; set; } = "orderflow.clients";
    public int ExpiryMinutes { get; set; } = 60;
}

public static class Roles
{
    public const string Customer = "Customer";
    public const string Admin = "Admin";
}

public sealed record AppUser(string UserId, string Username, string Password, string Role);

/// <summary>
/// Demo identity store. In production this is replaced by an identity provider
/// (Entra ID / IdentityServer / ASP.NET Core Identity over SQL Server).
/// </summary>
public sealed class UserStore
{
    private readonly Dictionary<string, AppUser> _byUsername = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alice"] = new("cust-alice", "alice", "password", Roles.Customer),
        ["bob"]   = new("cust-bob",   "bob",   "password", Roles.Customer),
        ["admin"] = new("admin-001",  "admin", "admin",    Roles.Admin),
    };

    public AppUser? Validate(string username, string password)
    {
        if (username is null || password is null) return null;
        return _byUsername.TryGetValue(username, out var user) && user.Password == password
            ? user
            : null;
    }
}
