using FluentAssertions;
using KanbAI_Core.Data;
using KanbAI_Core.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KanbAI_Core.Tests.Data;

public class SaveChangesTimestampTests
{
    [Fact]
    public async Task SaveChangesAsync_AddedEntity_SetsCreatedAtToApproximatelyUtcNow()
    {
        // Arrange
        using var context = CreateContext(nameof(SaveChangesAsync_AddedEntity_SetsCreatedAtToApproximatelyUtcNow));
        var entity = new StampTestEntity { Id = Guid.NewGuid(), Name = "Test" };
        var before = DateTimeOffset.UtcNow;

        // Act
        context.StampTestEntities.Add(entity);
        await context.SaveChangesAsync();
        var after = DateTimeOffset.UtcNow;

        // Assert
        entity.CreatedAt.Should().BeOnOrAfter(before);
        entity.CreatedAt.Should().BeOnOrBefore(after);
    }

    [Fact]
    public async Task SaveChangesAsync_AddedEntity_SetsUpdatedAtToApproximatelyUtcNow()
    {
        // Arrange
        using var context = CreateContext(nameof(SaveChangesAsync_AddedEntity_SetsUpdatedAtToApproximatelyUtcNow));
        var entity = new StampTestEntity { Id = Guid.NewGuid(), Name = "Test" };
        var before = DateTimeOffset.UtcNow;

        // Act
        context.StampTestEntities.Add(entity);
        await context.SaveChangesAsync();
        var after = DateTimeOffset.UtcNow;

        // Assert
        entity.UpdatedAt.Should().BeOnOrAfter(before);
        entity.UpdatedAt.Should().BeOnOrBefore(after);
    }

    [Fact]
    public async Task SaveChangesAsync_AddedEntity_CreatedAtEqualsUpdatedAt()
    {
        // Arrange
        using var context = CreateContext(nameof(SaveChangesAsync_AddedEntity_CreatedAtEqualsUpdatedAt));
        var entity = new StampTestEntity { Id = Guid.NewGuid(), Name = "Test" };

        // Act
        context.StampTestEntities.Add(entity);
        await context.SaveChangesAsync();

        // Assert — each timestamp is a separate DateTimeOffset.UtcNow call,
        // so sub-microsecond drift is expected.
        entity.CreatedAt.Should().BeCloseTo(entity.UpdatedAt, TimeSpan.FromSeconds(1),
            "both timestamps should be set at approximately the same instant on creation");
    }

    [Fact]
    public async Task SaveChangesAsync_ModifiedEntity_UpdatesOnlyUpdatedAt()
    {
        // Arrange
        using var context = CreateContext(nameof(SaveChangesAsync_ModifiedEntity_UpdatesOnlyUpdatedAt));
        var entity = new StampTestEntity { Id = Guid.NewGuid(), Name = "Original" };
        context.StampTestEntities.Add(entity);
        await context.SaveChangesAsync();

        var originalCreatedAt = entity.CreatedAt;
        var originalUpdatedAt = entity.UpdatedAt;

        await Task.Delay(50);

        // Act
        entity.Name = "Modified";
        context.Entry(entity).State = EntityState.Modified;
        await context.SaveChangesAsync();

        // Assert
        entity.CreatedAt.Should().Be(originalCreatedAt,
            "CreatedAt must not change on modification");
        entity.UpdatedAt.Should().BeAfter(originalUpdatedAt,
            "UpdatedAt must advance on modification");
    }

    [Fact]
    public async Task SaveChangesAsync_ModifiedEntity_CreatedAtRemainsUnchanged()
    {
        // Arrange
        using var context = CreateContext(nameof(SaveChangesAsync_ModifiedEntity_CreatedAtRemainsUnchanged));
        var entity = new StampTestEntity { Id = Guid.NewGuid(), Name = "Original" };
        context.StampTestEntities.Add(entity);
        await context.SaveChangesAsync();

        var originalCreatedAt = entity.CreatedAt;
        await Task.Delay(50);

        // Act
        entity.Name = "Updated";
        context.Entry(entity).State = EntityState.Modified;
        await context.SaveChangesAsync();

        // Assert
        entity.CreatedAt.Should().Be(originalCreatedAt);
    }

    [Fact]
    public async Task SaveChangesAsync_UnchangedEntity_DoesNotUpdateTimestamps()
    {
        // Arrange
        using var context = CreateContext(nameof(SaveChangesAsync_UnchangedEntity_DoesNotUpdateTimestamps));
        var entity = new StampTestEntity { Id = Guid.NewGuid(), Name = "Stable" };
        context.StampTestEntities.Add(entity);
        await context.SaveChangesAsync();

        var originalCreatedAt = entity.CreatedAt;
        var originalUpdatedAt = entity.UpdatedAt;

        // Act
        await context.SaveChangesAsync();

        // Assert
        entity.CreatedAt.Should().Be(originalCreatedAt);
        entity.UpdatedAt.Should().Be(originalUpdatedAt);
    }

    [Fact]
    public async Task SaveChangesAsync_MultipleAddedEntities_AllReceiveTimestamps()
    {
        // Arrange
        using var context = CreateContext(nameof(SaveChangesAsync_MultipleAddedEntities_AllReceiveTimestamps));
        var entities = Enumerable.Range(1, 3)
            .Select(i => new StampTestEntity { Id = Guid.NewGuid(), Name = $"Entity{i}" })
            .ToList();

        // Act
        context.StampTestEntities.AddRange(entities);
        await context.SaveChangesAsync();

        // Assert
        foreach (var entity in entities)
        {
            entity.CreatedAt.Should().NotBe(default(DateTimeOffset));
            entity.UpdatedAt.Should().NotBe(default(DateTimeOffset));
            entity.CreatedAt.Should().BeCloseTo(entity.UpdatedAt, TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public async Task SaveChangesAsync_NoTrackedEntities_ReturnsZeroWithoutError()
    {
        // Arrange
        using var context = CreateContext(nameof(SaveChangesAsync_NoTrackedEntities_ReturnsZeroWithoutError));

        // Act
        var result = await context.SaveChangesAsync();

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task SaveChangesAsync_AddedEntity_TimestampsAreUtc()
    {
        // Arrange
        using var context = CreateContext(nameof(SaveChangesAsync_AddedEntity_TimestampsAreUtc));
        var entity = new StampTestEntity { Id = Guid.NewGuid(), Name = "Utc" };

        // Act
        context.StampTestEntities.Add(entity);
        await context.SaveChangesAsync();

        // Assert
        entity.CreatedAt.Offset.Should().Be(TimeSpan.Zero, "CreatedAt should be UTC");
        entity.UpdatedAt.Offset.Should().Be(TimeSpan.Zero, "UpdatedAt should be UTC");
    }

    #region Test Infrastructure

    private static TimestampTestDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new TimestampTestDbContext(options);
    }

    private class StampTestEntity : BaseEntity
    {
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Inherits from ApplicationDbContext so the SaveChangesAsync timestamp
    /// override is exercised, while adding a DbSet for the test-only entity.
    /// </summary>
    private class TimestampTestDbContext : ApplicationDbContext
    {
        public DbSet<StampTestEntity> StampTestEntities => Set<StampTestEntity>();

        public TimestampTestDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }
    }

    #endregion
}
