---
name: integration-testing
description: Integration testing patterns, WebApplicationFactory setup, known TestServer limitations, and reusable auth bypass snippets for the KanbAI-Core project.
---

# Integration Testing Standards

These rules apply when writing integration tests that use `WebApplicationFactory<Program>`. They capture infrastructure patterns specific to this project's middleware stack so that testing is consistent and efficient.

## 1. 🚨 Known TestServer Limitations

| Limitation | Impact | Mitigation |
|---|---|---|
| **No `IConnectionItemsFeature`** | `NegotiateHandler.HandleRequestAsync()` throws `NotSupportedException` on every request, even if `FallbackPolicy = null`. | Remove all Negotiate auth scheme registrations from DI and replace with a no-op `TestAuthHandler`. |
| **No Kestrel** | HTTPS redirection warns (`Failed to determine the https port for redirect`) but does not fail. | Informational only — safe to ignore in test output. |
| **Implicit `DeveloperExceptionPage`** | In Development mode, the framework auto-adds `DeveloperExceptionPageMiddleware` before user-defined middleware. | Ensure the `GlobalExceptionHandler` always returns appropriate responses. |

### Why `FallbackPolicy = null` Alone Is NOT Enough

`FallbackPolicy` controls **authorization** (policy enforcement). It does NOT prevent the **authentication** middleware from running. `NegotiateHandler` implements `IAuthenticationRequestHandler`, which means `AuthenticationMiddleware` invokes its `HandleRequestAsync()` for every request regardless of the default scheme.

**The fix:** Remove the `IConfigureOptions<AuthenticationOptions>` descriptors that register the Negotiate scheme, then register a clean test scheme. This ensures the Negotiate handler is never in the scheme map and never invoked.

## 2. 🏗️ Standard `CreateClient` Pattern

Every integration test class using `WebApplicationFactory<Program>` should use a helper method like this:

```csharp
private HttpClient CreateClient(string environment = "Development")
{
    return _factory.WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment(environment);
        builder.ConfigureTestServices(services =>
        {
            // 1. Remove all Negotiate auth scheme registrations
            var authConfigs = services
                .Where(d => d.ServiceType == typeof(IConfigureOptions<AuthenticationOptions>))
                .ToList();
            foreach (var descriptor in authConfigs)
                services.Remove(descriptor);

            // 2. Register a no-op test auth scheme
            services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

            // 3. Disable the authorization fallback policy
            services.AddAuthorization(options => options.FallbackPolicy = null);

            // 4. (Optional) Add test-specific IStartupFilter for custom endpoints
        });
    }).CreateClient();
}
```

### Required Using Directives

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
```

## 3. 🔑 No-Op `TestAuthHandler`

```csharp
private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        return Task.FromResult(AuthenticateResult.NoResult());
    }
}
```

Place this as a `private sealed class` inside the test class (nested) to keep test infrastructure co-located with the tests that use it.

## 4. 🎯 Injecting Test-Only Endpoints via `IStartupFilter`

To test middleware (e.g., exception handlers) without modifying production code, register an `IStartupFilter` that inserts inline middleware **after** the original pipeline:

```csharp
private sealed class ThrowingEndpointStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            next(app); // Build the original pipeline first (includes UseExceptionHandler)
            app.Use(nextMiddleware => context =>
            {
                if (context.Request.Path.StartsWithSegments("/test/throw"))
                {
                    throw new InvalidOperationException("Test exception message");
                }
                return nextMiddleware(context);
            });
        };
    }
}
```

**Why `next(app)` is called first:** The test middleware must be registered AFTER `UseExceptionHandler()` in the pipeline so exceptions are caught by the handler under test.

Register the filter in `ConfigureTestServices`:

```csharp
services.AddSingleton<IStartupFilter>(new ThrowingEndpointStartupFilter());
```

## 5. 📐 Test Class Structure Conventions

- **Fixture:** Use `IClassFixture<WebApplicationFactory<Program>>` for shared factory lifecycle.
- **Namespace:** Mirror the production namespace under `KanbAI_Core.Tests` (e.g., `KanbAI_Core.Tests.Middleware`).
- **Regions:** Group tests by concern (`Status Code & Headers`, `Response Body Format`, `Environment-Specific Behavior`, `Regression`, `Test Infrastructure`).
- **Environment parameterization:** Use `[Theory]` with `[InlineData("Development")]` / `[InlineData("Production")]` when the same assertion applies across environments.

## 6. 🧪 Database Testing

- **In-Memory Database:** Use EF Core's in-memory provider for fast unit-style tests.
- **Test Database:** Use a real SQL Server database (LocalDB or container) for integration tests that need to verify actual SQL behavior.
- **Cleanup:** Ensure tests clean up after themselves or use transactions that roll back.
