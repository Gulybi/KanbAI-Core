---
name: code-standards
description: Strict coding standards, .NET best practices, and architecture guidelines. Applies whenever writing or refactoring C# code.
---

# .NET Coding Standards & Best Practices

These rules dictate how C# code should be written, structured, and optimized. They apply strictly to any agent modifying or generating `.cs` files (e.g., Developer, QA Tester).

## 1. 🏗️ Modern C# Features & Clean Code
- **C# 10+ Features:** Always utilize modern C# capabilities. Use file-scoped namespaces, implicit usings, global usings, and `record` types for DTOs and value objects.
- **YAGNI (You Aren't Gonna Need It):** Do not over-engineer. If a feature requires a simple single-method update, do not generate complex design patterns or new layers unless explicitly instructed by the Staff Engineer's tech spec.
- **Dependency Injection:** Strictly use constructor injection for services and repositories. Never instantiate dependencies using the `new` keyword.

## 2. ⚡ Performance & Async/Await Rules
- **Asynchronous Operations:** All I/O operations (database calls, file reads, external API requests) MUST be asynchronous.
- **No Blocking:** NEVER use `.Result`, `.Wait()`, or `Task.Run()` to block async code. Always use `await`.
- **Collections:** Avoid multiple enumerations of `IEnumerable`. Materialize to a `List` or `Array` if the collection needs to be accessed more than once.

## 3. 🗄️ Entity Framework Core Optimization
- **Read-Only Queries:** ALWAYS use `.AsNoTracking()` for queries that only read data and do not intend to update the entities.
- **N+1 Problem Prevention:** ALWAYS prevent N+1 query issues. Use `.Include()` for eager loading or `.Select()` projections when fetching related data.

## 4. 🛡️ Error Handling
- **No Swallowed Exceptions:** Never use empty `catch` blocks or `catch (Exception ex)` without properly rethrowing or logging the error.
- **API Layer Exceptions:** Do not use `try-catch (Exception ex)` extensively in the API layer (Controllers/Minimal APIs). Rely on global Exception Middleware for unhandled errors.
- **Validation Errors:** Return proper HTTP 400 responses for validation failures with structured error details.

## 5. 📐 Code Organization
- **File-Scoped Namespaces:** Always use file-scoped namespace declarations (C# 10+):
  ```csharp
  namespace KanbAI_Core.Models.Entities;
  
  public class MyEntity { }
  ```
- **One Type Per File:** Each class, interface, enum, or record should be in its own file.
- **Folder Structure:** Follow the project's established conventions:
  - Entities → `Models/Entities/`
  - Enums → `Models/Enums/`
  - DTOs → `DTOs/`
  - Services → `Services/`
  - EF Configurations → `Data/Configurations/`

## 6. 🎯 Naming Conventions
- **PascalCase:** Classes, methods, properties, enums, and public members.
- **camelCase:** Private fields (with `_` prefix), local variables, parameters.
- **Descriptive Names:** Use clear, intention-revealing names. Avoid abbreviations unless they're universally understood (e.g., DTO, API).

## 7. 🔄 Best Practices
- **Nullability:** Enable nullable reference types and handle nulls explicitly.
- **Immutability:** Prefer `record` types for DTOs and value objects.
- **SOLID Principles:** Follow Single Responsibility Principle — each class should have one reason to change.
- **No Magic Strings/Numbers:** Use constants or enums instead of hardcoded values.
