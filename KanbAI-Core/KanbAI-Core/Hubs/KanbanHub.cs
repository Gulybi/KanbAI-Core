namespace KanbAI_Core.Hubs;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

[Authorize]
public sealed class KanbanHub : Hub
{
    private readonly ILogger<KanbanHub> _logger;

    public KanbanHub(ILogger<KanbanHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Adds the calling connection to a project-specific SignalR group.
    /// Clients in the same group receive identical broadcast messages sent to that group.
    /// </summary>
    /// <param name="projectId">The project's Guid identifier as a string.</param>
    /// <exception cref="HubException">
    /// Thrown when projectId is null, empty, whitespace, or not a valid Guid format.
    /// </exception>
    public async Task JoinProjectGroup(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            _logger.LogWarning(
                "User {UserId} attempted to join a project group with null/empty projectId (connection {ConnectionId})",
                GetUserId(), Context.ConnectionId);
            throw new HubException("Project ID is required.");
        }

        if (!Guid.TryParse(projectId, out var projectGuid))
        {
            _logger.LogWarning(
                "User {UserId} attempted to join a project group with invalid projectId format: {ProjectId} (connection {ConnectionId})",
                GetUserId(), projectId, Context.ConnectionId);
            throw new HubException("Invalid project ID format.");
        }

        var groupName = $"project_{projectGuid.ToString().ToLowerInvariant()}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        _logger.LogInformation(
            "User {UserId} joined project group {GroupName} (connection {ConnectionId})",
            GetUserId(), groupName, Context.ConnectionId);
    }

    /// <summary>
    /// Removes the calling connection from a project-specific SignalR group.
    /// Clients navigating away from a project board should invoke this method to stop receiving updates.
    /// </summary>
    /// <param name="projectId">The project's Guid identifier as a string.</param>
    /// <exception cref="HubException">
    /// Thrown when projectId is null, empty, whitespace, or not a valid Guid format.
    /// </exception>
    public async Task LeaveProjectGroup(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            _logger.LogWarning(
                "User {UserId} attempted to leave a project group with null/empty projectId (connection {ConnectionId})",
                GetUserId(), Context.ConnectionId);
            throw new HubException("Project ID is required.");
        }

        if (!Guid.TryParse(projectId, out var projectGuid))
        {
            _logger.LogWarning(
                "User {UserId} attempted to leave a project group with invalid projectId format: {ProjectId} (connection {ConnectionId})",
                GetUserId(), projectId, Context.ConnectionId);
            throw new HubException("Invalid project ID format.");
        }

        var groupName = $"project_{projectGuid.ToString().ToLowerInvariant()}";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

        _logger.LogInformation(
            "User {UserId} left project group {GroupName} (connection {ConnectionId})",
            GetUserId(), groupName, Context.ConnectionId);
    }

    /// <summary>
    /// Invoked when a client connects to the hub.
    /// Logs connection events for observability.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation(
            "User {UserId} connected to KanbanHub (connection {ConnectionId})",
            GetUserId(), Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Invoked when a client disconnects from the hub.
    /// Logs disconnection events for observability.
    /// SignalR automatically cleans up group memberships on disconnect.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception != null)
        {
            _logger.LogWarning(
                exception,
                "User {UserId} disconnected from KanbanHub with exception (connection {ConnectionId})",
                GetUserId(), Context.ConnectionId);
        }
        else
        {
            _logger.LogInformation(
                "User {UserId} disconnected from KanbanHub (connection {ConnectionId})",
                GetUserId(), Context.ConnectionId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Extracts the user ID from the authenticated user's JWT claims.
    /// Returns "anonymous" if the user is not authenticated or the NameIdentifier claim is missing.
    /// </summary>
    private string GetUserId()
    {
        return Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
    }
}
