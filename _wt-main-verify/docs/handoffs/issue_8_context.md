# Context Handoff: Issue #8 - Implement Base Entities and Standard API Responses

## Title & Business Value
**Title:** Implement Base Entities and Standard API Responses
**Who:** The engineering team building and consuming the KanbAI-Core API.
**Why:** As the application grows and new domain entities are introduced, every database model will share common fields (identity, audit timestamps). Without a shared base, developers would duplicate these fields across every entity, leading to inconsistency, missed audit data, and harder maintenance. Similarly, API consumers (front-end applications, integrations) need a predictable response envelope so they can uniformly parse success payloads, error messages, and metadata without guessing the shape of each endpoint's output. These two foundational pieces—`BaseEntity` and a standard `ApiResponse` wrapper—are prerequisites for all future domain modelling and controller development.

## Current State vs. Desired State

**Current State:**
- The `ApplicationDbContext` is registered and connects to SQL Server, but contains **no `DbSet<>` properties** and **no entity classes**. An empty `InitialCreate` migration exists.
- There are no model/entity classes anywhere in the project. The `Models/Entities`, `DTOs`, and `Controllers` directories do **not** exist yet and need to be created.
- API responses are returned as raw objects (e.g., the template `WeatherForecast` record in `Program.cs` via minimal API). There is no standard wrapper that communicates success/failure, error details, or pagination metadata to the caller.

**Desired State:**
- A `BaseEntity` abstract class exists that encapsulates the fields every database entity will share: a unique identifier, a creation timestamp, and a last-updated timestamp.
- All future entity classes inherit from `BaseEntity`, guaranteeing consistent identity and audit columns across every table.
- A generic `ApiResponse<T>` wrapper class exists that every controller endpoint uses to return data. This wrapper provides a uniform contract containing at minimum: the data payload, a success/failure indicator, and a place for error or informational messages.
- API consumers can rely on a single, predictable JSON structure regardless of which endpoint they call.

## Acceptance Criteria
- [ ] An abstract `BaseEntity` class is created with (at minimum) `Id`, `CreatedAt`, and `UpdatedAt` properties.
- [ ] The `BaseEntity` class is placed in the appropriate project location following the established folder conventions (`Models/Entities`).
- [ ] A generic `ApiResponse<T>` wrapper class (or equivalent) is created that standardises the shape of all API responses.
- [ ] The `ApiResponse` wrapper includes at minimum: a data/payload field, a success indicator, and an error/message field.
- [ ] The wrapper is placed in the appropriate project location (e.g., `DTOs` or a dedicated `Responses` folder) following team conventions.
- [ ] Existing or sample endpoints can demonstrate usage of the `ApiResponse` wrapper (the template weather endpoint is acceptable for this purpose).
- [ ] The solution compiles and all existing tests continue to pass.
