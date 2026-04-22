# Context Handoff: Issue #45 - Implement Board Columns API

## Title & Business Value

**Title:** Implement Board Columns API
**Who:** Users who need to manage Kanban board columns within their projects, and frontend developers who will integrate with the column management endpoints.
**Why:** Board columns are the fundamental organizational structure for Kanban boards. Users need to create, view, and delete columns (e.g., "To Do", "In Progress", "Done") within their projects. Without a Board Columns API, users cannot configure their Kanban boards to match their workflow, and the application cannot support task management (which depends on columns existing). This API is a prerequisite for implementing task creation and drag-and-drop functionality.

## Current State vs. Desired State

### Current State

- A `BoardColumn` entity exists in `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Models/Entities/BoardColumn.cs` with properties:
  - `Name` (string, required)
  - `ColorCode` (string, optional)
  - `ColumnOrder` (int, required)
  - `ProjectId` (Guid, foreign key)
  - Navigation property to `Project`
  - Navigation property to `Tasks` collection
- A `BoardColumnConfiguration` exists in `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Data/Configurations/BoardColumnConfiguration.cs` defining:
  - `Name` max length of 100 characters
  - `ColorCode` max length of 20 characters
  - Cascade delete behavior (when a project is deleted, its columns are deleted)
- The `Project` entity in `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Models/Entities/Project.cs` has a `Columns` navigation property (ICollection of BoardColumn)
- An existing `ProjectController` exists in `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Controllers/ProjectController.cs` demonstrating the established patterns:
  - Authorization via `[Authorize]` attribute
  - User ID extraction from JWT claims
  - Service layer delegation pattern
  - Standardized `ApiResponse<T>` wrapper for responses
- A `ProjectService` exists in `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Services/Projects/` demonstrating service layer patterns with interface segregation
- There is **no** `ColumnService` or interface
- There is **no** `ColumnController`
- There are **no** DTOs for column operations
- There are **no** API endpoints to manage columns

### Desired State

- A `ColumnController` exists that exposes RESTful endpoints for column management
- A `ColumnService` and interface exist that implement the business logic for column operations
- DTOs exist for column operations (creation, response)
- Users can fetch all columns for a specific project, with columns returned ordered by their `ColumnOrder` property (ascending)
- Users can create new columns within a project they are a member of
- Users can delete columns from a project they are a member of
- All column operations are protected by authorization and verify the user is a member of the target project
- The API follows the same patterns established by `ProjectController` (authorization, service delegation, response wrapping, error handling)

## Milestone Context

**Milestone:** Project and Kanban Business Logic

| # | Issue | State | Relationship |
|---|-------|-------|--------------|
| 44 | Implement Project Member Management | Open | Prerequisite (must verify user is project member) |
| 45 | **Implement Board Columns API** | **Open (this issue)** | — |
| 46 | Implement Kanban Task Creation and Management | Open | Depends on #45 (tasks belong to columns) |
| 47 | Implement Task Movement Logic (Drag-and-Drop Backend Support) | Open | Depends on #46 (requires tasks to exist) |

Issue #45 is a **critical prerequisite** for issues #46 and #47. Tasks cannot be created without columns to place them in, and task movement logic requires multiple columns to exist.

## Acceptance Criteria

- [ ] A `ColumnController` class exists in the `Controllers` folder, following the naming and authorization patterns of `ProjectController`.
- [ ] A `ColumnService` interface exists in the `Services` folder, defining the contract for column operations.
- [ ] A `ColumnService` implementation exists that implements the interface, following the dependency injection pattern used by `ProjectService`.
- [ ] The service and interface are registered in the dependency injection container.
- [ ] DTOs exist for column operations and are placed in the `DTOs` folder, consistent with existing DTO locations.
- [ ] An endpoint exists to fetch all columns for a specific project.
- [ ] When fetching columns for a project, the columns are returned ordered by their `ColumnOrder` property in ascending order.
- [ ] When fetching columns, only users who are members of the project can retrieve the columns.
- [ ] If a user attempts to fetch columns for a project they are not a member of, the API returns an appropriate authorization error.
- [ ] An endpoint exists to create a new column within a specific project.
- [ ] When creating a column, the user must be a member of the target project.
- [ ] When creating a column, the `Name` field is required and cannot exceed 100 characters.
- [ ] When creating a column, if `ColumnOrder` is not provided, it is automatically set to maintain a valid ordering (e.g., max existing order + 1, or 0 if no columns exist).
- [ ] An endpoint exists to delete a column from a project.
- [ ] When deleting a column, the user must be a member of the target project.
- [ ] When a column is deleted, the deletion succeeds even if tasks are associated with it (cascade delete is already configured at the entity level).
- [ ] If a user attempts to create or delete a column for a project they are not a member of, the API returns an appropriate authorization error.
- [ ] All endpoints return responses wrapped in the standard `ApiResponse<T>` format, consistent with `ProjectController`.
- [ ] All endpoints are protected with the `[Authorize]` attribute.
- [ ] The controller extracts the user ID from JWT claims using the same pattern as `ProjectController`.
- [ ] If a project does not exist when fetching, creating, or deleting columns, the API returns a 404 Not Found response with a clear error message.
- [ ] The solution compiles and all existing tests continue to pass.

### AC Validation

| # | Criterion Summary | Testable | Specific | Independent | Impl-Free | Complete |
|---|-------------------|----------|----------|-------------|-----------|----------|
| 1 | ColumnController exists in Controllers folder | ✅ | ✅ | ✅ | ✅ | ✅ |
| 2 | ColumnService interface defined | ✅ | ✅ | ✅ | ✅ | ✅ |
| 3 | ColumnService implementation exists | ✅ | ✅ | ✅ | ✅ | ✅ |
| 4 | Service registered in DI container | ✅ | ✅ | ✅ | ✅ | ✅ |
| 5 | Column DTOs exist in DTOs folder | ✅ | ✅ | ✅ | ✅ | ✅ |
| 6 | Endpoint to fetch all columns exists | ✅ | ✅ | ✅ | ✅ | ✅ |
| 7 | Columns returned ordered by ColumnOrder asc | ✅ | ✅ | ✅ | ✅ | ✅ |
| 8 | Fetch columns requires project membership | ✅ | ✅ | ✅ | ✅ | ✅ |
| 9 | Non-member fetch returns authorization error | ✅ | ✅ | ✅ | ✅ | ✅ |
| 10 | Endpoint to create column exists | ✅ | ✅ | ✅ | ✅ | ✅ |
| 11 | Create column requires project membership | ✅ | ✅ | ✅ | ✅ | ✅ |
| 12 | Column Name required, max 100 chars | ✅ | ✅ | ✅ | ✅ | ✅ |
| 13 | ColumnOrder auto-set if not provided | ✅ | ✅ | ✅ | ✅ | ✅ |
| 14 | Endpoint to delete column exists | ✅ | ✅ | ✅ | ✅ | ✅ |
| 15 | Delete column requires project membership | ✅ | ✅ | ✅ | ✅ | ✅ |
| 16 | Delete succeeds with associated tasks | ✅ | ✅ | ✅ | ✅ | ✅ |
| 17 | Non-member create/delete returns auth error | ✅ | ✅ | ✅ | ✅ | ✅ |
| 18 | Responses use ApiResponse<T> wrapper | ✅ | ✅ | ✅ | ✅ | ✅ |
| 19 | All endpoints use [Authorize] attribute | ✅ | ✅ | ✅ | ✅ | ✅ |
| 20 | User ID extracted from JWT claims | ✅ | ✅ | ✅ | ✅ | ✅ |
| 21 | Non-existent project returns 404 | ✅ | ✅ | ✅ | ✅ | ✅ |
| 22 | Solution compiles, tests pass | ✅ | ✅ | ✅ | ✅ | ✅ |
