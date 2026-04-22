# Issue #43: Implement Project CRUD Operations

**GitHub Issue:** [#43 - Implement Project CRUD Operations](https://github.com/Gulybi/KanbAI-Core/issues/43)  
**Milestone:** Project and Kanban Business Logic (Issue #43 of 5)  
**Assignee:** @Gulybi  
**Created:** 2026-04-22

---

## 📊 Business Value & Context

### What Are We Building?
A complete Project management API that allows authenticated users to:
- **Create new projects** with name and optional description
- **Retrieve their own projects** (projects where they are members or owners)
- **Update project details** (name, description)
- **Delete projects** (soft delete or hard delete)

### Who Is This For?
**End Users** (authenticated application users with valid JWT tokens) who need to organize their work into discrete projects. Each project serves as a container for kanban boards, tasks, and team members.

### Why Is This Valuable?
This is the **foundation** of the entire kanban workflow. Without project management, users cannot:
- Create workspaces for their teams
- Organize boards and tasks hierarchically
- Control access and membership to their work

This issue is the **first step** in the "Project and Kanban Business Logic" milestone. Subsequent issues (#44-#47) depend on projects existing and being manageable.

---

## 🗺️ Milestone Context

**Milestone:** Project and Kanban Business Logic  
**Total Issues:** 5

| Issue | Title | Status | Dependency |
|-------|-------|--------|------------|
| **#43** | **Implement Project CRUD Operations** | **OPEN** | **← YOU ARE HERE** |
| #44 | Implement Project Member Management | OPEN | Requires #43 (projects must exist) |
| #45 | Implement Board Columns API | OPEN | Requires #43 (columns belong to projects) |
| #46 | Implement Kanban Task Creation and Management | OPEN | Requires #43, #45 (tasks belong to columns) |
| #47 | Implement Task Movement Logic (Drag-and-Drop Backend Support) | OPEN | Requires #46 (tasks must exist to move) |

**Critical Path:** This is a **blocking issue**. All subsequent features depend on projects being fully manageable.

---

## 🔍 Current State vs. Desired State

### Current State (As of 2026-04-22)

#### ✅ What Already Exists
1. **Project Entity** ([Models/Entities/Project.cs](../KanbAI-Core/KanbAI-Core/Models/Entities/Project.cs))
   - Inherits from `BaseEntity` (provides `Guid Id`, `DateTimeOffset CreatedAt/UpdatedAt`)
   - Properties:
     - `string Name` (max 200 chars, required)
     - `string? Description` (max 500 chars, optional)
   - Navigation properties:
     - `ICollection<ProjectMember> Members` (configured with cascade delete)
     - `ICollection<BoardColumn> Columns`
   - **Entity Configuration:** [Data/Configurations/ProjectConfiguration.cs](../KanbAI-Core/KanbAI-Core/Data/Configurations/ProjectConfiguration.cs)

2. **ProjectMember Entity** ([Models/Entities/ProjectMember.cs](../KanbAI-Core/KanbAI-Core/Models/Entities/ProjectMember.cs))
   - Relations:
     - `Guid ProjectId` → `Project` (FK, cascade delete)
     - `Guid UserId` → `User` (FK, restrict delete)
   - `ProjectRole Role` enum (Member/Owner) with default `Member`
   - **Composite unique index** on `(ProjectId, UserId)` prevents duplicate memberships

3. **Authentication System**
   - JWT authentication fully implemented ([Controllers/AuthController.cs](../KanbAI-Core/KanbAI-Core/Controllers/AuthController.cs))
   - Token service generates JWT with claims:
     - `ClaimTypes.NameIdentifier` → `user.Id` (Guid as string)
     - `ClaimTypes.Email` → `user.Email`
     - `ClaimTypes.Name` → `user.Name`
     - `ClaimTypes.Role` → `user.Role.ToString()` (UserRole enum: Member/Admin)
     - `JwtRegisteredClaimNames.Jti` → unique token identifier
   - Claims extraction pattern demonstrated in existing controllers

4. **Service and Controller Patterns**
   - **Controllers:** Constructor dependency injection, returns `IActionResult` (BadRequest, Unauthorized, CreatedAtAction, Ok)
   - **Services:** Interface-based services (e.g., `ITokenService`, `IPasswordHasher`)
   - **DTOs:** Record-based with validation attributes (`[Required]`, `[EmailAddress]`, `[MinLength]`)
   - **Response Format:** `ApiResponse<T>` (generic) with `Success`, `Message`, `Errors`, and static factory methods (`Ok`, `Fail`)
   - **Error Handling:** Global exception handler middleware returns `ApiResponse.Fail()` for unhandled exceptions

5. **Database Context**
   - `ApplicationDbContext` with `DbSet<Project>`, `DbSet<User>`, `DbSet<ProjectMember>`
   - Automatically updates `CreatedAt`/`UpdatedAt` in `SaveChangesAsync` override
   - Migrations exist for Project and ProjectMember tables (migration `20260411*`)

#### ❌ What's Missing
1. **No ProjectService** (service layer to encapsulate business logic)
2. **No ProjectController** (API endpoints to expose CRUD operations)
3. **No Project DTOs** (CreateProjectDto, UpdateProjectDto, ProjectResponseDto)
4. **No authorization logic** to:
   - Ensure only the authenticated user can create projects (and is automatically assigned as Owner)
   - Verify user has permission to update/delete projects (must be Owner or Member)
   - Filter "Get All Projects" to return only projects where the current user is a member

### Desired State (After Issue #43)

#### 🎯 Users Should Be Able To:
1. **Create a new project** via `POST /api/projects`
   - Provide: `Name` (required), `Description` (optional)
   - System automatically:
     - Assigns the authenticated user (from JWT claims) as the project Owner
     - Creates a `ProjectMember` record linking `UserId` → `ProjectId` with `Role = Owner`
   - Returns: `201 Created` with the new project details (including generated `Id`)

2. **Retrieve their projects** via `GET /api/projects`
   - Returns only projects where the authenticated user is a member (via `ProjectMember` table)
   - Includes: `Id`, `Name`, `Description`, `CreatedAt`, `UpdatedAt`, and user's `Role` in that project

3. **Retrieve a single project** via `GET /api/projects/{id}`
   - Returns: Project details if the user is a member
   - Returns: `404 Not Found` if the project doesn't exist or the user is not a member

4. **Update a project** via `PUT /api/projects/{id}`
   - User must be a member of the project (Owner or Member role)
   - Allows updating: `Name`, `Description`
   - Returns: `200 OK` with updated project details
   - Returns: `403 Forbidden` if the user is not a member
   - Returns: `404 Not Found` if the project doesn't exist

5. **Delete a project** via `DELETE /api/projects/{id}`
   - User must be the **Owner** of the project (not just a Member)
   - Deletes the project and cascades to `ProjectMember` records (via EF configuration)
   - Returns: `204 No Content` on success
   - Returns: `403 Forbidden` if the user is not the Owner
   - Returns: `404 Not Found` if the project doesn't exist

#### 🔐 Security Requirements
- **All endpoints** must require authentication (`[Authorize]` attribute)
- **User identity** must be extracted from JWT claims (`ClaimTypes.NameIdentifier`)
- **Authorization checks** must verify:
  - Create: Any authenticated user can create (becomes Owner automatically)
  - Read: User must be a member (Owner or Member)
  - Update: User must be a member (Owner or Member)
  - Delete: User must be the Owner

---

## ✅ Acceptance Criteria

These criteria define "Done" from a **business perspective** (not implementation details).

### 1. Project Creation
- [ ] **AC1.1:** An authenticated user can send `POST /api/projects` with `{ "Name": "My Project", "Description": "Optional description" }` and receive a `201 Created` response with the project's `Id`, `Name`, `Description`, `CreatedAt`, `UpdatedAt`, and their role (`Owner`).
- [ ] **AC1.2:** The system automatically creates a `ProjectMember` record linking the authenticated user to the new project with `Role = Owner`.
- [ ] **AC1.3:** If `Name` is missing or empty, the request returns `400 Bad Request` with a validation error message.
- [ ] **AC1.4:** If `Name` exceeds 200 characters, the request returns `400 Bad Request` with a validation error message.
- [ ] **AC1.5:** If `Description` exceeds 500 characters, the request returns `400 Bad Request` with a validation error message.
- [ ] **AC1.6:** Unauthenticated requests return `401 Unauthorized`.

### 2. Retrieve All Projects for Current User
- [ ] **AC2.1:** `GET /api/projects` returns a list of all projects where the authenticated user is a member (Owner or Member role), including the project's `Id`, `Name`, `Description`, `CreatedAt`, `UpdatedAt`, and the user's `Role` in that project.
- [ ] **AC2.2:** If the user is not a member of any projects, the response is `200 OK` with an empty array `[]`.
- [ ] **AC2.3:** Projects where the user is **not** a member are **not included** in the response.
- [ ] **AC2.4:** Unauthenticated requests return `401 Unauthorized`.

### 3. Retrieve a Single Project by ID
- [ ] **AC3.1:** `GET /api/projects/{id}` returns `200 OK` with the project's `Id`, `Name`, `Description`, `CreatedAt`, `UpdatedAt`, and the user's `Role` in that project, if the user is a member.
- [ ] **AC3.2:** If the project does not exist, the response is `404 Not Found`.
- [ ] **AC3.3:** If the project exists but the user is **not a member**, the response is `404 Not Found` (to prevent information disclosure).
- [ ] **AC3.4:** Unauthenticated requests return `401 Unauthorized`.

### 4. Update a Project
- [ ] **AC4.1:** A user who is a member (Owner or Member) of a project can send `PUT /api/projects/{id}` with `{ "Name": "Updated Name", "Description": "Updated description" }` and receive `200 OK` with the updated project details.
- [ ] **AC4.2:** The `UpdatedAt` timestamp is automatically updated to the current time.
- [ ] **AC4.3:** If the user is **not a member** of the project, the response is `403 Forbidden`.
- [ ] **AC4.4:** If the project does not exist, the response is `404 Not Found`.
- [ ] **AC4.5:** If `Name` is missing, empty, or exceeds 200 characters, the request returns `400 Bad Request` with a validation error.
- [ ] **AC4.6:** If `Description` exceeds 500 characters, the request returns `400 Bad Request` with a validation error.
- [ ] **AC4.7:** Unauthenticated requests return `401 Unauthorized`.

### 5. Delete a Project
- [ ] **AC5.1:** A user who is the **Owner** of a project can send `DELETE /api/projects/{id}` and receive `204 No Content`. The project and all related `ProjectMember` records are deleted (cascade).
- [ ] **AC5.2:** If the user is a **Member** (not Owner), the response is `403 Forbidden`.
- [ ] **AC5.3:** If the project does not exist, the response is `404 Not Found`.
- [ ] **AC5.4:** If the user is **not a member** of the project, the response is `404 Not Found` (to prevent information disclosure).
- [ ] **AC5.5:** Unauthenticated requests return `401 Unauthorized`.

### 6. Edge Cases & Error Handling
- [ ] **AC6.1:** Duplicate project names are allowed (no uniqueness constraint on `Name`).
- [ ] **AC6.2:** All validation errors return `400 Bad Request` with a structured `ApiResponse.Fail()` containing error details.
- [ ] **AC6.3:** Database errors (e.g., connection failure) are caught by the global exception handler and return `500 Internal Server Error` with a generic error message (no sensitive details).

---

## 🧪 Quality Gate Checklist

Before marking this issue as complete, verify:

| Check | Requirement |
|-------|-------------|
| **Testable** | Can a QA engineer write automated assertions for each acceptance criterion? |
| **Specific** | Does each criterion describe concrete, observable behavior (e.g., status codes, response fields)? |
| **Independent** | Is each criterion verifiable in isolation? |
| **Implementation-Free** | Do the criteria describe *what* the system does, not *how* it does it? |
| **Complete** | Are edge cases covered (missing fields, unauthorized access, non-existent projects)? |

---

## 🚀 Next Steps

Once this context note is approved:
1. **Staff Engineer** will read this document and create a technical specification in `docs/handoffs/issue_43_tech_spec.md` defining:
   - Service interface (`IProjectService`) and implementation
   - Controller endpoints (route definitions, HTTP methods, authorization)
   - DTO schemas (validation rules, property mappings)
   - Database queries (EF Core patterns for filtering by membership)
2. **Developer** will implement the code following the tech spec.
3. **QA Tester** will write automated tests validating each acceptance criterion.

---

**Document Status:** ✅ Ready for Staff Engineer Review  
**Last Updated:** 2026-04-22
