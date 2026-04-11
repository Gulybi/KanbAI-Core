# Context Handoff: Issue #21 - Implement User Domain Entity

## Title & Business Value
**Title:** Implement User Domain Entity
**Who:** End users of the KanbAI platform and the engineering team building subsequent features (authentication, board ownership, task assignment).
**Why:** The User entity is the primary actor in the KanbAI system. Every future feature—board creation, task management, collaboration, authentication—depends on having a well-defined User model in the domain. Without it, there is no way to associate boards, tasks, or permissions with individuals. Establishing this entity now unblocks the entire downstream domain model and ensures that user identity, credentials, and role information are captured consistently from the start.

## Current State vs. Desired State

**Current State:**
- An abstract `BaseEntity` class exists in `Models/Entities/BaseEntity.cs` providing `Id` (Guid), `CreatedAt`, and `UpdatedAt` properties.
- `ApplicationDbContext` overrides `SaveChangesAsync` to automatically stamp `CreatedAt` and `UpdatedAt` on any tracked `BaseEntity` subclass.
- There are **no concrete domain entity classes** in the project (only test-only subclasses used in unit tests).
- `ApplicationDbContext` declares **no `DbSet<>` properties**, so no tables are generated for any domain entity.
- There is no concept of a "user" anywhere in the production codebase.

**Desired State:**
- A concrete `User` entity class exists, inheriting from `BaseEntity`, that represents a registered user of the platform.
- The `User` entity captures the user's display name, unique email address, hashed password, and role within the system.
- The `User` entity is registered in `ApplicationDbContext` via a `DbSet<User>` so that EF Core can generate and manage the corresponding database table.
- A corresponding EF Core migration is created to materialise the `Users` table in the database.
- The `User` entity serves as the foundational domain model that all future entities (boards, tasks, comments, etc.) will reference via foreign keys.

## Acceptance Criteria
- [ ] A `User` entity class is created that inherits from `BaseEntity`.
- [ ] The `User` class includes a `Name` property for the user's display name.
- [ ] The `User` class includes an `Email` property for the user's unique email address.
- [ ] The `User` class includes a `PasswordHash` property for storing the hashed password (plaintext passwords must never be stored).
- [ ] The `User` class includes a `Role` property to represent the user's role in the system.
- [ ] The `User` class is placed in the established `Models/Entities` folder, consistent with the existing `BaseEntity` location.
- [ ] `ApplicationDbContext` is updated to include a `DbSet<User>` property.
- [ ] An EF Core migration is generated that creates the `Users` table with the appropriate columns and constraints.
- [ ] The solution compiles and all existing tests continue to pass.
- [ ] No plaintext passwords or secrets are stored or exposed by the entity design.
