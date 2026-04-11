using System.Reflection;
using FluentAssertions;
using KanbAI_Core.Models.Entities;

namespace KanbAI_Core.Tests.Models.Entities;

public class BaseEntityTests
{
    [Fact]
    public void BaseEntity_ClassDefinition_IsAbstract()
    {
        // Arrange
        var type = typeof(BaseEntity);

        // Act
        var isAbstract = type.IsAbstract;

        // Assert
        isAbstract.Should().BeTrue("BaseEntity must be abstract to prevent direct instantiation");
    }

    [Fact]
    public void BaseEntity_IdProperty_IsGuidType()
    {
        // Arrange
        var property = typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(Guid));
    }

    [Fact]
    public void BaseEntity_CreatedAtProperty_IsDateTimeOffsetType()
    {
        // Arrange
        var property = typeof(BaseEntity).GetProperty(nameof(BaseEntity.CreatedAt));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(DateTimeOffset));
    }

    [Fact]
    public void BaseEntity_UpdatedAtProperty_IsDateTimeOffsetType()
    {
        // Arrange
        var property = typeof(BaseEntity).GetProperty(nameof(BaseEntity.UpdatedAt));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(DateTimeOffset));
    }

    [Fact]
    public void BaseEntity_AllProperties_HavePublicGettersAndSetters()
    {
        // Arrange
        var properties = new[] { "Id", "CreatedAt", "UpdatedAt" };

        // Act & Assert
        foreach (var name in properties)
        {
            var prop = typeof(BaseEntity).GetProperty(name);
            prop.Should().NotBeNull($"property '{name}' should exist");
            prop!.GetMethod.Should().NotBeNull($"'{name}' should have a getter");
            prop.GetMethod!.IsPublic.Should().BeTrue($"'{name}' getter should be public");
            prop.SetMethod.Should().NotBeNull($"'{name}' should have a setter");
            prop.SetMethod!.IsPublic.Should().BeTrue($"'{name}' setter should be public");
        }
    }

    [Fact]
    public void ConcreteSubclass_InheritsAllProperties_AccessibleViaBaseType()
    {
        // Arrange
        var entity = new ConcreteTestEntity();
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Act
        entity.Id = id;
        entity.CreatedAt = now;
        entity.UpdatedAt = now;

        // Assert
        entity.Should().BeAssignableTo<BaseEntity>();
        entity.Id.Should().Be(id);
        entity.CreatedAt.Should().Be(now);
        entity.UpdatedAt.Should().Be(now);
    }

    [Fact]
    public void ConcreteSubclass_DefaultPropertyValues_AreTypeDefaults()
    {
        // Arrange & Act
        var entity = new ConcreteTestEntity();

        // Assert
        entity.Id.Should().Be(Guid.Empty);
        entity.CreatedAt.Should().Be(default(DateTimeOffset));
        entity.UpdatedAt.Should().Be(default(DateTimeOffset));
    }

    [Fact]
    public void ConcreteSubclass_CastToBaseEntity_RetainsPropertyValues()
    {
        // Arrange
        var id = Guid.NewGuid();
        var created = DateTimeOffset.UtcNow.AddDays(-1);
        var updated = DateTimeOffset.UtcNow;
        var entity = new ConcreteTestEntity { Id = id, CreatedAt = created, UpdatedAt = updated };

        // Act
        BaseEntity baseRef = entity;

        // Assert
        baseRef.Id.Should().Be(id);
        baseRef.CreatedAt.Should().Be(created);
        baseRef.UpdatedAt.Should().Be(updated);
    }

    private class ConcreteTestEntity : BaseEntity { }
}
