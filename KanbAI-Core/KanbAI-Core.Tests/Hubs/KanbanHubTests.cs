using System.Security.Claims;
using FluentAssertions;
using KanbAI_Core.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;

namespace KanbAI_Core.Tests.Hubs;

/// <summary>
/// Unit tests for <see cref="KanbanHub"/>. Uses mocked <see cref="IGroupManager"/>
/// and <see cref="HubCallerContext"/> so hub method logic, validation, and logging
/// can be exercised in isolation from the SignalR transport.
/// </summary>
public class KanbanHubTests
{
    private readonly Mock<IGroupManager> _mockGroups;
    private readonly Mock<HubCallerContext> _mockContext;
    private readonly Mock<ILogger<KanbanHub>> _mockLogger;
    private readonly KanbanHub _hub;
    private const string ConnectionId = "conn-12345";

    public KanbanHubTests()
    {
        _mockGroups = new Mock<IGroupManager>();
        _mockContext = new Mock<HubCallerContext>();
        _mockLogger = new Mock<ILogger<KanbanHub>>();

        _mockContext.Setup(c => c.ConnectionId).Returns(ConnectionId);

        _hub = new KanbanHub(_mockLogger.Object)
        {
            Groups = _mockGroups.Object,
            Context = _mockContext.Object
        };
    }

    #region JoinProjectGroup - Happy Path

    [Fact]
    public async Task JoinProjectGroup_ValidProjectId_AddsConnectionToGroup()
    {
        // Arrange
        var projectId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();
        _mockContext.Setup(c => c.User).Returns(CreateClaimsPrincipal(userId));

        // Act
        await _hub.JoinProjectGroup(projectId);

        // Assert
        var expectedGroupName = $"project_{projectId.ToLowerInvariant()}";
        _mockGroups.Verify(
            g => g.AddToGroupAsync(ConnectionId, expectedGroupName, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task JoinProjectGroup_ValidProjectId_LogsInformationEvent()
    {
        // Arrange
        var projectId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();
        _mockContext.Setup(c => c.User).Returns(CreateClaimsPrincipal(userId));

        // Act
        await _hub.JoinProjectGroup(projectId);

        // Assert
        VerifyLog(LogLevel.Information, "joined project group", Times.Once());
    }

    [Fact]
    public async Task JoinProjectGroup_ValidGuid_NormalizesToLowercase()
    {
        // Arrange
        var projectGuid = Guid.NewGuid();
        var upperCaseProjectId = projectGuid.ToString().ToUpperInvariant();
        var userId = Guid.NewGuid().ToString();
        _mockContext.Setup(c => c.User).Returns(CreateClaimsPrincipal(userId));

        // Act
        await _hub.JoinProjectGroup(upperCaseProjectId);

        // Assert
        var expectedGroupName = $"project_{projectGuid.ToString().ToLowerInvariant()}";
        _mockGroups.Verify(
            g => g.AddToGroupAsync(ConnectionId, expectedGroupName, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region JoinProjectGroup - Validation

    [Fact]
    public async Task JoinProjectGroup_NullProjectId_ThrowsHubException()
    {
        // Arrange
        _mockContext.Setup(c => c.User).Returns(CreateClaimsPrincipal(Guid.NewGuid().ToString()));

        // Act
        var act = async () => await _hub.JoinProjectGroup(null!);

        // Assert
        var exception = await act.Should().ThrowAsync<HubException>();
        exception.Which.Message.Should().Be("Project ID is required.");
        _mockGroups.Verify(
            g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task JoinProjectGroup_EmptyProjectId_ThrowsHubException()
    {
        // Arrange
        _mockContext.Setup(c => c.User).Returns(CreateClaimsPrincipal(Guid.NewGuid().ToString()));

        // Act
        var act = async () => await _hub.JoinProjectGroup(string.Empty);

        // Assert
        var exception = await act.Should().ThrowAsync<HubException>();
        exception.Which.Message.Should().Be("Project ID is required.");
    }

    [Fact]
    public async Task JoinProjectGroup_WhitespaceProjectId_ThrowsHubException()
    {
        // Arrange
        _mockContext.Setup(c => c.User).Returns(CreateClaimsPrincipal(Guid.NewGuid().ToString()));

        // Act
        var act = async () => await _hub.JoinProjectGroup("   ");

        // Assert
        var exception = await act.Should().ThrowAsync<HubException>();
        exception.Which.Message.Should().Be("Project ID is required.");
    }

    [Fact]
    public async Task JoinProjectGroup_InvalidGuidFormat_ThrowsHubException()
    {
        // Arrange
        _mockContext.Setup(c => c.User).Returns(CreateClaimsPrincipal(Guid.NewGuid().ToString()));

        // Act
        var act = async () => await _hub.JoinProjectGroup("not-a-guid");

        // Assert
        var exception = await act.Should().ThrowAsync<HubException>();
        exception.Which.Message.Should().Be("Invalid project ID format.");
        _mockGroups.Verify(
            g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task JoinProjectGroup_InvalidGuidFormat_LogsWarning()
    {
        // Arrange
        _mockContext.Setup(c => c.User).Returns(CreateClaimsPrincipal(Guid.NewGuid().ToString()));

        // Act
        var act = async () => await _hub.JoinProjectGroup("abc-123");
        await act.Should().ThrowAsync<HubException>();

        // Assert
        VerifyLog(LogLevel.Warning, "invalid projectId format", Times.Once());
    }

    #endregion

    #region LeaveProjectGroup - Happy Path

    [Fact]
    public async Task LeaveProjectGroup_ValidProjectId_RemovesConnectionFromGroup()
    {
        // Arrange
        var projectId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();
        _mockContext.Setup(c => c.User).Returns(CreateClaimsPrincipal(userId));

        // Act
        await _hub.LeaveProjectGroup(projectId);

        // Assert
        var expectedGroupName = $"project_{projectId.ToLowerInvariant()}";
        _mockGroups.Verify(
            g => g.RemoveFromGroupAsync(ConnectionId, expectedGroupName, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LeaveProjectGroup_ValidProjectId_LogsInformationEvent()
    {
        // Arrange
        var projectId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();
        _mockContext.Setup(c => c.User).Returns(CreateClaimsPrincipal(userId));

        // Act
        await _hub.LeaveProjectGroup(projectId);

        // Assert
        VerifyLog(LogLevel.Information, "left project group", Times.Once());
    }

    [Fact]
    public async Task LeaveProjectGroup_GroupNotJoined_DoesNotThrow()
    {
        // Arrange
        var projectId = Guid.NewGuid().ToString();
        _mockContext.Setup(c => c.User).Returns(CreateClaimsPrincipal(Guid.NewGuid().ToString()));

        // SignalR's built-in IGroupManager treats "remove from group never joined" as a no-op.
        // Our mock must faithfully reproduce that behavior — no exception, completed task.
        _mockGroups
            .Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var act = async () => await _hub.LeaveProjectGroup(projectId);

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region LeaveProjectGroup - Validation

    [Fact]
    public async Task LeaveProjectGroup_NullProjectId_ThrowsHubException()
    {
        // Arrange
        _mockContext.Setup(c => c.User).Returns(CreateClaimsPrincipal(Guid.NewGuid().ToString()));

        // Act
        var act = async () => await _hub.LeaveProjectGroup(null!);

        // Assert
        var exception = await act.Should().ThrowAsync<HubException>();
        exception.Which.Message.Should().Be("Project ID is required.");
    }

    [Fact]
    public async Task LeaveProjectGroup_EmptyProjectId_ThrowsHubException()
    {
        // Arrange
        _mockContext.Setup(c => c.User).Returns(CreateClaimsPrincipal(Guid.NewGuid().ToString()));

        // Act
        var act = async () => await _hub.LeaveProjectGroup(string.Empty);

        // Assert
        var exception = await act.Should().ThrowAsync<HubException>();
        exception.Which.Message.Should().Be("Project ID is required.");
    }

    [Fact]
    public async Task LeaveProjectGroup_InvalidGuidFormat_ThrowsHubException()
    {
        // Arrange
        _mockContext.Setup(c => c.User).Returns(CreateClaimsPrincipal(Guid.NewGuid().ToString()));

        // Act
        var act = async () => await _hub.LeaveProjectGroup("not-a-guid");

        // Assert
        var exception = await act.Should().ThrowAsync<HubException>();
        exception.Which.Message.Should().Be("Invalid project ID format.");
        _mockGroups.Verify(
            g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Connection Lifecycle

    [Fact]
    public async Task OnConnectedAsync_LogsConnectionEvent()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        _mockContext.Setup(c => c.User).Returns(CreateClaimsPrincipal(userId));

        // Act
        await _hub.OnConnectedAsync();

        // Assert
        VerifyLog(LogLevel.Information, "connected to KanbanHub", Times.Once());
    }

    [Fact]
    public async Task OnDisconnectedAsync_WithoutException_LogsInformationEvent()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        _mockContext.Setup(c => c.User).Returns(CreateClaimsPrincipal(userId));

        // Act
        await _hub.OnDisconnectedAsync(null);

        // Assert
        VerifyLog(LogLevel.Information, "disconnected from KanbanHub", Times.Once());
    }

    [Fact]
    public async Task OnDisconnectedAsync_WithException_LogsWarningEvent()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        _mockContext.Setup(c => c.User).Returns(CreateClaimsPrincipal(userId));
        var exception = new InvalidOperationException("Connection lost");

        // Act
        await _hub.OnDisconnectedAsync(exception);

        // Assert
        VerifyLog(LogLevel.Warning, "disconnected from KanbanHub with exception", Times.Once());
    }

    #endregion

    #region GetUserId (via observable behavior)

    [Fact]
    public async Task OnConnectedAsync_AuthenticatedUser_LogsNameIdentifierClaim()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        _mockContext.Setup(c => c.User).Returns(CreateClaimsPrincipal(userId));

        // Act
        await _hub.OnConnectedAsync();

        // Assert — the log message template includes {UserId}, and the parameter is resolved
        // from the NameIdentifier claim via the hub's GetUserId helper.
        VerifyLogContainsValue(LogLevel.Information, userId, Times.Once());
    }

    [Fact]
    public async Task OnConnectedAsync_UnauthenticatedUser_LogsAnonymous()
    {
        // Arrange
        _mockContext.Setup(c => c.User).Returns((ClaimsPrincipal?)null);

        // Act
        await _hub.OnConnectedAsync();

        // Assert
        VerifyLogContainsValue(LogLevel.Information, "anonymous", Times.Once());
    }

    [Fact]
    public async Task OnConnectedAsync_MissingNameIdentifierClaim_LogsAnonymous()
    {
        // Arrange — principal exists but has no NameIdentifier claim
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Email, "foo@bar.com") }, "TestAuth");
        _mockContext.Setup(c => c.User).Returns(new ClaimsPrincipal(identity));

        // Act
        await _hub.OnConnectedAsync();

        // Assert
        VerifyLogContainsValue(LogLevel.Information, "anonymous", Times.Once());
    }

    #endregion

    #region Test Helpers

    private static ClaimsPrincipal CreateClaimsPrincipal(string userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Verifies that <see cref="ILogger{TCategoryName}.Log"/> was invoked with the given
    /// log level and a message template containing the given substring.
    /// </summary>
    private void VerifyLog(LogLevel expectedLevel, string messageSubstring, Times times)
    {
        _mockLogger.Verify(
            l => l.Log(
                expectedLevel,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(messageSubstring)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }

    /// <summary>
    /// Verifies that the log message (after parameter substitution) contains the given value.
    /// Used to confirm that claim values (e.g. user id) were rendered into the structured log.
    /// </summary>
    private void VerifyLogContainsValue(LogLevel expectedLevel, string expectedValue, Times times)
    {
        _mockLogger.Verify(
            l => l.Log(
                expectedLevel,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(expectedValue)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }

    #endregion
}
