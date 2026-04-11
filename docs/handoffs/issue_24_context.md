# Context Handoff: Issue #24 - Implement Attachments (Assets) and Comments Domains

## Title & Business Value

**Title:** Implement Attachments (Assets) and Comments Domains
**Who:** Platform users who attach files to tasks and discuss work items through comments, and the engineering team building collaboration features on top of these domain models.
**Why:** Attachments and comments are essential collaboration primitives in any task-management tool. Without an `Asset` entity, users cannot associate files (images, documents, screenshots) with tasks. Without a `TaskComment` entity, users cannot have threaded discussions on work items. These entities complete the core domain model required before configuring the full `AppDbContext` and generating migrations.

## Current State vs. Desired State

### Current State

- An abstract `BaseEntity` class exists in `Models/Entities/BaseEntity.cs` providing `Id` (Guid), `CreatedAt`, and `UpdatedAt` properties, auto-stamped by `ApplicationDbContext.SaveChangesAsync`.
- A `User` entity exists in `Models/Entities/User.cs` with `Name`, `Email`, `PasswordHash`, `Role`, navigation to `ProjectMemberships` and `AssignedTasks`.
- A `KanbanTask` entity exists in `Models/Entities/KanbanTask.cs` with `Title`, `Content`, `TaskOrder`, a required FK to `BoardColumn`, and an optional FK to `User` (`AssignedId`).
- `BoardColumn` and `Project` entities exist with their respective relationships fully wired.
- `ApplicationDbContext` in `Data/ApplicationDbContext.cs` exposes `DbSet` properties for `User`, `Project`, `ProjectMember`, `BoardColumn`, and `KanbanTask`. Configurations are auto-discovered via `ApplyConfigurationsFromAssembly`.
- Entity configurations follow the `IEntityTypeConfiguration<T>` pattern in `Data/Configurations/`.
- Enums live in `Models/Enums/`.
- There are **no** `Asset`, `TaskComment`, or attachment/comment-related entities, enums, or configurations anywhere in the codebase.

### Desired State

- An `Asset` entity exists that stores file metadata for attachments uploaded to a task (e.g., storage key, MIME type, file size, original file name, optional thumbnail key, and processing status).
- A `TaskComment` entity exists that represents a user-authored text comment on a task.
- Both entities inherit from `BaseEntity` and follow the same patterns as existing entities (`KanbanTask`, `BoardColumn`, etc.).
- Foreign key relationships connect assets to their parent `KanbanTask` and comments to both their parent `KanbanTask` and the authoring `User`.
- Navigation properties on all related entities allow traversal of the object graph (e.g., `KanbanTask` → `Assets`, `KanbanTask` → `Comments`, `TaskComment` → `Author`, `User` → `AuthoredComments`).
- The new entities are registered in `ApplicationDbContext` and configured via fluent API following the existing `IEntityTypeConfiguration<T>` pattern.

## Milestone Context

**Milestone:** Database Models & EF Core Setup (Domain Entities)

| # | Issue | State | Relationship |
|---|-------|-------|--------------|
| 21 | Implement User Domain Entity | Closed | Prerequisite (delivered `User`, `UserRole`, `UserConfiguration`) |
| 22 | Implement Project Domain and N:M Relationship | Closed | Prerequisite (delivered `Project`, `ProjectMember`, `ProjectRole`) |
| 23 | Implement Kanban Board Domain (Columns & Tasks) | Closed | Prerequisite (delivered `BoardColumn`, `KanbanTask`) |
| 24 | **Implement Attachments (Assets) and Comments Domains** | **Open (this issue)** | — |
| 25 | Configure AppDbContext with Fluent API | Open | Depends on #21–#24 |
| 26 | Generate Initial EF Core Migration and Apply to Database | Open | Depends on #25 |

Issue #24 depends on the `KanbanTask` entity from #23 and the `User` entity from #21. Issue #25 (AppDbContext configuration) depends on this issue being complete so that all entity configurations are in place.

## GitHub Issue Comment Reference

The issue owner provided a SQL schema sketch in the comments that includes two additional `Asset` fields not in the original issue body:

- `ThumbnailKey` — nullable storage key for a generated thumbnail of the uploaded file.
- `ProcessingStatus` — tracks the lifecycle state of an uploaded file (e.g., pending, processing, completed, failed).

These fields are included in the acceptance criteria below.

## Acceptance Criteria

### Asset Entity

- [ ] An `Asset` entity class exists that inherits from `BaseEntity`.
- [ ] The `Asset` entity includes a `FileName` property representing the original name of the uploaded file.
- [ ] The `Asset` entity includes a `StorageKey` property representing the unique key or path used to locate the file in external storage.
- [ ] The `Asset` entity includes a `ThumbnailKey` property representing the optional key or path for a generated thumbnail of the file.
- [ ] The `Asset` entity includes a `MimeType` property representing the file's content type (e.g., `image/png`, `application/pdf`).
- [ ] The `Asset` entity includes a `FileSize` property representing the size of the file in bytes.
- [ ] The `Asset` entity includes a `ProcessingStatus` property representing the current processing state of the uploaded file.
- [ ] The `Asset` entity includes a required foreign key property linking it to a `KanbanTask`.
- [ ] Navigation from an `Asset` to its parent `KanbanTask` is supported.

### TaskComment Entity

- [ ] A `TaskComment` entity class exists that inherits from `BaseEntity`.
- [ ] The `TaskComment` entity includes a `Content` property representing the text body of the comment.
- [ ] The `TaskComment` entity includes a required foreign key property linking it to a `KanbanTask`.
- [ ] The `TaskComment` entity includes a required foreign key property linking it to a `User` who authored the comment.
- [ ] Navigation from a `TaskComment` to its parent `KanbanTask` is supported.
- [ ] Navigation from a `TaskComment` to the authoring `User` is supported.

### Navigation on Existing Entities

- [ ] Navigation from a `KanbanTask` to its collection of `Asset`s is supported.
- [ ] Navigation from a `KanbanTask` to its collection of `TaskComment`s is supported.
- [ ] Navigation from a `User` to the collection of `TaskComment`s they have authored is supported.

### Registration & Conventions

- [ ] Both new entity classes are placed in the `Models/Entities` folder, consistent with existing entity locations.
- [ ] Both new entities are registered in `ApplicationDbContext` via `DbSet<>` properties.
- [ ] The solution compiles and all existing tests continue to pass.

### Edge Cases & Constraints

- [ ] An `Asset` cannot exist without a parent `KanbanTask` (the task reference is required).
- [ ] A `TaskComment` cannot exist without a parent `KanbanTask` (the task reference is required).
- [ ] A `TaskComment` cannot exist without an authoring `User` (the author reference is required).
- [ ] If a `KanbanTask` is removed, its associated assets are also removed (cascading behaviour).
- [ ] If a `KanbanTask` is removed, its associated comments are also removed (cascading behaviour).
- [ ] If a `User` is removed, authored comments are not silently deleted; the deletion is restricted to prevent orphaned content.
- [ ] The `Content` property of `TaskComment` cannot be empty or whitespace-only (a comment must have a body).
- [ ] The `StorageKey` property of `Asset` must be unique across all assets (no two assets share the same storage location).
- [ ] The `FileSize` property of `Asset` must be a non-negative value.
- [ ] The `FileName` property of `Asset` cannot be empty (every asset must have an original file name).
- [ ] The `ProcessingStatus` property of `Asset` is required (every asset must have a known processing state).
- [ ] The `ThumbnailKey` property of `Asset` is optional (not all file types produce thumbnails).

### AC Validation

| # | Criterion Summary | Testable | Specific | Independent | Impl-Free | Complete |
|---|-------------------|----------|----------|-------------|-----------|----------|
| 1 | Asset inherits BaseEntity | ✅ | ✅ | ✅ | ✅ | ✅ |
| 2 | Asset has FileName property | ✅ | ✅ | ✅ | ✅ | ✅ |
| 3 | Asset has StorageKey property | ✅ | ✅ | ✅ | ✅ | ✅ |
| 4 | Asset has ThumbnailKey property | ✅ | ✅ | ✅ | ✅ | ✅ |
| 5 | Asset has MimeType property | ✅ | ✅ | ✅ | ✅ | ✅ |
| 6 | Asset has FileSize property | ✅ | ✅ | ✅ | ✅ | ✅ |
| 7 | Asset has ProcessingStatus property | ✅ | ✅ | ✅ | ✅ | ✅ |
| 8 | Asset required FK to KanbanTask | ✅ | ✅ | ✅ | ✅ | ✅ |
| 9 | Asset navigates to KanbanTask | ✅ | ✅ | ✅ | ✅ | ✅ |
| 10 | TaskComment inherits BaseEntity | ✅ | ✅ | ✅ | ✅ | ✅ |
| 11 | TaskComment has Content property | ✅ | ✅ | ✅ | ✅ | ✅ |
| 12 | TaskComment required FK to KanbanTask | ✅ | ✅ | ✅ | ✅ | ✅ |
| 13 | TaskComment required FK to User | ✅ | ✅ | ✅ | ✅ | ✅ |
| 14 | TaskComment navigates to KanbanTask | ✅ | ✅ | ✅ | ✅ | ✅ |
| 15 | TaskComment navigates to User | ✅ | ✅ | ✅ | ✅ | ✅ |
| 16 | KanbanTask navigates to Assets | ✅ | ✅ | ✅ | ✅ | ✅ |
| 17 | KanbanTask navigates to Comments | ✅ | ✅ | ✅ | ✅ | ✅ |
| 18 | User navigates to authored comments | ✅ | ✅ | ✅ | ✅ | ✅ |
| 19 | Entities in Models/Entities folder | ✅ | ✅ | ✅ | ✅ | ✅ |
| 20 | Entities registered in DbContext | ✅ | ✅ | ✅ | ✅ | ✅ |
| 21 | Solution compiles, tests pass | ✅ | ✅ | ✅ | ✅ | ✅ |
| 22 | Asset requires KanbanTask | ✅ | ✅ | ✅ | ✅ | ✅ |
| 23 | TaskComment requires KanbanTask | ✅ | ✅ | ✅ | ✅ | ✅ |
| 24 | TaskComment requires authoring User | ✅ | ✅ | ✅ | ✅ | ✅ |
| 25 | Cascade delete: KanbanTask → Assets | ✅ | ✅ | ✅ | ✅ | ✅ |
| 26 | Cascade delete: KanbanTask → Comments | ✅ | ✅ | ✅ | ✅ | ✅ |
| 27 | User deletion restricted (not cascade to comments) | ✅ | ✅ | ✅ | ✅ | ✅ |
| 28 | TaskComment Content cannot be empty | ✅ | ✅ | ✅ | ✅ | ✅ |
| 29 | Asset StorageKey is unique | ✅ | ✅ | ✅ | ✅ | ✅ |
| 30 | Asset FileSize is non-negative | ✅ | ✅ | ✅ | ✅ | ✅ |
| 31 | Asset FileName cannot be empty | ✅ | ✅ | ✅ | ✅ | ✅ |
| 32 | Asset ProcessingStatus is required | ✅ | ✅ | ✅ | ✅ | ✅ |
| 33 | Asset ThumbnailKey is optional | ✅ | ✅ | ✅ | ✅ | ✅ |
