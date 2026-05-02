using KanbAI_Core.Data;
using KanbAI_Core.Middleware;
using Microsoft.EntityFrameworkCore;

namespace KanbAI_Core.Extensions;

public static class ServiceCollectionExtensions
{
    public const string CorsPolicyName = "AllowAngularFrontend";

    /// <summary>
    /// Registers <see cref="ApplicationDbContext"/> with SQL Server
    /// using the "DefaultConnection" connection string.
    /// </summary>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));
        return services;
    }

    /// <summary>
    /// Registers OpenAPI document generation, the global exception handler,
    /// and ProblemDetails.
    /// </summary>
    public static IServiceCollection AddApiInfrastructure(this IServiceCollection services)
    {
        services.AddControllers();
        services.AddOpenApi();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
        return services;
    }

    /// <summary>
    /// Registers authorization services without a global fallback policy.
    /// Controllers must explicitly declare authorization requirements using [Authorize] or [AllowAnonymous].
    /// </summary>
    public static IServiceCollection AddAuthServices(this IServiceCollection services)
    {
        services.AddAuthorization();
        return services;
    }

    /// <summary>
    /// Registers CORS with the <see cref="CorsPolicyName"/> policy
    /// using allowed origins from configuration.
    /// </summary>
    public static IServiceCollection AddCorsPolicy(
        this IServiceCollection services, IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicyName, policy =>
            {
                policy.WithOrigins(allowedOrigins)
                      .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
                      .WithHeaders("Content-Type", "Authorization");
            });
        });
        return services;
    }
}
