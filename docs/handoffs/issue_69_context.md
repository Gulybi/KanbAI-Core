# Issue #69: Implement Project Members Fetching and Email-Based Member Assignment

**GitHub Issue:** [#69 - Implement Project Members Fetching and Email-Based Member Assignment](https://github.com/Gulybi/KanbAI-Core/issues/69)  
**Milestone:** None (Standalone Enhancement)  
**Assignee:** @Gulybi  
**Created:** 2026-04-29

---

## 📊 Business Value & Context

### What Are We Building?
Two critical enhancements to the existing Project Member Management API that unblock frontend integration:
- **Member List Retrieval** - A new endpoint to fetch all members of a specific project
- **Email-Based Member Assignment** - Extend the existing Add Member endpoint to accept email addresses instead of requiring Guid UserIds

### Who Is This For?
**Frontend Developers** integrating the project management UI who need to:
- Display the current list of team members for a project (with their names, emails, roles, and join dates)
- Add new members to a project by searching for them by email address (a user-friendly identifier)

**Project Owners** (end users) who benefit from:
- Seeing who is currently on their project team at a glance
- Inviting collaborators by email address rather than needing to know obscure system-generated GUIDs

### Why Is This Valuable?
**Critical API Gap Identified During Frontend Integration:**  
The current implementation (Issue #44) provides POST and DELETE endpoints for member management but lacks a GET endpoint to retrieve the current member list. This means the frontend cannot display who is already on a project team without fetching the entire project and manually extracting member data.

**Frontend Cannot Supply Guid UserIds:**  
The existing `AddMemberDto` requires a `Guid UserId` property. The frontend has no mechanism to discover a user's Guid without a dedicated user search endpoint. Requiring email-based lookup streamlines the workflow and aligns with how users naturally identify collaborators.

**Security Consideration:**  
Rather than exposing a broad user search endpoint that could leak information about all registered users, accepting an email address in the Add Member flow keeps the API surface minimal and project-scoped. Only project owners can attempt to add members, and the lookup happens server-side.

---

## 🔍 Current State vs. Desired State

### Current State

**Existing API Endpoints (Implemented in Issue #44):**
- `POST /api/Project/{projectId}/members` - Adds a user to the project
  - **Request Body:** `AddMemberDto` with `Guid UserId` (required)
  - **Authorization:** Only project owners can call this endpoint
  - **Response:** 201 Created with `MemberResponseDto` (UserId, Name, Email, Role, JoinedAt)
  - **Location:** `KanbAI-Core/Controllers/ProjectController.cs` (lines 95-119)
- `DELETE /api/Project/{projectId}/members/{userId}` - Removes a user from the project
  - **Authorization:** Only project owners can call this endpoint
  - **Location:** `KanbAI-Core/Controllers/ProjectController.cs` (lines 121-145)

**Existing DTOs:**
- `AddMemberDto` in `KanbAI-Core/DTOs/AddMemberDto.cs`:
  - Property: `Guid UserId` (required, validated with DataAnnotations)
- `MemberResponseDto` in `KanbAI-Core/DTOs/MemberResponseDto.cs`:
  - Properties: `UserId` (string), `Name` (string), `Email` (string), `Role` (string), `JoinedAt` (DateTimeOffset)

**Existing Service Layer:**
- `IProjectService` in `KanbAI-Core/Services/Projects/IProjectService.cs`:
  - `AddMemberAsync(Guid projectId, Guid userIdToAdd, Guid requestingUserId)` - lines 66-69
    - Returns `(MemberResponseDto? member, string? errorMessage)`
    - Validates: project exists, requesting user is owner, user to add exists, no duplicate memberships
  - `RemoveMemberAsync(Guid projectId, Guid userIdToRemove, Guid requestingUserId)` - lines 85-88
    - Returns `(bool isRemoved, string? errorMessage)`
  - **No method exists to retrieve all members of a project**
  - **No method accepts email address for member lookup**

**Domain Model:**
- `ProjectMember` entity in `KanbAI-Core/Models/Entities/ProjectMember.cs`:
  - Properties: `ProjectId`, `UserId`, `Role` (ProjectRole enum: Member or Owner)
  - Navigation properties: `Project`, `User`
  - Inherits from `BaseEntity` (provides Id, CreatedAt, UpdatedAt)
- `User` entity in `KanbAI-Core/Models/Entities/User.cs`:
  - Properties: `Name`, `Email`, `PasswordHash`, `Role` (UserRole enum)
  - Navigation property: `ICollection<ProjectMember> ProjectMemberships`

**Current Implementation Gap:**
1. **No GET /api/Project/{projectId}/members endpoint** - Frontend cannot fetch the member list
2. **AddMemberDto requires Guid UserId** - Frontend has no way to discover user GUIDs without an additional user search API

### Desired State

**New API Endpoint:**
- `GET /api/Project/{projectId}/members` - Retrieves all members of a project
  - **Authorization:** Only project members (Owner or Member) can call this endpoint
  - **Response:** 200 OK with `List<MemberResponseDto>` (ordered by role descending, then by join date ascending)
  - **Error Cases:**
    - 404 Not Found: Project does not exist or requesting user is not a member
  - **Location:** New method in `ProjectController`

**Enhanced Add Member Endpoint:**
- `POST /api/Project/{projectId}/members` - Extend to accept email address OR Guid UserId
  - **Request Body:** Modified `AddMemberDto` to support either `Email` (string) or `UserId` (Guid)
  - **Service Layer Lookup:** If email is provided, the service queries the Users table to find the corresponding Guid
  - **Error Cases (Enhanced):**
    - 400 Bad Request: Email provided but no user with that email exists
    - 400 Bad Request: Email provided but is malformed (fails basic email validation)
    - (All existing error cases from Issue #44 remain: duplicate member, non-owner requesting, etc.)

**Service Layer Enhancements:**
- New method in `IProjectService`:
  - `GetProjectMembersAsync(Guid projectId, Guid requestingUserId)` - Returns `List<MemberResponseDto>` or null if unauthorized
- Modified method signature (or overload) in `IProjectService`:
  - Accept email address as an alternative to Guid UserId in the Add Member flow
  - Internal logic resolves email to Guid before validation and persistence

**DTO Changes:**
- `AddMemberDto` modification:
  - **Option A (Flexible):** Support both `Email` (string, optional) and `UserId` (Guid, optional) with validation ensuring exactly one is provided
  - **Option B (Email-Only):** Replace `Guid UserId` with `string Email` (simpler, aligns with frontend needs)
  - **Recommendation:** Let the Staff Engineer decide based on backward compatibility considerations

---

## ✅ Acceptance Criteria

**As a project member, I can retrieve the list of all members in a project so that I can see who has access to the project.**

- [ ] A GET /api/Project/{projectId}/members endpoint exists
- [ ] The endpoint requires a valid JWT token (authenticated user)
- [ ] The endpoint returns 404 Not Found if the project does not exist
- [ ] The endpoint returns 404 Not Found if the authenticated user is not a member of the project
- [ ] Upon successful retrieval, the endpoint returns 200 OK with a list of `MemberResponseDto` objects
- [ ] Each `MemberResponseDto` contains: UserId, Name, Email, Role, and JoinedAt timestamp
- [ ] The list is ordered by role descending (Owners first, then Members) and by join date ascending within each role
- [ ] The response follows the existing `ApiResponse<List<MemberResponseDto>>.Ok(data)` pattern
- [ ] Both project owners and regular members can call this endpoint (authorization check: "is user a member of the project?")

**As a project owner, I can add a new member to my project using their email address so that I do not need to know their system-generated user ID.**

- [ ] The existing POST /api/Project/{projectId}/members endpoint accepts an email address in the request body
- [ ] When an email address is provided, the service looks up the corresponding user in the Users table
- [ ] If no user with the provided email exists, the endpoint returns 400 Bad Request with a clear error message (e.g., "No user found with email address: example@domain.com")
- [ ] If the email address is malformed (fails basic validation), the endpoint returns 400 Bad Request with a clear error message
- [ ] If the user is found by email, the existing validation logic applies: check for duplicate membership, check for owner authorization, etc.
- [ ] Upon successful addition, the endpoint returns 201 Created with the newly added member's details (same as current behavior)
- [ ] All existing acceptance criteria from Issue #44 remain valid (owner-only enforcement, duplicate detection, error handling)

**Email lookup is case-insensitive and trims whitespace to ensure robust matching.**

- [ ] The email lookup query uses case-insensitive comparison (e.g., "User@Example.COM" matches "user@example.com")
- [ ] Leading and trailing whitespace in the provided email address is trimmed before lookup

**Error messages are specific and actionable for frontend developers.**

- [ ] When email lookup fails, the error message includes the email address that was searched (e.g., "No user found with email address: invalid@example.com")
- [ ] When both Email and UserId are provided (if applicable), the error message clarifies which parameter to use
- [ ] When neither Email nor UserId are provided (if applicable), the error message specifies that one is required

**The implementation is backward-compatible if the DTO change supports both Email and UserId.**

- [ ] If the Staff Engineer chooses to support both `Email` and `UserId` in `AddMemberDto`, existing API clients using `UserId` continue to work without changes
- [ ] Validation logic ensures exactly one identifier (Email OR UserId) is provided, not both and not neither

**Edge cases are handled gracefully.**

- [ ] An empty project (only the owner exists) returns a list with one member when GET /members is called
- [ ] Email lookup with an empty string returns 400 Bad Request (not 500 Internal Server Error)
- [ ] Email lookup with a null value is caught by model validation before reaching the service layer
- [ ] Requesting the member list for a non-existent project GUID returns 404 Not Found (not 500 Internal Server Error)

---

## 🎯 Out of Scope (Future Enhancements)

These capabilities are explicitly **not** part of this issue and should be deferred to future work:

- **Dedicated User Search Endpoint** - No GET /api/User/search?query=email endpoint (email lookup happens only within the Add Member context)
- **Pagination for Member List** - The GET /members endpoint returns all members in a single response (reasonable for small-to-medium teams)
- **Filtering or Sorting Options** - The member list is always sorted by role then join date (no query parameters for custom sorting)
- **Member Profile Details** - The response contains only the fields in `MemberResponseDto` (no additional user profile data like avatar, bio, etc.)
- **Batch Email Import** - No ability to add multiple members by providing a list of email addresses in a single request

---

## 📚 Reference Materials

**Related Issues:**
- [#44 - Implement Project Member Management](https://github.com/Gulybi/KanbAI-Core/issues/44) - Foundation for Add/Remove Member endpoints
- [#43 - Implement Project CRUD Operations](https://github.com/Gulybi/KanbAI-Core/issues/43) - ProjectController and IProjectService interface

**Relevant Files:**
- `KanbAI-Core/Controllers/ProjectController.cs` - Existing controller to be extended with GET /members endpoint
- `KanbAI-Core/Services/Projects/IProjectService.cs` - Service interface to be extended with GetProjectMembersAsync method
- `KanbAI-Core/Services/Projects/ProjectService.cs` - Service implementation (lines 156-218 contain AddMemberAsync logic to be enhanced)
- `KanbAI-Core/DTOs/AddMemberDto.cs` - DTO to be modified to accept Email as an alternative to UserId
- `KanbAI-Core/DTOs/MemberResponseDto.cs` - Response DTO (already contains all needed fields, no changes required)
- `KanbAI-Core/Models/Entities/User.cs` - User entity with Email property (line 8)
- `KanbAI-Core/Models/Entities/ProjectMember.cs` - Junction entity linking users to projects

**Coding Standards:**
- `.claude/rules/code-standards.md` - Modern C# conventions (async/await, AsNoTracking for read-only queries, file-scoped namespaces)
- `.claude/rules/security-safety.md` - Input validation, no PII in logs, parameterized queries
- `.claude/rules/testing-observability.md` - Unit test structure (AAA pattern), xUnit naming conventions

**Existing Patterns to Follow:**
- Authorization checks: Follow the pattern in `AddMemberAsync` (lines 161-185 in ProjectService.cs) - verify requesting user is a project member, then check role
- Error tuple returns: Use `(TResult? data, string? errorMessage)` pattern for service methods (consistent with AddMemberAsync and RemoveMemberAsync)
- DTO mapping: Reuse the existing `MapToMemberDto` helper (lines 291-301 in ProjectService.cs)
- Structured logging: Use parameterized logging with `ILogger<ProjectService>` (see lines 214-215 for example)

---

## 🚦 Success Metrics

This feature is considered complete when:
1. The frontend can successfully fetch the member list for a project and display it in the UI
2. The frontend can successfully add a new member by email address without needing to query for the user's Guid
3. All existing member management tests from Issue #44 continue to pass (no regressions)
4. New integration tests demonstrate: fetch empty member list (owner only), fetch multi-member list (owner + members), add member by email (success), add member by non-existent email (400 error)
5. Edge cases are validated: case-insensitive email lookup, trimmed whitespace, malformed email validation
6. The implementation passes code review with zero security vulnerabilities (no information leakage via email enumeration attacks)

---

**Prepared by:** Product Manager Agent  
**Date:** 2026-04-29

---

The business context is defined and saved. You can now instruct the staff-engineer to read the context note and design the technical specification.
