using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OrderFlow.Platform.Security;

/// <summary>
/// Bridges our <see cref="JwtTokenService"/> into ASP.NET Core's authentication
/// pipeline, so the framework's [Authorize] / [Authorize(Roles = ...)] attributes
/// and authorization policies enforce RBAC exactly as they would with JwtBearer.
/// </summary>
public sealed class JwtAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Bearer";
    private readonly JwtTokenService _tokens;

    public JwtAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        JwtTokenService tokens)
        : base(options, logger, encoder)
    {
        _tokens = tokens;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var header))
            return Task.FromResult(AuthenticateResult.NoResult());

        var value = header.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var token = value["Bearer ".Length..].Trim();
        if (!_tokens.TryValidate(token, out var principal) || principal is null)
            return Task.FromResult(AuthenticateResult.Fail("Invalid or expired token."));

        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public static class SecurityRegistration
{
    /// <summary>
    /// Wires JWT auth + role-based authorization. Adds the token service, the
    /// seeded user store, the custom bearer handler, and named RBAC policies.
    /// </summary>
    public static IServiceCollection AddOrderFlowSecurity(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.SectionName));
        services.AddSingleton<JwtTokenService>();
        services.AddSingleton<UserStore>();

        services.AddAuthentication(JwtAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, JwtAuthenticationHandler>(
                JwtAuthenticationHandler.SchemeName, _ => { });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminOnly", p => p.RequireRole(Roles.Admin));
            options.AddPolicy("Customers", p => p.RequireRole(Roles.Customer, Roles.Admin));
        });

        return services;
    }
}
