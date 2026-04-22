---
name: testing-observability
description: Mandatory standards for automated testing (xUnit), mocking boundaries, and structured logging for observability.
---

# Testing & Observability Standards

These rules apply whenever writing automated tests or implementing logging within the application. High test quality and clear observability are critical for long-term maintainability.

## 1. 🧪 Automated Testing (xUnit)
- **Framework:** Use xUnit as the primary testing framework. Use FluentAssertions for assertions if available in the project.
- **Naming Convention:** Strictly follow the `MethodName_StateUnderTest_ExpectedBehavior` naming pattern for all test methods (e.g., `GetOrderById_OrderDoesNotExist_ReturnsNotFound`).
- **Structure (AAA):** Every test MUST follow the Arrange-Act-Assert pattern. Keep these three sections visually separated by blank lines.
- **Test Coverage:** Do not just test the "Happy Path". You must explicitly write tests for edge cases (null inputs, empty collections) and failure modes (exceptions, 400/404 responses).

## 2. 🎭 Mocking & Isolation
- **Unit vs. Integration:** Unit tests must be completely isolated. NEVER hit a real database, file system, or external API in a unit test.
- **Mocking Framework:** Use the project's standard mocking library (e.g., Moq or NSubstitute) to mock dependencies (`IRepository`, external services).
- **Setup Specificity:** Be specific when setting up mocks. Verify that expected methods were called with the correct parameters rather than just using `It.IsAny()`.
- **Integration Tests:** For end-to-end tests using `WebApplicationFactory<Program>`, follow the patterns documented in the integration testing standards.

## 3. 🔭 Observability & Structured Logging
- **Structured Logs:** Always use semantic, structured logging via `ILogger<T>`. 
- **No String Interpolation:** NEVER use string interpolation (`$"{variable}"`) inside logging message templates. Use parameterized templates so log aggregators (like Serilog/Elasticsearch) can index the properties.
  - *BAD:* `_logger.LogInformation($"User {userId} logged in.");`
  - *GOOD:* `_logger.LogInformation("User {UserId} logged in.", userId);`
- **Appropriate Log Levels:**
  - `Error`: For unhandled exceptions or critical failures that require immediate attention.
  - `Warning`: For handled exceptions, retries, or unexpected but non-fatal states (e.g., "Rate limit exceeded").
  - `Information`: For significant business events (e.g., "Order placed", "Entity created").
  - `Debug`: For detailed tracing information used only during development.

## 4. 📊 Test Organization
- **Test Projects:** Keep tests in separate projects (e.g., `KanbAI-Core.Tests`, `KanbAI-Core.IntegrationTests`).
- **Mirror Structure:** Test file structure should mirror the production code structure.
- **Shared Fixtures:** Use xUnit's `IClassFixture<T>` and `ICollectionFixture<T>` for shared setup.

## 5. 🎯 Assertion Best Practices
- **Specific Assertions:** Use specific assertions rather than generic ones (e.g., `Should().BeEmpty()` instead of `Should().HaveCount(0)`).
- **Multiple Assertions:** Group related assertions together, but avoid testing multiple unrelated behaviors in a single test.
- **Error Messages:** Provide clear failure messages for custom assertions.

## 6. 🔄 Test Maintenance
- **Keep Tests Fast:** Unit tests should run in milliseconds. Slow tests indicate integration concerns leaking in.
- **No Test Interdependencies:** Each test should be independent and runnable in any order.
- **Clean Up:** Always clean up resources (files, database connections) in test teardown.
