---
name: pre-implementation-checklist
description: Systematic verification checklist to run before writing any code. Use when beginning implementation of a tech spec to catch missing directories, packages, naming conflicts, and spec ambiguities early.
---

# Pre-Implementation Checklist

## When to Use

Run this checklist after reading a `_tech_spec.md` and before creating or modifying any `.cs` files. The goal is to surface blockers early — a missing folder or package discovered mid-implementation wastes context and forces backtracking.

## The Checklist

### 1. Directory Readiness

For every file path listed in the tech spec's "Implementation Steps" section:

| Check | Action if Failed |
|-------|------------------|
| Does the **target directory** exist? | Create it before writing any files. |
| Does the target directory follow the project's established folder conventions? | Verify against existing structure (e.g., `Models/Entities/`, `Data/Configurations/`, `Models/Enums/`). |

**How to check:**
```bash
# List existing directories
ls -la Models/
ls -la Data/
ls -la DTOs/

# Create missing directories
mkdir -p Models/Enums
mkdir -p Data/Configurations
```

### 2. Package Dependencies

For every NuGet package the tech spec assumes is available:

| Check | Action if Failed |
|-------|------------------|
| Is the package listed in the `.csproj` file? | Add it via `dotnet add package {Name}` before implementation. |
| Is the installed version compatible with the target framework? | Check the `.csproj` `<TargetFramework>` and the package's supported frameworks. |

**How to check:**
```bash
# View installed packages
dotnet list package

# Add missing package
dotnet add package Microsoft.EntityFrameworkCore
dotnet add package FluentValidation
```

### 3. Naming Conflict Scan

For every new class, enum, record, or interface the tech spec defines:

| Check | Action if Failed |
|-------|------------------|
| Does a type with the **same name** already exist anywhere in the project? | Search the codebase (e.g., `class {Name}`, `enum {Name}`, `record {Name}`). If a conflict exists, stop and ask the user to clarify with the Staff Engineer. |
| Does the new **namespace** match the project's `RootNamespace` and folder path convention? | Verify against existing files in the same directory. |

**How to check:**
```bash
# Search for existing type names
grep -r "class UserEntity" --include="*.cs"
grep -r "enum UserStatus" --include="*.cs"
grep -r "record UserDto" --include="*.cs"
```

Or use the Grep tool:
- Pattern: `class {TypeName}` or `enum {TypeName}` or `record {TypeName}`
- Output mode: `files_with_matches`

### 4. Tech Spec Completeness

For every file the tech spec asks you to create or modify:

| Check | Action if Failed |
|-------|------------------|
| Does the spec provide the **exact code** or at minimum the full type signature? | If only a vague description is given, ask the user to clarify with the Staff Engineer. Do not guess. |
| Are **all referenced types** (base classes, enums, interfaces) either already in the codebase or defined elsewhere in the spec? | If a type is referenced but not defined, stop and ask. |
| For modified files: does the spec describe **where** the change goes (property position, method location)? | If ambiguous, read the target file first to determine the correct insertion point. |

### 5. EF Core Readiness (if applicable)

Only run these checks when the tech spec includes entity or migration work:

| Check | Action if Failed |
|-------|------------------|
| Does `ApplicationDbContext` already have an `OnModelCreating` override? | If not, the spec should include adding one. |
| Can `dotnet ef` resolve the DbContext at design time? | If the project lacks a `DesignTimeDbContextFactory` and the app host cannot start (e.g., WDAC policy, missing secrets), create one. See the `ef-core-migration` skill. |
| Are all entity configurations using `IEntityTypeConfiguration<T>` (not inline in `OnModelCreating`)? | Follow existing patterns in `Data/Configurations/`. |

**How to check:**
```bash
# Test EF Core design-time resolution
dotnet ef migrations list

# If it fails, you may need a DesignTimeDbContextFactory
```

### 6. Build Baseline

Before making any changes, verify the solution builds successfully:

```bash
dotnet build
```

If the build fails before you start, document the pre-existing issues so you can distinguish them from issues you introduce.

## Output

After completing the checklist, you should have:
- ✅ All required directories created
- ✅ All required packages installed
- ✅ Confidence that no naming conflicts exist
- ✅ A clear understanding of every file to create or modify
- ✅ EF Core tooling confirmed working (if applicable)
- ✅ Baseline build status documented

Only then proceed to implementation.
