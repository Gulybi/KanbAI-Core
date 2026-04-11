# Context Handoff: Issue #22 - Implement Project Domain and N:M Relationship

## Title & Business Value

**Title:** Implement Project Domain and N:M Relationship
**Who:** Platform users who will create and collaborate on projects, and the engineering team building downstream features (Kanban boards, columns, tasks, comments).
**Why:** The Project is the top-level organisational unit in KanbAI. Every Kanban board, column, task, and attachment ultimately lives within a project. Without a Project entity and a well-defined many-to-many relationship between Users and Projects, there is no way to model collaboration, ownership, or project-scoped permissions. This entity is a prerequisite for all subsequent domain work in the milestone (boards, columns, tasks, attachments, comments).

## Current State vs. Desired State

### Current State

- An abstract `BaseEntity` class exists in `Models/Entities/BaseEntity.cs` providing `Id` (Guid), `CreatedAt`, and `UpdatedAt` properties.
- A concrete `User` entity exists in `Models/Entities/User.cs`, inheriting from `BaseEntity`, with `Name`, `Email`, `PasswordHash`, and `Role` (`UserRole` enum) properties.
- `UserRole` enum exists in `Models/Enums/UserRole.cs` with values `Member` and `Admin`.
- `ApplicationDbContext` in `Data/ApplicationDbContext.cs` exposes `DbSet<User> Users` and auto-stamps `CreatedAt`/`UpdatedAt` via `SaveChangesAsync`.
- `UserConfiguration` in `Data/Configurations/UserConfiguration.cs` defines the fluent API configuration for the `User` entity (primary key, required fields, max lengths, unique email index, default role).
- Entity configurations are auto-discovered via `modelBuilder.ApplyConfigurationsFromAssembly(...)`.
- There is **no** `Project` entity, `ProjectMember` junction entity, or `ProjectRole` type anywhere in the codebase.
- The `User` entity has **no** navigation properties to any other entity.

### Desired State

- A `Project` entity exists representing a collaborative workspace that users can create, join, and manage.
- A `ProjectMember` junction entity exists that explicitly models the many-to-many relationship between `User` and `Project`, carrying a `ProjectRole` to indicate the member's role within that specific project.
- A `ProjectRole` type is defined to distinguish between different levels of project membership (e.g., owner vs. regular member).
- Navigation properties are defined on all three entities (`User`, `Project`, `ProjectMember`) so that the object graph can be traversed in both directions.
- The new entities are registered in `ApplicationDbContext` and configured using the fluent API following the existing pattern (`IEntityTypeConfiguration<T>` classes in `Data/Configurations/`).

## Milestone Context

**Milestone:** Database Models & EF Core Setup (Domain Entities)

| # | Issue | State | Relationship |
|---|-------|-------|--------------|
| 22 | **Implement Project Domain and N:M Relationship** | **Open (this issue)** | — |
| 23 | Implement Kanban Board Domain (Columns & Tasks) | Open | Depends on #22 (boards belong to a project) |
| 24 | Implement Attachments (Assets) and Comments Domains | Open | Depends on #23 |
| 25 | Configure AppDbContext with Fluent API | Open | Depends on #22–#24 (all entities must exist) |
| 26 | Generate Initial EF Core Migration and Apply to Database | Open | Depends on #25 |

**Prerequisite (completed):** Issue #21 — Implement User Domain Entity (delivered the `User` entity, `UserRole` enum, `UserConfiguration`, and `DbSet<User>`).

Issue #22 is the **next sequential step** in the milestone. All subsequent issues (#23–#26) depend directly or transitively on the entities introduced here.

## Acceptance Criteria

- [ ] A `Project` entity class exists that inherits from `BaseEntity`.
- [ ] The `Project` entity includes a `Name` property representing the project's display name.
- [ ] A `ProjectMember` junction entity exists that represents the association between a `User` and a `Project`.
- [ ] The `ProjectMember` entity inherits from `BaseEntity`.
- [ ] The `ProjectMember` entity includes a `ProjectRole` property that indicates the member's role within the specific project.
- [ ] A `ProjectRole` type is defined to distinguish between at least two levels of membership: an owner role and a regular member role.
- [ ] Each `Project` can have many associated members (Users), and each `User` can belong to many Projects (N:M relationship).
- [ ] Navigation from a `Project` to its members is supported.
- [ ] Navigation from a `User` to the projects they belong to is supported.
- [ ] Navigation from a `ProjectMember` to both its associated `User` and `Project` is supported.
- [ ] A user cannot be a member of the same project more than once (the combination of user and project is unique within `ProjectMember`).
- [ ] All new entity classes are placed in the established `Models/Entities` folder, consistent with existing entity locations.
- [ ] Any new enum types are placed in the established `Models/Enums` folder, consistent with the existing `UserRole` location.
- [ ] The new entities are registered in `ApplicationDbContext` via `DbSet<>` properties.
- [ ] The solution compiles and all existing tests continue to pass.

### AC Validation

| # | Criterion Summary | Testable | Specific | Independent | Impl-Free | Complete |
|---|-------------------|----------|----------|-------------|-----------|----------|
| 1 | Project inherits BaseEntity | ✅ | ✅ | ✅ | ✅ | ✅ |
| 2 | Project has Name property | ✅ | ✅ | ✅ | ✅ | ✅ |
| 3 | ProjectMember junction entity exists | ✅ | ✅ | ✅ | ✅ | ✅ |
| 4 | ProjectMember inherits BaseEntity | ✅ | ✅ | ✅ | ✅ | ✅ |
| 5 | ProjectMember has ProjectRole property | ✅ | ✅ | ✅ | ✅ | ✅ |
| 6 | ProjectRole type with owner and member | ✅ | ✅ | ✅ | ✅ | ✅ |
| 7 | N:M relationship between User and Project | ✅ | ✅ | ✅ | ✅ | ✅ |
| 8 | Project navigates to members | ✅ | ✅ | ✅ | ✅ | ✅ |
| 9 | User navigates to projects | ✅ | ✅ | ✅ | ✅ | ✅ |
| 10 | ProjectMember navigates to User and Project | ✅ | ✅ | ✅ | ✅ | ✅ |
| 11 | Unique user-project membership constraint | ✅ | ✅ | ✅ | ✅ | ✅ |
| 12 | Entities in Models/Entities folder | ✅ | ✅ | ✅ | ✅ | ✅ |
| 13 | Enums in Models/Enums folder | ✅ | ✅ | ✅ | ✅ | ✅ |
| 14 | Entities registered in DbContext | ✅ | ✅ | ✅ | ✅ | ✅ |
| 15 | Solution compiles, existing tests pass | ✅ | ✅ | ✅ | ✅ | ✅ |
