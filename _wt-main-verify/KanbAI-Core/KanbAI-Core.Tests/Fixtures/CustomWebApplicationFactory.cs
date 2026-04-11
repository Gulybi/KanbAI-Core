using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace KanbAI_Core.Tests.Fixtures;

/// <summary>
/// Shared test host factory that prevents Scalar.AspNetCore from loading.
/// WDAC policy on some machines blocks the unsigned Scalar DLL, which crashes
/// every integration test that boots the full ASP.NET Core pipeline.
/// Setting <c>Testing:SkipScalar = true</c> tells Program.cs to skip
/// <c>MapScalarApiReference()</c>, avoiding the blocked assembly entirely.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Testing:SkipScalar"] = "true"
            });
        });
    }
}
