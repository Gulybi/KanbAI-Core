# Issue #44: Implement Project Member Management

**GitHub Issue:** [#44 - Implement Project Member Management](https://github.com/Gulybi/KanbAI-Core/issues/44)  
**Milestone:** Project and Kanban Business Logic (Issue #44 of 5)  
**Assignee:** @Gulybi  
**Created:** 2026-04-27

---

## 📊 Business Value & Context

### What Are We Building?
A Project Member Management API that enables project owners to control team membership by:
- **Adding users to a project** - Invite users to collaborate on a project by assigning them as members
- **Removing users from a project** - Revoke access when team members leave or are no longer needed
- **Authorization enforcement** - Ensure only project owners can modify project membership

### Who Is This For?
**Project Owners** (users with ProjectRole.Owner) who need to build and manage their project teams. This enables them to:
- Onboard new team members to collaborate on shared work
- Offboard members who no longer need access
- Maintain control over who can view and contribute to their projects

**Project Members** (users with ProjectRole.Member) indirectly benefit because they gain or lose access to projects based on these operations.

### Why Is This Valuable?
**Collaboration is the core value proposition** of KanbAI. A project without the ability to add team members is merely a personal workspace. This feature transforms projects into true collaborative environments where:
- Teams can work together on shared kanban boards
- Owners can delegate tasks to specific members
- Access control ensures sensitive projects remain private to their intended audience

Without member management, every project is isolated to its creator, severely limiting the platform's utility for team-based workflows.

---

## 🗺️ Milestone Context

**Milestone:** Project and Kanban Business Logic  
**Total Issues:** 5

| Issue | Title | Status | Dependency |
|-------|-------|--------|------------|
| #43 | Implement Project CRUD Operations | COMPLETED | Foundation |
| **#44** | **Implement Project Member Management** | **OPEN** | **← YOU ARE HERE** |
| #45 | Implement Board Columns API | OPEN | Independent of #44 |
| #46 | Implement Kanban Task Creation and Management | TBD | Requires #43, #45 |
| #47 | Implement Task Movement Logic | TBD | Requires #46 |

**Prerequisites (Completed):**
- Issue #43: ProjectController exists with CRUD operations (/api/Project endpoints)
- Issue #22: ProjectMember entity and ProjectRole enum exist in the domain model
- Issue #26: EF Core migrations applied with ProjectMembers table

**Relationship to Other Issues:**
- **Independent of #45**: Board Columns API does not depend on member management
- **Foundation for future authorization**: While not blocking #45-#47, member management establishes the pattern for project-scoped authorization that will be reused in subsequent features

---

## 🔍 Current State vs. Desired State

### Current State

**Domain Model (Fully Implemented):**
- `ProjectMember` entity exists in `KanbAI-Core/Models/Entities/ProjectMember.cs`:
  - Represents many-to-many junction between User and Project
  - Properties: `ProjectId`, `Project`, `UserId`, `User`, `Role` (ProjectRole enum)
  - Inherits from `BaseEntity` (provides Id, CreatedAt, UpdatedAt)
- `ProjectRole` enum exists in `KanbAI-Core/Models/Entities/ProjectRole.cs`:
  - `Member = 0` (standard team member, read/write access)
  - `Owner = 1` (project creator, full control including member management and deletion)
- `Project` entity in `KanbAI-Core/Models/Entities/Project.cs`:
  - Navigation property: `ICollection<ProjectMember> Members`
- `User` entity in `KanbAI-Core/Models/Entities/User.cs`:
  - Navigation property: `ICollection<ProjectMember> ProjectMemberships`

**API Layer (CRUD Only):**
- `ProjectController` in `KanbAI-Core/Controllers/ProjectController.cs`:
  - Endpoints: POST /api/Project, GET /api/Project, GET /api/Project/{id}, PUT /api/Project/{id}, DELETE /api/Project/{id}
  - Authorization: All endpoints require [Authorize] attribute (JWT-based authentication)
  - Helper: `GetCurrentUserId()` extracts authenticated user ID from JWT claims
  - **No member management endpoints exist**

**Service Layer:**
- `IProjectService` interface in `KanbAI-Core/Services/Projects/IProjectService.cs`:
  - Methods: `CreateProjectAsync`, `GetUserProjectsAsync`, `GetProjectByIdAsync`, `UpdateProjectAsync`, `DeleteProjectAsync`
  - `DeleteProjectAsync` enforces owner-only deletion (returns error message if user is not Owner)
  - **No methods for adding/removing members**

**Data Transfer Objects (DTOs):**
- `ProjectResponseDto`, `CreateProjectDto`, `UpdateProjectDto` exist in `KanbAI-Core/DTOs/`
- **No DTOs exist for member management operations**

**Authorization Pattern:**
- Project deletion already implements owner-only enforcement in `ProjectService.DeleteProjectAsync`:
  - Checks if user is a member of the project
  - Returns specific error message if user is not the owner: "Only the project owner can delete the project."
  - Controller returns 403 Forbidden for this specific error

### Desired State

**API Layer:**
- New endpoints in `ProjectController`:
  - **POST /api/Project/{projectId}/members** - Add a user to the project with Member role
  - **DELETE /api/Project/{projectId}/members/{userId}** - Remove a user from the project
- Both endpoints return 403 Forbidden if the authenticated user is not the project owner
- Both endpoints return 404 Not Found if the project does not exist or the authenticated user is not a member

**Service Layer:**
- New methods in `IProjectService`:
  - Add a user as a project member (validates user existence, prevents duplicate memberships)
  - Remove a user from project membership (prevents removing the owner, ensures at least one owner remains)

**Authorization Rules (Business Logic):**
1. **Only project owners can add members** - Users with ProjectRole.Member cannot invite others
2. **Only project owners can remove members** - Users with ProjectRole.Member cannot revoke access
3. **Cannot remove the last owner** - At least one Owner must remain to prevent orphaned projects
4. **Cannot add duplicate members** - A user cannot be added to a project they are already a member of
5. **Added users default to Member role** - Only the creator or existing owners can assign the Owner role (future enhancement)

**Error Handling:**
- 400 Bad Request: User to be added does not exist, user is already a member, attempting to remove last owner
- 403 Forbidden: Authenticated user is not the project owner
- 404 Not Found: Project does not exist or authenticated user is not a member

---

## ✅ Acceptance Criteria

**As a project owner, I can add a user to my project so that they can collaborate on the project's boards and tasks.**

- [ ] A POST /api/Project/{projectId}/members endpoint exists
- [ ] The endpoint requires a valid JWT token (authenticated user)
- [ ] The endpoint accepts a request body containing the user ID to be added
- [ ] The endpoint returns 403 Forbidden if the authenticated user is not a project owner
- [ ] The endpoint returns 404 Not Found if the project does not exist
- [ ] The endpoint returns 404 Not Found if the authenticated user is not a member of the project
- [ ] The endpoint returns 400 Bad Request if the user to be added does not exist in the system
- [ ] The endpoint returns 400 Bad Request with a clear message if the user is already a member of the project
- [ ] Upon successful addition, the user is assigned ProjectRole.Member (not Owner)
- [ ] Upon successful addition, a ProjectMember record is persisted to the database with CreatedAt timestamp
- [ ] The endpoint returns 201 Created with the newly added member's details (user ID, name, email, role, joinedAt timestamp)

**As a project owner, I can remove a user from my project so that they no longer have access to the project's data.**

- [ ] A DELETE /api/Project/{projectId}/members/{userId} endpoint exists
- [ ] The endpoint requires a valid JWT token (authenticated user)
- [ ] The endpoint returns 403 Forbidden if the authenticated user is not a project owner
- [ ] The endpoint returns 404 Not Found if the project does not exist
- [ ] The endpoint returns 404 Not Found if the authenticated user is not a member of the project
- [ ] The endpoint returns 404 Not Found if the user to be removed is not a member of the project
- [ ] The endpoint returns 400 Bad Request if attempting to remove the last remaining owner from the project
- [ ] Upon successful removal, the ProjectMember record is deleted from the database
- [ ] The endpoint returns 204 No Content upon successful removal

**As a project member (non-owner), I cannot add or remove members to ensure only owners control project membership.**

- [ ] When a user with ProjectRole.Member calls POST /api/Project/{projectId}/members, the endpoint returns 403 Forbidden
- [ ] When a user with ProjectRole.Member calls DELETE /api/Project/{projectId}/members/{userId}, the endpoint returns 403 Forbidden

**Authorization enforcement is consistent with existing project operations.**

- [ ] The authorization logic follows the same pattern as DELETE /api/Project/{id} (owner-only check, specific error messages)
- [ ] The same `GetCurrentUserId()` helper is reused to extract the authenticated user from JWT claims
- [ ] Error responses follow the existing `ApiResponse.Fail(errorMessage)` pattern

**Edge cases are handled gracefully.**

- [ ] Attempting to add a user who is already a member returns a clear, specific error message (e.g., "User is already a member of this project.")
- [ ] Attempting to remove a user who is not a member returns 404 Not Found (not 400 Bad Request)
- [ ] Attempting to remove the only owner returns a clear error message (e.g., "Cannot remove the last owner from the project.")
- [ ] If the authenticated user provides an invalid user ID (non-existent GUID), the system returns 400 Bad Request with a clear message (e.g., "User not found.")

---

## 🎯 Out of Scope (Future Enhancements)

These capabilities are explicitly **not** part of this issue and should be deferred to future work:

- **Changing a member's role** (e.g., promoting Member to Owner or demoting Owner to Member) - No PUT /api/Project/{projectId}/members/{userId} endpoint
- **Listing all members of a project** - No GET /api/Project/{projectId}/members endpoint
- **Transferring project ownership** - Owners cannot designate a new primary owner
- **Invitation system** - No email invitations, pending invites, or acceptance workflow (users are added directly)
- **Audit log** - No record of who added/removed whom and when (beyond CreatedAt/UpdatedAt timestamps)
- **Bulk operations** - No ability to add/remove multiple users in a single request
- **Member-specific permissions** - All Members have the same level of access (read/write on all project data)

---

## 📚 Reference Materials

**Related Issues:**
- [#43 - Implement Project CRUD Operations](https://github.com/Gulybi/KanbAI-Core/issues/43) - Foundation for ProjectController and IProjectService
- [#22 - Implement Project Domain and N:M Relationship](https://github.com/Gulybi/KanbAI-Core/issues/22) - Introduced ProjectMember entity and ProjectRole enum

**Relevant Files:**
- `KanbAI-Core/Models/Entities/ProjectMember.cs` - Domain entity for the junction table
- `KanbAI-Core/Models/Enums/ProjectRole.cs` - Enum defining Member (0) and Owner (1) roles
- `KanbAI-Core/Models/Entities/Project.cs` - Project entity with Members navigation property
- `KanbAI-Core/Models/Entities/User.cs` - User entity with ProjectMemberships navigation property
- `KanbAI-Core/Controllers/ProjectController.cs` - Existing controller to be extended with member management endpoints
- `KanbAI-Core/Services/Projects/IProjectService.cs` - Service interface to be extended with member management methods
- `KanbAI-Core/Data/Configurations/ProjectMemberConfiguration.cs` - EF Core fluent configuration for ProjectMember entity

**Coding Standards:**
- `.claude/rules/code-standards.md` - Modern C# conventions (file-scoped namespaces, async/await, DI patterns)
- `.claude/rules/security-safety.md` - Authorization checks, input validation, no PII in logs
- `.claude/rules/testing-observability.md` - Unit test structure (AAA pattern), xUnit naming conventions

---

## 🚦 Success Metrics

This feature is considered complete when:
1. A project owner can successfully add a user to their project via the API
2. A project owner can successfully remove a non-owner user from their project via the API
3. A project member (non-owner) receives a 403 Forbidden when attempting either operation
4. All edge cases (duplicate member, remove last owner, non-existent user) return appropriate error codes and messages
5. The implementation passes code review with zero security vulnerabilities related to authorization bypass
6. Integration tests demonstrate the full workflow: create project, add member, authenticate as member, verify access

---

**Prepared by:** Product Manager Agent  
**Date:** 2026-04-27

---

The business context is defined and saved. You can now instruct the staff-engineer to read the context note and design the technical specification.
