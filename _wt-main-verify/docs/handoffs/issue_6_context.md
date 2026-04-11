# Issue #6: Initialize .NET Web API and Project Structure

## Title & Business Value
**What:** Initialize the foundational .NET Web API project and establish the core folder structure.
**Who:** The engineering team.
**Why:** A well-organized, standardized project structure is essential for maintainability, separation of concerns, and scalability as the KanbAI-Core application grows. It sets the foundation for all future backend development.

## Current State vs. Desired State
**Current State:** The repository currently lacks a .NET Web API project and the necessary directory structure for backend development.
**Desired State:** A fully initialized .NET Web API project exists with a standard `.gitignore` for .NET. The project includes a clear folder structure separating concerns into:
- `Controllers` (API endpoints)
- `Models/Entities` (Domain models)
- `Data` (DbContext and database configurations)
- `Services` (Business logic)
- `DTOs` (Data Transfer Objects)

## Acceptance Criteria
- [ ] A .NET Web API project is initialized using the .NET CLI.
- [ ] The standard .NET `.gitignore` file is added to the repository.
- [ ] The following directories are created within the project: `Controllers`, `Models/Entities`, `Data`, `Services`, and `DTOs`.
