using FluentAssertions;
using KanbAI_Core.Data;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KanbAI_Core.Tests.Data.Configurations;

public class ProjectMemberConfigurationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;

    public ProjectMemberConfigurationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task ProjectMemberConfiguration_UniqueConstraint_PreventsDoubleMembership()
    {
        // Arrange
        var project = CreateValidProject();
        var user = CreateValidUser();

        _context.Projects.Add(project);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var member1 = new ProjectMember
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            UserId = user.Id,
            Role = ProjectRole.Owner
        };
        _context.ProjectMembers.Add(member1);
        await _context.SaveChangesAsync();

        // Act
        var member2 = new ProjectMember
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            UserId = user.Id,
            Role = ProjectRole.Member
        };
        _context.ProjectMembers.Add(member2);
        var act = () => _context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ProjectMemberConfiguration_Role_DefaultsToMember()
    {
        // Arrange
        var project = CreateValidProject();
        var user = CreateValidUser();

        _context.Projects.Add(project);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var memberId = Guid.NewGuid();
        var member = new ProjectMember
        {
            Id = memberId,
            ProjectId = project.Id,
            UserId = user.Id
        };

        // Act
        _context.ProjectMembers.Add(member);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var savedMember = await _context.ProjectMembers.FindAsync(memberId);

        // Assert
        savedMember.Should().NotBeNull();
        savedMember!.Role.Should().Be(ProjectRole.Member);
    }

    [Fact]
    public async Task ProjectMemberConfiguration_CascadeDelete_RemovesMembersWhenProjectDeleted()
    {
        // Arrange
        var project = CreateValidProject();
        var user = CreateValidUser();

        _context.Projects.Add(project);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var member = new ProjectMember
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            UserId = user.Id,
            Role = ProjectRole.Member
        };
        _context.ProjectMembers.Add(member);
        await _context.SaveChangesAsync();

        // Act
        _context.Projects.Remove(project);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var remainingMembers = await _context.ProjectMembers
            .Where(pm => pm.ProjectId == project.Id)
            .ToListAsync();

        // Assert
        remainingMembers.Should().BeEmpty();
    }

    [Fact]
    public async Task ProjectMemberConfiguration_RestrictDelete_PreventsUserDeletionWithMemberships()
    {
        // Arrange
        var project = CreateValidProject();
        var user = CreateValidUser();

        _context.Projects.Add(project);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var member = new ProjectMember
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            UserId = user.Id,
            Role = ProjectRole.Member
        };
        _context.ProjectMembers.Add(member);
        await _context.SaveChangesAsync();

        // Clear tracker so EF doesn't try to fix up relationships in memory;
        // this forces the FK constraint to be enforced at the database level.
        _context.ChangeTracker.Clear();

        var userToDelete = await _context.Users.FindAsync(user.Id);

        // Act
        _context.Users.Remove(userToDelete!);
        var act = () => _context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ProjectMemberConfiguration_DifferentUsersCanJoinSameProject()
    {
        // Arrange
        var project = CreateValidProject();
        var user1 = CreateValidUser();
        var user2 = CreateValidUser();

        _context.Projects.Add(project);
        _context.Users.AddRange(user1, user2);
        await _context.SaveChangesAsync();

        var member1 = new ProjectMember
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            UserId = user1.Id,
            Role = ProjectRole.Owner
        };
        var member2 = new ProjectMember
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            UserId = user2.Id,
            Role = ProjectRole.Member
        };

        // Act
        _context.ProjectMembers.AddRange(member1, member2);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var members = await _context.ProjectMembers
            .Where(pm => pm.ProjectId == project.Id)
            .ToListAsync();

        // Assert
        members.Should().HaveCount(2);
    }

    [Fact]
    public async Task ProjectMemberConfiguration_SameUserCanJoinDifferentProjects()
    {
        // Arrange
        var project1 = CreateValidProject();
        var project2 = CreateValidProject();
        var user = CreateValidUser();

        _context.Projects.AddRange(project1, project2);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var member1 = new ProjectMember
        {
            Id = Guid.NewGuid(),
            ProjectId = project1.Id,
            UserId = user.Id,
            Role = ProjectRole.Member
        };
        var member2 = new ProjectMember
        {
            Id = Guid.NewGuid(),
            ProjectId = project2.Id,
            UserId = user.Id,
            Role = ProjectRole.Owner
        };

        // Act
        _context.ProjectMembers.AddRange(member1, member2);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var memberships = await _context.ProjectMembers
            .Where(pm => pm.UserId == user.Id)
            .ToListAsync();

        // Assert
        memberships.Should().HaveCount(2);
    }

    private static Project CreateValidProject() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Project"
    };

    private static User CreateValidUser() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test User",
        Email = $"{Guid.NewGuid()}@example.com",
        PasswordHash = "$2a$11$examplehashvalue",
        Role = UserRole.Member
    };

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
