# Issue 11: Setup Dependency Injection (DI) Extension Methods

## Title & Business Value
**Title:** Setup Dependency Injection (DI) Extension Methods
**Business Value:** This improves the maintainability and readability of the application's entry point by organizing service registrations into dedicated extension methods. A clean entry point makes it easier for developers to understand and manage the application's dependencies as the project grows.

## Current State vs. Desired State
**Current State:** Services, repositories, and database contexts are currently registered directly in `Program.cs`, which can lead to a cluttered and hard-to-maintain application entry point.
**Desired State:** The registration logic is moved to a dedicated static extension class (e.g., `ServiceCollectionExtensions`). `Program.cs` simply calls these extension methods, resulting in a clean and organized startup configuration.

## Acceptance Criteria
- [ ] A static extension class (e.g., `ServiceCollectionExtensions`) is created to manage DI registrations.
- [ ] Registration logic for repositories, services, and database contexts is successfully moved out of `Program.cs` and into the new extension class.
- [ ] `Program.cs` is updated to use the new extension methods for registering dependencies.
- [ ] The application compiles, starts, and runs successfully with the new DI setup, verifying that all dependencies are correctly resolved.