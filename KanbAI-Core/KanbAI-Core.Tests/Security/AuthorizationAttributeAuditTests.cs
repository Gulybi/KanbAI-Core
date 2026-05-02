using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KanbAI_Core.Tests.Security;

/// <summary>
/// Security audit tests that use reflection to verify all controllers
/// have explicit authorization attributes ([Authorize] or [AllowAnonymous]).
/// These tests prevent accidental exposure of endpoints by enforcing
/// explicit security declarations at the controller level.
/// </summary>
public class AuthorizationAttributeAuditTests
{
    #region Security Audit Tests

    [Fact]
    public void AllControllers_HaveExplicitAuthorizationAttribute()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;
        var controllers = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Controller"))
            .ToList();

        // Act & Assert
        controllers.Should().NotBeEmpty("the assembly should contain controller classes");

        foreach (var controller in controllers)
        {
            var hasAuthorize = controller.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Any();
            var hasAllowAnonymous = controller.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true).Any();

            (hasAuthorize || hasAllowAnonymous).Should().BeTrue(
                $"Controller {controller.Name} is missing [Authorize] or [AllowAnonymous] attribute. " +
                "All controllers must explicitly declare their authorization requirements.");
        }
    }

    [Fact]
    public void AuthController_HasAllowAnonymousAttribute()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;
        var authController = assembly.GetTypes()
            .FirstOrDefault(t => t.Name == "AuthController");

        // Assert
        authController.Should().NotBeNull("AuthController must exist in the assembly");

        var hasAllowAnonymous = authController!
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true)
            .Any();

        hasAllowAnonymous.Should().BeTrue(
            "AuthController must have [AllowAnonymous] attribute to allow public access to /register and /login endpoints");
    }

    [Fact]
    public void HealthController_HasAllowAnonymousAttribute()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;
        var healthController = assembly.GetTypes()
            .FirstOrDefault(t => t.Name == "HealthController");

        // Assert
        healthController.Should().NotBeNull("HealthController must exist in the assembly");

        var hasAllowAnonymous = healthController!
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true)
            .Any();

        hasAllowAnonymous.Should().BeTrue(
            "HealthController must have [AllowAnonymous] attribute to allow public health checks");
    }

    [Fact]
    public void ProjectController_HasAuthorizeAttribute()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;
        var projectController = assembly.GetTypes()
            .FirstOrDefault(t => t.Name == "ProjectController");

        // Assert
        projectController.Should().NotBeNull("ProjectController must exist in the assembly");

        var hasAuthorize = projectController!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Any();

        hasAuthorize.Should().BeTrue(
            "ProjectController must have [Authorize] attribute to protect project endpoints");
    }

    [Fact]
    public void TaskController_HasAuthorizeAttribute()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;
        var taskController = assembly.GetTypes()
            .FirstOrDefault(t => t.Name == "TaskController");

        // Assert
        taskController.Should().NotBeNull("TaskController must exist in the assembly");

        var hasAuthorize = taskController!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Any();

        hasAuthorize.Should().BeTrue(
            "TaskController must have [Authorize] attribute to protect task endpoints");
    }

    [Fact]
    public void ColumnController_HasAuthorizeAttribute()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;
        var columnController = assembly.GetTypes()
            .FirstOrDefault(t => t.Name == "ColumnController");

        // Assert
        columnController.Should().NotBeNull("ColumnController must exist in the assembly");

        var hasAuthorize = columnController!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Any();

        hasAuthorize.Should().BeTrue(
            "ColumnController must have [Authorize] attribute to protect column endpoints");
    }

    #endregion

    #region Regression Prevention Tests

    [Fact]
    public void NoController_HasBothAuthorizeAndAllowAnonymous()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;
        var controllers = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Controller"))
            .ToList();

        // Act & Assert
        foreach (var controller in controllers)
        {
            var hasAuthorize = controller.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Any();
            var hasAllowAnonymous = controller.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true).Any();

            if (hasAuthorize && hasAllowAnonymous)
            {
                Assert.Fail(
                    $"Controller {controller.Name} has both [Authorize] and [AllowAnonymous] attributes at class level. " +
                    "This is a configuration conflict. Choose one or the other.");
            }
        }
    }

    [Fact]
    public void AllControllersAreApiControllers()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;
        var controllers = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Controller"))
            .ToList();

        // Act & Assert
        foreach (var controller in controllers)
        {
            var hasApiControllerAttribute = controller
                .GetCustomAttributes(typeof(ApiControllerAttribute), inherit: true)
                .Any();

            hasApiControllerAttribute.Should().BeTrue(
                $"Controller {controller.Name} must have [ApiController] attribute for proper API behavior");
        }
    }

    #endregion
}
