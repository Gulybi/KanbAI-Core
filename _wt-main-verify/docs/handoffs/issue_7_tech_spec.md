# Technical Specification: Issue #7 - Configure Entity Framework Core with Local Database

## Overview
Integrate Entity Framework Core with a local SQL Server database into the .NET Web API project (`KanbAI-Core/KanbAI-Core`).

## Database/Domain Design
- **Provider:** Microsoft.EntityFrameworkCore.SqlServer
- **Tools:** Microsoft.EntityFrameworkCore.Design, Microsoft.EntityFrameworkCore.Tools
- **Context:** Create `ApplicationDbContext` inheriting from `DbContext` in the `Data` folder.
- **Connection String:** Add `DefaultConnection` to `appsettings.Development.json` pointing to the local SQL Server instance.

## API Contracts
- No new API endpoints are required for this issue.

## Application Layer Boundaries
- `ApplicationDbContext` will be registered in the dependency injection container in `Program.cs`.
- The connection string will be retrieved from the configuration.

## Implementation Steps for @agent_developer
1. **Install NuGet Packages:**
   - Navigate to the `KanbAI-Core/KanbAI-Core` project directory.
   - Install `Microsoft.EntityFrameworkCore.SqlServer`.
   - Install `Microsoft.EntityFrameworkCore.Design`.
   - Install `Microsoft.EntityFrameworkCore.Tools`.
2. **Configure Connection String:**
   - Open `KanbAI-Core/KanbAI-Core/appsettings.Development.json`.
   - Add a `ConnectionStrings` section with a `DefaultConnection` pointing to the local SQL Server (e.g., `Server=localhost;Database=KanbAI;Trusted_Connection=True;TrustServerCertificate=True;`).
3. **Create DbContext:**
   - In the `KanbAI-Core/KanbAI-Core/Data` directory, create a new class named `ApplicationDbContext.cs`.
   - Inherit from `Microsoft.EntityFrameworkCore.DbContext`.
   - Add a constructor that accepts `DbContextOptions<ApplicationDbContext>` and passes it to the base class.
4. **Register DbContext:**
   - Open `KanbAI-Core/KanbAI-Core/Program.cs`.
   - Add the necessary `using` statements for EF Core and the `Data` namespace.
   - Register `ApplicationDbContext` in the DI container using `builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));`.
5. **Create Initial Migration:**
   - Run the EF Core CLI command to create the initial migration (e.g., `dotnet ef migrations add InitialCreate`).
   - *Note: If `dotnet ef` tools are not installed globally, install them first using `dotnet tool install --global dotnet-ef`.*
6. **Apply Migration (Optional/Verification):**
   - Run the command to update the database (e.g., `dotnet ef database update`) to verify the connection and schema creation.

## Development Status
- **NuGet Packages:** Added `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Design`, and `Microsoft.EntityFrameworkCore.Tools` to `KanbAI-Core.csproj`.
- **Connection String:** Configured `DefaultConnection` in `appsettings.Development.json`.
- **DbContext:** Created `ApplicationDbContext.cs` in the `Data` folder.
- **DI Registration:** Registered `ApplicationDbContext` with SQL Server in `Program.cs`.
- **Initial Migration:** `dotnet ef migrations add InitialCreate` executed successfully. Migration files created in the `Migrations` folder.
- **Database Update:** `dotnet ef database update` executed successfully. The `KanbAI` database schema has been applied to the local SQL Server instance.

## Testing Status

### Test Project
- **Project:** `KanbAI-Core.Tests` (xUnit, added to `KanbAI-Core.sln`)
- **Packages:** xUnit, FluentAssertions, Moq, Microsoft.EntityFrameworkCore.InMemory, Microsoft.AspNetCore.Mvc.Testing

### Test Classes & Cases

#### `Data/ApplicationDbContextTests.cs` (7 tests)
| Test Method | Category | Description |
|---|---|---|
| `Constructor_WithInMemoryOptions_CreatesInstance` | Happy Path | Verifies DbContext can be constructed with valid options |
| `Database_WithInMemoryProvider_CanConnect` | Happy Path | Verifies `CanConnectAsync` returns true |
| `Database_WithInMemoryProvider_EnsureCreatedSucceeds` | Happy Path | Verifies `EnsureCreatedAsync` succeeds |
| `Constructor_InheritsFromDbContext` | Happy Path | Confirms inheritance from `DbContext` |
| `Constructor_WithDifferentDatabaseNames_CreatesIsolatedInstances` | Edge Case | Validates distinct DB names produce isolated instances |
| `Dispose_AfterCreation_DoesNotThrow` | Edge Case | Ensures disposal doesn't throw |
| `SaveChangesAsync_WithNoEntities_ReturnsZero` | Edge Case | Confirms saving with no changes returns 0 |

#### `Configuration/ConnectionStringConfigurationTests.cs` (8 tests)
| Test Method | Category | Description |
|---|---|---|
| `DevelopmentConfig_DefaultConnection_Exists` | Happy Path | Verifies `DefaultConnection` is present |
| `DevelopmentConfig_DefaultConnection_ContainsServerComponent` | Happy Path | Validates `Server=` is in the connection string |
| `DevelopmentConfig_DefaultConnection_ContainsDatabaseName` | Happy Path | Validates `Database=` is in the connection string |
| `DevelopmentConfig_DefaultConnection_UsesTrustServerCertificate` | Happy Path | Confirms `TrustServerCertificate=True` |
| `DevelopmentConfig_DefaultConnection_PointsToLocalhost` | Happy Path | Validates `Server=localhost` for local dev |
| `DevelopmentConfig_DefaultConnection_TargetsKanbAIDatabase` | Happy Path | Validates `Database=KanbAI` |
| `DevelopmentConfig_ConnectionStringsSection_Exists` | Happy Path | Verifies the `ConnectionStrings` section exists |
| `DevelopmentConfig_NonExistentConnection_ReturnsNull` | Failure Mode | Confirms requesting an undefined key returns null |

#### `Configuration/ServiceRegistrationTests.cs` (6 tests)
| Test Method | Category | Description |
|---|---|---|
| `AddDbContext_WithSqlServerProvider_RegistersApplicationDbContext` | Happy Path | Resolves `ApplicationDbContext` from DI |
| `AddDbContext_WithSqlServerProvider_RegistersDbContextOptions` | Happy Path | Resolves `DbContextOptions<ApplicationDbContext>` from DI |
| `AddDbContext_WithScopedLifetime_CreatesDifferentInstancesPerScope` | Edge Case | Validates scoped lifetime yields different instances |
| `AddDbContext_WithSameScope_ReturnsSameInstance` | Edge Case | Validates same scope returns same instance |
| `AddDbContext_WithoutRegistration_ReturnsNull` | Failure Mode | Confirms unregistered context returns null |
| `ResolvedDbContext_CanPerformBasicOperations` | Happy Path | Verifies DI-resolved context can connect |

#### `Data/MigrationTests.cs` (4 tests)
| Test Method | Category | Description |
|---|---|---|
| `InitialCreate_Exists_AndIsAnnotatedWithCorrectMigrationId` | Happy Path | Verifies `[Migration]` attribute and ID |
| `InitialCreate_IsAnnotatedWithDbContextType` | Happy Path | Confirms `[DbContext]` references `ApplicationDbContext` |
| `InitialCreate_InheritsFromMigration` | Happy Path | Validates inheritance from `Migration` |
| `ApplicationDbContextModelSnapshot_Exists_AndReferencesApplicationDbContext` | Happy Path | Verifies model snapshot references correct context |

### Coverage Summary
- **Total Tests:** 25
- **Happy Path:** 16
- **Edge Cases:** 5
- **Failure Modes:** 4

### Notes
- All unit tests use `InMemoryDatabase` to avoid hitting a real SQL Server instance, per `@rule_testing_observability`.
- No production code was modified. The test project is fully isolated.
- The .NET SDK is not installed standalone (Rider bundles its own); tests should be run from JetBrains Rider's test runner.
- `WebApplicationFactory` integration tests were omitted because the implicit `Program` class is internal and modifying production code is prohibited by QA constraints. DI registration is instead validated by manually building `ServiceCollection`.