# Context Handoff: Issue #23 - Implement Kanban Board Domain (Columns & Tasks)

## Title & Business Value

**Title:** Implement Kanban Board Domain (Columns & Tasks)
**Who:** Platform users who organise their work visually using Kanban boards within projects, and the engineering team building task-management features on top of this domain model.
**Why:** The Kanban board is the core interaction surface of KanbAI. Without column and task entities, there is no way to represent the visual layout of work items, their ordering, or their assignment to collaborators. These entities are the structural backbone on which all task-level features (drag-and-drop reordering, assignment, comments, attachments) will be built.

## Current State vs. Desired State

### Current State

- An abstract `BaseEntity` class exists in `Models/Entities/BaseEntity.cs` providing `Id` (Guid), `CreatedAt`, and `UpdatedAt` properties, auto-stamped by `ApplicationDbContext.SaveChangesAsync`.
- A `User` entity exists in `Models/Entities/User.cs` with `Name`, `Email`, `PasswordHash`, `Role`, and `ICollection<ProjectMember> ProjectMemberships`.
- A `Project` entity exists in `Models/Entities/Project.cs` with `Name` and `ICollection<ProjectMember> Members`.
- A `ProjectMember` junction entity exists in `Models/Entities/ProjectMember.cs` modelling the N:M User-Project relationship with a `ProjectRole`.
- `ApplicationDbContext` in `Data/ApplicationDbContext.cs` exposes `DbSet<User>`, `DbSet<Project>`, and `DbSet<ProjectMember>`. Configurations are auto-discovered via `ApplyConfigurationsFromAssembly`.
- Entity configurations follow the `IEntityTypeConfiguration<T>` pattern in `Data/Configurations/`.
- Enums live in `Models/Enums/`.
- There are **no** `BoardColumn` or `KanbanTask` entities, no board-related enums, and no column/task tables in the migration snapshot.

### Desired State

- A `BoardColumn` entity exists that represents a named, ordered column within a project's Kanban board (e.g., "To Do", "In Progress", "Done").
- A `KanbanTask` entity exists (named to avoid conflict with `System.Threading.Tasks.Task`) that represents a work item placed within a column, optionally assigned to a user.
- Both entities inherit from `BaseEntity` and follow the same patterns as `Project` and `ProjectMember`.
- Ordering properties (`ColumnOrder` on `BoardColumn`, `TaskOrder` on `KanbanTask`) allow the front-end to render columns and tasks in the correct sequence.
- Foreign key relationships connect columns to their project, tasks to their column, and tasks to an optionally assigned user.
- Navigation properties on all related entities allow traversal of the object graph (e.g., `Project` → `Columns`, `BoardColumn` → `Tasks`, `KanbanTask` → `Column`, `KanbanTask` → `AssignedUser`).
- The new entities are registered in `ApplicationDbContext` and configured via fluent API following the existing `IEntityTypeConfiguration<T>` pattern.

## Milestone Context

**Milestone:** Database Models & EF Core Setup (Domain Entities)

| # | Issue | State | Relationship |
|---|-------|-------|--------------|
| 21 | Implement User Domain Entity | Closed | Prerequisite (delivered `User`, `UserRole`, `UserConfiguration`) |
| 22 | Implement Project Domain and N:M Relationship | Closed/In-progress | Prerequisite (delivered `Project`, `ProjectMember`, `ProjectRole`) |
| 23 | **Implement Kanban Board Domain (Columns & Tasks)** | **Open (this issue)** | — |
| 24 | Implement Attachments (Assets) and Comments Domains | Open | Depends on #23 (comments/attachments attach to tasks) |
| 25 | Configure AppDbContext with Fluent API | Open | Depends on #22–#24 |
| 26 | Generate Initial EF Core Migration and Apply to Database | Open | Depends on #25 |

Issue #23 depends on the `Project` and `User` entities from #21 and #22. Issue #24 (Attachments & Comments) will depend on the `KanbanTask` entity introduced here.

### Reference Schema (from Issue Comment)

The issue owner provided a SQL reference schema in a comment to guide the design intent:

- **BoardColumns:** ProjectId (FK → Projects), Name, ColorCode, ColumnOrder
- **Tasks:** ColumnId (FK → BoardColumns), Title, Content, AssignedId (FK → Users, nullable), TaskOrder

This schema is a design reference for the *what*, not the *how*. The actual implementation should follow the project's existing entity patterns (Guid PKs via `BaseEntity`, fluent API configurations, etc.).

## Acceptance Criteria

### BoardColumn Entity

- [ ] A `BoardColumn` entity class exists that inherits from `BaseEntity`.
- [ ] The `BoardColumn` entity includes a `Name` property representing the column's display name.
- [ ] The `BoardColumn` entity includes a `ColorCode` property representing an optional visual colour identifier for the column.
- [ ] The `BoardColumn` entity includes a `ColumnOrder` property representing the column's position within the board.
- [ ] The `BoardColumn` entity includes a foreign key property linking it to a `Project`.
- [ ] Navigation from a `BoardColumn` to its parent `Project` is supported.
- [ ] Navigation from a `BoardColumn` to its collection of tasks is supported.

### KanbanTask Entity

- [ ] A `KanbanTask` entity class exists that inherits from `BaseEntity`.
- [ ] The entity is named `KanbanTask` (not `Task`) to avoid conflict with `System.Threading.Tasks.Task`.
- [ ] The `KanbanTask` entity includes a `Title` property representing the task's display name.
- [ ] The `KanbanTask` entity includes a `Content` property representing an optional detailed description of the task.
- [ ] The `KanbanTask` entity includes a `TaskOrder` property representing the task's position within its column.
- [ ] The `KanbanTask` entity includes a foreign key property linking it to a `BoardColumn`.
- [ ] The `KanbanTask` entity includes an optional foreign key property (`AssignedId`) linking it to a `User` who is assigned to the task.
- [ ] Navigation from a `KanbanTask` to its parent `BoardColumn` is supported.
- [ ] Navigation from a `KanbanTask` to its assigned `User` is supported (the assigned user may be null).

### Navigation on Existing Entities

- [ ] Navigation from a `Project` to its collection of `BoardColumn`s is supported.
- [ ] Navigation from a `User` to the collection of `KanbanTask`s assigned to them is supported.

### Registration & Conventions

- [ ] Both new entity classes are placed in the `Models/Entities` folder, consistent with existing entity locations.
- [ ] Both new entities are registered in `ApplicationDbContext` via `DbSet<>` properties.
- [ ] The solution compiles and all existing tests continue to pass.

### Edge Cases & Constraints

- [ ] A `BoardColumn` cannot exist without a parent `Project` (the project reference is required).
- [ ] A `KanbanTask` cannot exist without a parent `BoardColumn` (the column reference is required).
- [ ] A `KanbanTask` may exist without an assigned user (the `AssignedId` is optional/nullable).
- [ ] If a `Project` is removed, its associated columns are also removed (cascading behaviour).
- [ ] If a `BoardColumn` is removed, its associated tasks are also removed (cascading behaviour).
- [ ] If an assigned `User` is removed, the task is not removed; instead, the assignment is cleared or the deletion is restricted.

### AC Validation

| # | Criterion Summary | Testable | Specific | Independent | Impl-Free | Complete |
|---|-------------------|----------|----------|-------------|-----------|----------|
| 1 | BoardColumn inherits BaseEntity | ✅ | ✅ | ✅ | ✅ | ✅ |
| 2 | BoardColumn has Name property | ✅ | ✅ | ✅ | ✅ | ✅ |
| 3 | BoardColumn has ColorCode property | ✅ | ✅ | ✅ | ✅ | ✅ |
| 4 | BoardColumn has ColumnOrder property | ✅ | ✅ | ✅ | ✅ | ✅ |
| 5 | BoardColumn FK to Project | ✅ | ✅ | ✅ | ✅ | ✅ |
| 6 | BoardColumn navigates to Project | ✅ | ✅ | ✅ | ✅ | ✅ |
| 7 | BoardColumn navigates to tasks | ✅ | ✅ | ✅ | ✅ | ✅ |
| 8 | KanbanTask inherits BaseEntity | ✅ | ✅ | ✅ | ✅ | ✅ |
| 9 | Named KanbanTask (not Task) | ✅ | ✅ | ✅ | ✅ | ✅ |
| 10 | KanbanTask has Title | ✅ | ✅ | ✅ | ✅ | ✅ |
| 11 | KanbanTask has Content (optional) | ✅ | ✅ | ✅ | ✅ | ✅ |
| 12 | KanbanTask has TaskOrder | ✅ | ✅ | ✅ | ✅ | ✅ |
| 13 | KanbanTask FK to BoardColumn | ✅ | ✅ | ✅ | ✅ | ✅ |
| 14 | KanbanTask optional FK to User | ✅ | ✅ | ✅ | ✅ | ✅ |
| 15 | KanbanTask navigates to BoardColumn | ✅ | ✅ | ✅ | ✅ | ✅ |
| 16 | KanbanTask navigates to assigned User | ✅ | ✅ | ✅ | ✅ | ✅ |
| 17 | Project navigates to BoardColumns | ✅ | ✅ | ✅ | ✅ | ✅ |
| 18 | User navigates to assigned tasks | ✅ | ✅ | ✅ | ✅ | ✅ |
| 19 | Entities in Models/Entities folder | ✅ | ✅ | ✅ | ✅ | ✅ |
| 20 | Entities registered in DbContext | ✅ | ✅ | ✅ | ✅ | ✅ |
| 21 | Solution compiles, tests pass | ✅ | ✅ | ✅ | ✅ | ✅ |
| 22 | BoardColumn requires Project | ✅ | ✅ | ✅ | ✅ | ✅ |
| 23 | KanbanTask requires BoardColumn | ✅ | ✅ | ✅ | ✅ | ✅ |
| 24 | KanbanTask allows null assigned user | ✅ | ✅ | ✅ | ✅ | ✅ |
| 25 | Cascade delete: Project → Columns | ✅ | ✅ | ✅ | ✅ | ✅ |
| 26 | Cascade delete: Column → Tasks | ✅ | ✅ | ✅ | ✅ | ✅ |
| 27 | User deletion does not cascade to tasks | ✅ | ✅ | ✅ | ✅ | ✅ |
