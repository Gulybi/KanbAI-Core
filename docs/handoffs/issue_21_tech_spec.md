# Technical Specification: Issue #21 - Implement User Domain Entity

## Overview
Create the first concrete domain entity (`User`) that inherits from `BaseEntity`. The `User` entity represents a registered user of the KanbAI platform and serves as the foundational model for all future entities (boards, tasks, comments) which will reference it via foreign keys.

## Database/Domain Design

### 1. `UserRole` Enum

**File:** `KanbAI-Core/KanbAI-Core/Models/Enums/UserRole.cs`
**Namespace:** `KanbAI_Core.Models.Enums`

```csharp
namespace KanbAI_Core.Models.Enums;

public enum UserRole
{
    Member = 0,
    Admin = 1
}
```

**Rationale:** An enum provides compile-time safety and prevents invalid role values. `Member` is the zero-default so that any uninitialized `UserRole` field defaults to the least-privileged role (principle of least privilege). Additional roles (e.g., `ProjectManager`, `Viewer`) can be added in future iterations without schema changes — EF Core stores the enum as an `int` column.

### 2. `User` Entity

**File:** `KanbAI-Core/KanbAI-Core/Models/Entities/User.cs`
**Namespace:** `KanbAI_Core.Models.Entities`

```csharp
namespace KanbAI_Core.Models.Entities;

public class User : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
}
```

**Inherited from `BaseEntity`:** `Id` (Guid), `CreatedAt` (DateTimeOffset), `UpdatedAt` (DateTimeOffset).

**Design Notes:**
- Properties use `string.Empty` defaults to avoid null-reference issues while still requiring values at the database level via EF Core configuration.
- `PasswordHash` stores only hashed values — the property name makes this intent explicit. Plaintext passwords must never be stored.
- `Role` defaults to `UserRole.Member` (enum default = 0) — no explicit initializer needed.
- The class is intentionally NOT a `record` because it is a mutable EF Core entity tracked by the change tracker, not an immutable value object/DTO.

### 3. EF Core Entity Configuration

**File:** `KanbAI-Core/KanbAI-Core/Data/Configurations/UserConfiguration.cs`
**Namespace:** `KanbAI_Core.Data.Configurations`

```csharp
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KanbAI_Core.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(u => u.Email)
            .IsUnique();

        builder.Property(u => u.PasswordHash)
            .IsRequired();

        builder.Property(u => u.Role)
            .IsRequired()
            .HasDefaultValue(UserRole.Member);
    }
}
```

**Constraint Rationale:**
| Property | Constraint | Reason |
|---|---|---|
| `Name` | Required, MaxLength(150) | Display name cannot be blank; 150 chars is generous for international names. |
| `Email` | Required, MaxLength(256), Unique Index | RFC 5321 allows up to 254 chars; 256 provides safe ceiling. Uniqueness prevents duplicate accounts. |
| `PasswordHash` | Required, no MaxLength | Hash length varies by algorithm (bcrypt ~60, Argon2 ~97+); no artificial cap avoids future breakage. |
| `Role` | Required, Default `Member` | Every user must have a role. Default to least privilege. |

### 4. `ApplicationDbContext` Update

**File:** `KanbAI-Core/KanbAI-Core/Data/ApplicationDbContext.cs`

Add a `DbSet<User>` property and apply entity configurations from the assembly:

```csharp
public DbSet<User> Users => Set<User>();

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
}
```

**Note:** `ApplyConfigurationsFromAssembly` automatically discovers all `IEntityTypeConfiguration<T>` implementations in the assembly. This pattern scales to future entities without modifying `ApplicationDbContext` each time a new configuration is added.

### 5. Migration

**Name:** `AddUserEntity`

The migration will produce a `Users` table with the following schema:

| Column | SQL Type | Nullable | Constraints |
|---|---|---|---|
| `Id` | `uniqueidentifier` | NO | Primary Key |
| `Name` | `nvarchar(150)` | NO | |
| `Email` | `nvarchar(256)` | NO | Unique Index `IX_Users_Email` |
| `PasswordHash` | `nvarchar(max)` | NO | |
| `Role` | `int` | NO | Default: `0` (Member) |
| `CreatedAt` | `datetimeoffset` | NO | |
| `UpdatedAt` | `datetimeoffset` | NO | |

## API Contracts
No new API endpoints are required for this issue. The `User` entity is a domain model only at this stage.

## Application Layer Boundaries
No service interfaces, MediatR handlers, or DTOs are required for this issue. Those will be introduced in subsequent issues (registration, authentication, user management).

## Implementation Steps for @agent_developer

Follow these steps strictly in order, referencing `@rule_code_standards`:

1. **Create `UserRole` enum:**
   - Create the folder `KanbAI-Core/KanbAI-Core/Models/Enums/` if it does not exist.
   - Create `UserRole.cs` with the enum definition as specified above.
   - Use file-scoped namespace.

2. **Create `User` entity:**
   - Create `User.cs` in `KanbAI-Core/KanbAI-Core/Models/Entities/`.
   - Inherit from `BaseEntity`.
   - Add `using KanbAI_Core.Models.Enums;` for the `UserRole` reference.
   - Add the four properties: `Name`, `Email`, `PasswordHash`, `Role`.
   - Use file-scoped namespace.

3. **Create `UserConfiguration`:**
   - Create the folder `KanbAI-Core/KanbAI-Core/Data/Configurations/` if it does not exist.
   - Create `UserConfiguration.cs` implementing `IEntityTypeConfiguration<User>`.
   - Apply all constraints from the table above.
   - Use file-scoped namespace.

4. **Update `ApplicationDbContext`:**
   - Add `public DbSet<User> Users => Set<User>();` property.
   - Add `using KanbAI_Core.Models.Entities;` if not already present (it already is for `BaseEntity`).
   - Override `OnModelCreating` to call `modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);`.

5. **Generate EF Core Migration:**
   - From the `KanbAI-Core/KanbAI-Core` project directory, run:
     ```
     dotnet ef migrations add AddUserEntity
     ```
   - Verify the generated migration creates the `Users` table with all expected columns and constraints.

6. **Build and verify:**
   - Run `dotnet build` from the solution directory to ensure the solution compiles without errors.
   - Run `dotnet test` to confirm all existing tests continue to pass.

## QA Guidance for @agent_tester_qa

### Unit Tests Required

**File:** `KanbAI-Core.Tests/Models/Entities/UserTests.cs`

| Test Name | Category | Description |
|---|---|---|
| `User_InheritsFromBaseEntity` | Happy Path | Verify `typeof(User).IsSubclassOf(typeof(BaseEntity))` |
| `User_NameProperty_IsStringType` | Happy Path | Verify property type via reflection |
| `User_EmailProperty_IsStringType` | Happy Path | Verify property type via reflection |
| `User_PasswordHashProperty_IsStringType` | Happy Path | Verify property type via reflection |
| `User_RoleProperty_IsUserRoleType` | Happy Path | Verify property type via reflection |
| `User_AllProperties_HavePublicGettersAndSetters` | Happy Path | Verify all 4 properties + inherited are publicly accessible |
| `User_DefaultPropertyValues_AreExpected` | Edge Case | New `User()` should have `string.Empty` for strings, `UserRole.Member` for role |
| `User_CastToBaseEntity_RetainsPropertyValues` | Edge Case | Set all properties, cast to `BaseEntity`, verify inherited props retained |

**File:** `KanbAI-Core.Tests/Models/Enums/UserRoleTests.cs`

| Test Name | Category | Description |
|---|---|---|
| `UserRole_MemberValue_IsZero` | Happy Path | `(int)UserRole.Member` should equal `0` |
| `UserRole_AdminValue_IsOne` | Happy Path | `(int)UserRole.Admin` should equal `1` |
| `UserRole_DefaultValue_IsMember` | Edge Case | `default(UserRole)` should equal `UserRole.Member` |
| `UserRole_IsEnum` | Happy Path | `typeof(UserRole).IsEnum` should be true |

**File:** `KanbAI-Core.Tests/Data/Configurations/UserConfigurationTests.cs` (using InMemory provider)

| Test Name | Category | Description |
|---|---|---|
| `UserConfiguration_Email_HasUniqueIndex` | Happy Path | Adding two users with the same email should throw on `SaveChangesAsync` |
| `UserConfiguration_Name_IsRequired` | Happy Path | A user with null/empty name should fail validation |
| `UserConfiguration_Email_IsRequired` | Happy Path | A user with null/empty email should fail validation |
| `UserConfiguration_PasswordHash_IsRequired` | Happy Path | A user with null/empty password hash should fail validation |
| `UserConfiguration_Role_DefaultsToMember` | Edge Case | A user saved without explicit role should have `Member` after retrieval |

**File:** `KanbAI-Core.Tests/Data/ApplicationDbContextTests.cs` (extend existing)

| Test Name | Category | Description |
|---|---|---|
| `ApplicationDbContext_UsersDbSet_IsNotNull` | Happy Path | Verify `context.Users` returns a non-null `DbSet<User>` |

### Test Infrastructure Notes
- Use `InMemoryDatabase` for all unit tests (consistent with existing patterns in `SaveChangesTimestampTests.cs`).
- For configuration tests that need the full `ApplicationDbContext` with configurations applied, create a test helper `DbContext` that inherits from `ApplicationDbContext` and calls `OnModelCreating` (or directly use `ApplicationDbContext` with InMemory options).
- Follow the `MethodName_StateUnderTest_ExpectedBehavior` naming convention per `@rule_testing_observability`.
- Follow the AAA pattern with blank-line separators per existing test conventions.
- No integration tests (`WebApplicationFactory`) are required for this issue as there are no API endpoints.

## Known Caveats
- **InMemory provider limitations:** The InMemory provider does not enforce unique indexes or max-length constraints. Tests for unique email constraint should catch `DbUpdateException` or use SQLite in-memory as an alternative if strict constraint validation is needed.
- **Migration tests:** The existing `MigrationTests.cs` references `InitialCreate` by type. The new migration (`AddUserEntity`) may also be tested for existence and correct `[Migration]` attribute if the QA agent deems it valuable.

## Development Status

**Developer:** @agent_developer
**Date:** 2026-04-08

### Files Created
| File | Purpose |
|---|---|
| `KanbAI-Core/KanbAI-Core/Models/Enums/UserRole.cs` | `UserRole` enum with `Member = 0` and `Admin = 1` values |
| `KanbAI-Core/KanbAI-Core/Models/Entities/User.cs` | `User` entity inheriting from `BaseEntity` with `Name`, `Email`, `PasswordHash`, `Role` properties |
| `KanbAI-Core/KanbAI-Core/Data/Configurations/UserConfiguration.cs` | EF Core fluent configuration with constraints (required fields, max lengths, unique email index, default role) |
| `KanbAI-Core/KanbAI-Core/Data/DesignTimeDbContextFactory.cs` | `IDesignTimeDbContextFactory<ApplicationDbContext>` to enable EF CLI migration generation without starting the full app host (needed due to WDAC policy blocking Scalar.AspNetCore.dll at runtime) |
| `KanbAI-Core/KanbAI-Core/Migrations/20260408175346_AddUserEntity.cs` | EF Core migration creating `Users` table with all expected columns, constraints, and `IX_Users_Email` unique index |
| `KanbAI-Core/KanbAI-Core/Migrations/20260408175346_AddUserEntity.Designer.cs` | Auto-generated migration designer file |

### Files Modified
| File | Change |
|---|---|
| `KanbAI-Core/KanbAI-Core/Data/ApplicationDbContext.cs` | Added `DbSet<User> Users` property and `OnModelCreating` override with `ApplyConfigurationsFromAssembly` |
| `KanbAI-Core/KanbAI-Core/Migrations/ApplicationDbContextModelSnapshot.cs` | Auto-updated by EF Core to include `User` entity model |

### Build & Test Results
- **Build:** 0 errors, 0 warnings.
- **Unit tests:** All 71 non-integration tests pass.
- **Integration tests (22):** Fail due to pre-existing WDAC policy blocking `Scalar.AspNetCore.dll` — not related to this change.

### Edge Cases for QA
- `DesignTimeDbContextFactory` was created as infrastructure to support `dotnet ef` tooling. It is not part of the application runtime pipeline. QA may verify it does not affect DI registration or test infrastructure.
- The `OnModelCreating` override uses `ApplyConfigurationsFromAssembly`, which will auto-discover any future `IEntityTypeConfiguration<T>` implementations. Existing tests using `InMemory` provider with subclassed contexts (e.g., `TimestampTestDbContext` in `SaveChangesTimestampTests`) should be verified to still work correctly (confirmed passing).

---

## Testing Status

**QA Engineer:** @agent_tester_qa
**Date:** 2026-04-08

### Test Infrastructure
- Added `Microsoft.EntityFrameworkCore.Sqlite` (v10.0.5) to the test project for constraint enforcement. The InMemory provider does not enforce unique indexes or NOT NULL constraints, so SQLite in-memory is used for `UserConfigurationTests`.
- All other unit tests use the existing InMemory provider, consistent with project conventions.

### Test Files Created/Modified

| File | Tests | Status |
|---|---|---|
| `KanbAI-Core.Tests/Models/Entities/UserTests.cs` | 8 | All Pass |
| `KanbAI-Core.Tests/Models/Enums/UserRoleTests.cs` | 4 | All Pass |
| `KanbAI-Core.Tests/Data/Configurations/UserConfigurationTests.cs` | 5 | All Pass |
| `KanbAI-Core.Tests/Data/ApplicationDbContextTests.cs` (extended) | 1 new | Pass |

### Test Results

| Test Name | Category | Status |
|---|---|---|
| `User_InheritsFromBaseEntity` | Happy Path | Pass |
| `User_NameProperty_IsStringType` | Happy Path | Pass |
| `User_EmailProperty_IsStringType` | Happy Path | Pass |
| `User_PasswordHashProperty_IsStringType` | Happy Path | Pass |
| `User_RoleProperty_IsUserRoleType` | Happy Path | Pass |
| `User_AllProperties_HavePublicGettersAndSetters` | Happy Path | Pass |
| `User_DefaultPropertyValues_AreExpected` | Edge Case | Pass |
| `User_CastToBaseEntity_RetainsPropertyValues` | Edge Case | Pass |
| `UserRole_MemberValue_IsZero` | Happy Path | Pass |
| `UserRole_AdminValue_IsOne` | Happy Path | Pass |
| `UserRole_DefaultValue_IsMember` | Edge Case | Pass |
| `UserRole_IsEnum` | Happy Path | Pass |
| `UserConfiguration_Email_HasUniqueIndex` | Happy Path | Pass |
| `UserConfiguration_Name_IsRequired` | Happy Path | Pass |
| `UserConfiguration_Email_IsRequired` | Happy Path | Pass |
| `UserConfiguration_PasswordHash_IsRequired` | Happy Path | Pass |
| `UserConfiguration_Role_DefaultsToMember` | Edge Case | Pass |
| `ApplicationDbContext_UsersDbSet_IsNotNull` | Happy Path | Pass |

**Total: 18 new tests, 18 passed, 0 failed.**

### Full Suite Summary
- **Total:** 111 tests
- **Passed:** 89
- **Failed:** 22 (all pre-existing WDAC policy failures in integration tests — unrelated to this change)
- **New tests added:** 18 (all passing)

### Coverage Gaps
- None identified. All acceptance criteria are covered by the test suite. Max-length constraints (`Name` max 150, `Email` max 256) are not tested because SQLite does not enforce `nvarchar(N)` max lengths — this is a known SQLite limitation and would require SQL Server integration tests to validate.
