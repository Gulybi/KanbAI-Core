using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using KanbAI_Core.Data;
using KanbAI_Core.DTOs;
using KanbAI_Core.Tests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KanbAI_Core.Tests.Integration;

/// <summary>
/// Integration tests for Task API endpoints.
/// These tests focus on HTTP-level concerns (status codes, validation, authentication).
/// Database-level concerns are covered by unit tests using in-memory EF Core.
/// </summary>
public class TaskApiIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public TaskApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    #region Security Tests

    [Fact]
    public async Task PostTask_Unauthenticated_Returns401()
    {
        // Arrange
        var client = CreateUnauthenticatedClient();
        var columnId = Guid.NewGuid();
        var dto = new CreateTaskDto { Title = "Task" };

        // Act
        var response = await client.PostAsJsonAsync($"/api/task/column/{columnId}", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task PostTask_MissingTitleInBody_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var columnId = Guid.NewGuid();

        var invalidBody = new { content = "missing title" };

        // Act
        var response = await client.PostAsJsonAsync($"/api/task/column/{columnId}", invalidBody);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostTask_TitleTooLong_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var columnId = Guid.NewGuid();

        var dto = new CreateTaskDto { Title = new string('A', 201) };

        // Act
        var response = await client.PostAsJsonAsync($"/api/task/column/{columnId}", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostTask_EmptyTitleInBody_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var columnId = Guid.NewGuid();

        var body = new { title = "" };

        // Act
        var response = await client.PostAsJsonAsync($"/api/task/column/{columnId}", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region MoveTask Security & Validation Tests

    [Fact]
    public async Task PutMoveTask_Unauthenticated_Returns401()
    {
        // Arrange
        var client = CreateUnauthenticatedClient();
        var taskId = Guid.NewGuid();
        var dto = new MoveTaskDto { ColumnId = Guid.NewGuid(), TaskOrder = 0 };

        // Act
        var response = await client.PutAsJsonAsync($"/api/task/{taskId}/move", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PutMoveTask_MissingColumnId_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var taskId = Guid.NewGuid();

        var invalidBody = new { taskOrder = 0 };

        // Act
        var response = await client.PutAsJsonAsync($"/api/task/{taskId}/move", invalidBody);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PutMoveTask_NegativeTaskOrder_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var taskId = Guid.NewGuid();

        var body = new { columnId = Guid.NewGuid(), taskOrder = -1 };

        // Act
        var response = await client.PutAsJsonAsync($"/api/task/{taskId}/move", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region UpdateTaskDescription Integration Tests

    [Fact]
    public async Task PutTaskDescription_Unauthenticated_Returns401()
    {
        // Arrange
        var client = CreateUnauthenticatedClient();
        var taskId = Guid.NewGuid();
        var dto = new UpdateTaskDescriptionDto { Content = "Description" };

        // Act
        var response = await client.PutAsJsonAsync($"/api/task/{taskId}/description", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PutTaskDescription_MissingContentInBody_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var taskId = Guid.NewGuid();

        var invalidBody = new { title = "missing content" };

        // Act
        var response = await client.PutAsJsonAsync($"/api/task/{taskId}/description", invalidBody);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PutTaskDescription_NullContent_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var taskId = Guid.NewGuid();

        var body = new { content = (string?)null };

        // Act
        var response = await client.PutAsJsonAsync($"/api/task/{taskId}/description", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PutTaskDescription_WhitespaceOnlyContent_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var taskId = Guid.NewGuid();

        var dto = new UpdateTaskDescriptionDto { Content = "   \n\t  " };

        // Act
        var response = await client.PutAsJsonAsync($"/api/task/{taskId}/description", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PutTaskDescription_ContentExceeds10000Chars_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var taskId = Guid.NewGuid();

        var dto = new UpdateTaskDescriptionDto { Content = new string('A', 10_001) };

        // Act
        var response = await client.PutAsJsonAsync($"/api/task/{taskId}/description", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region ClearTaskDescription Integration Tests

    [Fact]
    public async Task DeleteTaskDescription_Unauthenticated_Returns401()
    {
        // Arrange
        var client = CreateUnauthenticatedClient();
        var taskId = Guid.NewGuid();

        // Act
        var response = await client.DeleteAsync($"/api/task/{taskId}/description");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region DeleteTask Integration Tests

    [Fact]
    public async Task DeleteTask_Unauthenticated_Returns401()
    {
        // Arrange
        var client = CreateUnauthenticatedClient();
        var taskId = Guid.NewGuid();

        // Act
        var response = await client.DeleteAsync($"/api/task/{taskId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteTask_NonExistentTask_Returns404()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId, Guid.NewGuid().ToString());
        var nonExistentTaskId = Guid.NewGuid();

        // Act
        var response = await client.DeleteAsync($"/api/task/{nonExistentTaskId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var apiResponse = await response.Content.ReadFromJsonAsync<ApiResponse>();
        apiResponse.Should().NotBeNull();
        apiResponse!.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Task not found.");
    }

    [Fact]
    public async Task DeleteTask_Idempotent_SecondCallReturns404()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId, Guid.NewGuid().ToString());
        var taskId = Guid.NewGuid();

        // Act - First delete
        var firstResponse = await client.DeleteAsync($"/api/task/{taskId}");

        // Act - Second delete of same task
        var secondResponse = await client.DeleteAsync($"/api/task/{taskId}");

        // Assert
        // First call should return 404 (task doesn't exist in test)
        // Second call should also return 404
        firstResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteTask_MemberDeletesExistingTask_Returns204AndRemovesTask()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var projectId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        await SeedProjectWithTaskAsync(databaseName, userId, projectId, columnId, taskId);

        var client = CreateAuthenticatedClient(userId, databaseName);

        // Act
        var response = await client.DeleteAsync($"/api/task/{taskId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().BeEmpty();

        await using var verifyContext = CreateContextForDb(databaseName);
        (await verifyContext.KanbanTasks.FindAsync(taskId)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteTask_NonMemberDeletesTask_Returns403WithStructuredError()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var outsiderId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var projectId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        await SeedProjectWithTaskAsync(databaseName, ownerId, projectId, columnId, taskId);

        var client = CreateAuthenticatedClient(outsiderId, databaseName);

        // Act
        var response = await client.DeleteAsync($"/api/task/{taskId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var apiResponse = await response.Content.ReadFromJsonAsync<ApiResponse>();
        apiResponse.Should().NotBeNull();
        apiResponse!.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("You are not a member of this project.");
        apiResponse.Errors.Should().BeEmpty();

        await using var verifyContext = CreateContextForDb(databaseName);
        (await verifyContext.KanbanTasks.FindAsync(taskId)).Should().NotBeNull(
            "the task must not be deleted when the caller is not a project member");
    }

    [Fact]
    public async Task DeleteTask_TwoDeletesInARow_FirstReturns204ThenSecondReturns404()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var projectId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        await SeedProjectWithTaskAsync(databaseName, userId, projectId, columnId, taskId);

        var client = CreateAuthenticatedClient(userId, databaseName);

        // Act
        var firstResponse = await client.DeleteAsync($"/api/task/{taskId}");
        var secondResponse = await client.DeleteAsync($"/api/task/{taskId}");

        // Assert
        firstResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var secondBody = await secondResponse.Content.ReadFromJsonAsync<ApiResponse>();
        secondBody!.Message.Should().Be("Task not found.");
    }

    #endregion

    #region Seed Helpers

    private static async Task SeedProjectWithTaskAsync(
        string databaseName,
        Guid ownerUserId,
        Guid projectId,
        Guid columnId,
        Guid taskId)
    {
        await using var context = CreateContextForDb(databaseName);

        context.Users.Add(new KanbAI_Core.Models.Entities.User
        {
            Id = ownerUserId,
            Name = "Owner",
            Email = $"owner-{ownerUserId}@example.com",
            PasswordHash = "hash"
        });

        context.Projects.Add(new KanbAI_Core.Models.Entities.Project
        {
            Id = projectId,
            Name = "Integration Test Project"
        });

        context.ProjectMembers.Add(new KanbAI_Core.Models.Entities.ProjectMember
        {
            ProjectId = projectId,
            UserId = ownerUserId,
            Role = KanbAI_Core.Models.Enums.ProjectRole.Owner
        });

        context.BoardColumns.Add(new KanbAI_Core.Models.Entities.BoardColumn
        {
            Id = columnId,
            Name = "To Do",
            ColumnOrder = 0,
            ProjectId = projectId
        });

        context.KanbanTasks.Add(new KanbAI_Core.Models.Entities.KanbanTask
        {
            Id = taskId,
            Title = "Task to delete",
            TaskOrder = 0,
            ColumnId = columnId
        });

        await context.SaveChangesAsync();
    }

    private static ApplicationDbContext CreateContextForDb(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options);
    }

    #endregion

    #region Test Infrastructure

    private HttpClient CreateAuthenticatedClient(Guid userId, string? inMemoryDatabaseName = null)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                var authConfigs = services
                    .Where(d => d.ServiceType == typeof(IConfigureOptions<AuthenticationOptions>))
                    .ToList();
                foreach (var descriptor in authConfigs)
                    services.Remove(descriptor);

                services.AddAuthentication("Test")
                    .AddScheme<TestAuthSchemeOptions, TestAuthHandler>(
                        "Test",
                        options => options.UserId = userId);

                services.AddAuthorization(options => options.FallbackPolicy = null);

                if (inMemoryDatabaseName != null)
                {
                    var dbContextDescriptors = services
                        .Where(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                                 || d.ServiceType == typeof(DbContextOptions)
                                 || d.ServiceType == typeof(ApplicationDbContext)
                                 || (d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore") ?? false))
                        .ToList();
                    foreach (var descriptor in dbContextDescriptors)
                    {
                        services.Remove(descriptor);
                    }

                    services.AddDbContext<ApplicationDbContext>(options =>
                        options.UseInMemoryDatabase(inMemoryDatabaseName));
                }
            });
        }).CreateClient();
    }

    private HttpClient CreateUnauthenticatedClient()
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                var authConfigs = services
                    .Where(d => d.ServiceType == typeof(IConfigureOptions<AuthenticationOptions>))
                    .ToList();
                foreach (var descriptor in authConfigs)
                    services.Remove(descriptor);

                services.AddAuthentication("Test")
                    .AddScheme<TestAuthSchemeOptions, TestAuthHandler>("Test", _ => { });
            });
        }).CreateClient();
    }

    private sealed class TestAuthSchemeOptions : AuthenticationSchemeOptions
    {
        public Guid? UserId { get; set; }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<TestAuthSchemeOptions>
    {
        public TestAuthHandler(
            IOptionsMonitor<TestAuthSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Options.UserId.HasValue)
            {
                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, Options.UserId.Value.ToString())
                };
                var identity = new ClaimsIdentity(claims, "Test");
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, "Test");

                return Task.FromResult(AuthenticateResult.Success(ticket));
            }

            return Task.FromResult(AuthenticateResult.NoResult());
        }
    }

    #endregion
}
