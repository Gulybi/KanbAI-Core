---
name: ef-core-migration
description: Best practices for EF Core migration generation, design-time context resolution, and post-generation validation. Use when implementing entity changes, DbContext updates, or running dotnet ef migrations.
---

# EF Core Migration Skill

## When to Use

Read this skill whenever an implementation involves:
- Creating or modifying entity classes
- Adding `DbSet<>` properties to `ApplicationDbContext`
- Creating or modifying `IEntityTypeConfiguration<T>` classes
- Generating a new EF Core migration via `dotnet ef migrations add`

## 1. Design-Time Context Resolution

EF Core CLI tools (`dotnet ef`) must create a `DbContext` instance at design time. The tool tries two strategies in order:

1. **Application host startup** — Builds the full app via `Program.cs`. This can fail if:
   - A NuGet package DLL is blocked (e.g., Windows WDAC policy)
   - Environment-specific secrets or connection strings are missing
   - The startup pipeline has middleware that fails outside Kestrel

2. **`IDesignTimeDbContextFactory<T>`** — A factory class the CLI falls back to if host startup fails.

### When to Create a Design-Time Factory

Create `Data/DesignTimeDbContextFactory.cs` if ANY of these conditions apply:
- The application host fails to start during `dotnet ef` (check the error output)
- The project uses middleware or packages that are unavailable at design time
- The CI/CD pipeline runs migrations without full application secrets

### Factory Template

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace KanbAI_Core.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlServer(configuration.GetConnectionString("DefaultConnection"));

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
```

**Key points:**
- The factory bypasses the entire application startup pipeline
- It reads connection strings directly from `appsettings*.json`
- It does NOT register services, middleware, or authentication — only builds the context
- If the factory already exists in the project, do NOT create a duplicate

## 2. Migration Naming Convention

Use descriptive PascalCase names that reflect the domain change:

| Good | Bad |
|------|-----|
| `AddUserEntity` | `Migration1` |
| `AddBoardAndColumnEntities` | `Update` |
| `AddUniqueIndexOnUserEmail` | `Fix` |
| `RemoveObsoleteTaskStatusColumn` | `Changes_April_2026` |

**Rule:** The name should answer "What does this migration do?" without opening the file.

## 3. Migration Generation Command

Always run from the project directory that contains the `DbContext`:

```bash
dotnet ef migrations add {MigrationName}
```

If the project uses a separate startup project:

```bash
dotnet ef migrations add {MigrationName} --startup-project ../StartupProject
```

## 4. Post-Generation Validation

After generating a migration, **always verify** the migration file before proceeding:

### Column Verification

Compare each column in the generated migration against the tech spec's expected schema table:

| Check | What to Verify |
|-------|---------------|
| **Column names** | Do they match entity property names (PascalCase)? |
| **SQL types** | Do they match the expected types from the tech spec (e.g., `nvarchar(150)`, `uniqueidentifier`, `int`)? |
| **Nullability** | Are required columns marked `nullable: false`? |
| **Default values** | Are defaults present where the spec requires them (e.g., enum defaults)? |
| **Max lengths** | Do `HasMaxLength()` constraints produce the expected `nvarchar(N)` sizes? |

### Constraint Verification

| Check | What to Verify |
|-------|---------------|
| **Primary key** | Is the PK correctly defined (usually on `Id`)? |
| **Unique indexes** | Are unique constraints created with the expected index name (e.g., `IX_Users_Email`)? |
| **Foreign keys** | Are FK relationships and cascade behaviors correct? |
| **Table name** | Does the table name match expectations (EF Core uses the `DbSet` property name by default)? |

### Down Migration

Verify the `Down` method correctly reverses the `Up` method (drops the table, removes indexes, etc.).

## 5. Common Pitfalls

| Pitfall | Symptom | Solution |
|---------|---------|----------|
| WDAC blocks a DLL at design time | `FileLoadException: An Application Control policy has blocked this file` | Create a `DesignTimeDbContextFactory` |
| Missing connection string | `Unable to resolve service for DbContextOptions` | Ensure `appsettings.Development.json` has a valid `ConnectionStrings:DefaultConnection` |
| Snapshot conflict | Migration generates unexpected changes | Delete the bad migration with `dotnet ef migrations remove` and regenerate |
| Empty migration | `Up()` and `Down()` methods are empty | The model change was not detected — verify the entity is registered via `DbSet<>` and configurations are applied in `OnModelCreating` |
| `OnModelCreating` not called | Configurations are ignored | Ensure `modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly)` is called in the override |

## 6. Migration in Existing Projects

When a project already has migrations, check the `ApplicationDbContextModelSnapshot.cs` after generation. It should include the new entity's model definition. If the snapshot looks incorrect, the migration will produce wrong SQL — remove and regenerate.

## 7. Applying Migrations

To apply migrations to the database:

```bash
dotnet ef database update
```

To apply up to a specific migration:

```bash
dotnet ef database update {MigrationName}
```

To rollback to a previous state:

```bash
dotnet ef database update {PreviousMigrationName}
```
