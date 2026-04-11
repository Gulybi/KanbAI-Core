using FluentAssertions;
using KanbAI_Core.Data;
using KanbAI_Core.Models.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KanbAI_Core.Tests.Data.Configurations;

public class ProjectConfigurationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;

    public ProjectConfigurationTests()
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
    public async Task ProjectConfiguration_Name_IsRequired()
    {
        // Arrange
        var project = CreateValidProject();
        project.Name = null!;

        // Act
        _context.Projects.Add(project);
        var act = () => _context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ProjectConfiguration_Name_MaxLength200_Accepted()
    {
        // Arrange
        var project = CreateValidProject();
        project.Name = new string('A', 200);

        // Act
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var savedProject = await _context.Projects.FindAsync(project.Id);

        // Assert
        savedProject.Should().NotBeNull();
        savedProject!.Name.Should().HaveLength(200);
    }

    private static Project CreateValidProject() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Project"
    };

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
