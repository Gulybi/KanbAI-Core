using FluentAssertions;
using KanbAI_Core.Data;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KanbAI_Core.Tests.Data.Configurations;

public class BoardColumnConfigurationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;

    public BoardColumnConfigurationTests()
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
    public async Task BoardColumnConfiguration_Name_IsRequired()
    {
        // Arrange
        var project = CreateValidProject();
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            Name = null!,
            ColumnOrder = 0,
            ProjectId = project.Id
        };

        // Act
        _context.BoardColumns.Add(column);
        var act = () => _context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task BoardColumnConfiguration_Name_MaxLength100_Accepted()
    {
        // Arrange
        var project = CreateValidProject();
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var columnId = Guid.NewGuid();
        var column = new BoardColumn
        {
            Id = columnId,
            Name = new string('A', 100),
            ColumnOrder = 0,
            ProjectId = project.Id
        };

        // Act
        _context.BoardColumns.Add(column);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var savedColumn = await _context.BoardColumns.FindAsync(columnId);

        // Assert
        savedColumn.Should().NotBeNull();
        savedColumn!.Name.Should().HaveLength(100);
    }

    [Fact]
    public async Task BoardColumnConfiguration_ColorCode_IsOptional()
    {
        // Arrange
        var project = CreateValidProject();
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var columnId = Guid.NewGuid();
        var column = new BoardColumn
        {
            Id = columnId,
            Name = "To Do",
            ColorCode = null,
            ColumnOrder = 0,
            ProjectId = project.Id
        };

        // Act
        _context.BoardColumns.Add(column);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var savedColumn = await _context.BoardColumns.FindAsync(columnId);

        // Assert
        savedColumn.Should().NotBeNull();
        savedColumn!.ColorCode.Should().BeNull();
    }

    [Fact]
    public async Task BoardColumnConfiguration_CascadeDelete_RemovesColumnsWhenProjectDeleted()
    {
        // Arrange
        var project = CreateValidProject();
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            Name = "To Do",
            ColumnOrder = 0,
            ProjectId = project.Id
        };
        _context.BoardColumns.Add(column);
        await _context.SaveChangesAsync();

        // Act
        _context.Projects.Remove(project);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var remainingColumns = await _context.BoardColumns
            .Where(bc => bc.ProjectId == project.Id)
            .ToListAsync();

        // Assert
        remainingColumns.Should().BeEmpty();
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
