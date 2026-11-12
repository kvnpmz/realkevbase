using System.Text;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace RealKevbase.Api.Auth;

/// <summary>
/// Registration helpers for the API's own bearer-token authentication.
/// </summary>
public static class ApiAuthServiceCollectionExtensions
{
    /// <summary>
    /// Registers the token endpoint services and, when enabled, JWT bearer authentication.
    /// </summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configuration">Application configuration containing the <c>ApiAuth</c> section.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
    public static IServiceCollection AddApiTokenAuth(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(ApiTokenOptions.SectionName);
        services.Configure<ApiTokenOptions>(section);
        services.AddSingleton<ApiTokenService>();

        var options = section.Get<ApiTokenOptions>() ?? new ApiTokenOptions();
        if (!options.Enabled)
        {
            // Anonymous API: no scheme, no authorization services, nothing to challenge.
            return services;
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                // Tokens are minted by ApiTokenService with these exact claims; do not rewrite
                // "sub" into a long ClaimTypes URI, callers rely on the registered name.
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub"
                };
            });

        services.AddAuthorization();

        return services;
    }
}
