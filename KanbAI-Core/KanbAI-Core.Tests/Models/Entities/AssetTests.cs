using System.Reflection;
using FluentAssertions;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;

namespace KanbAI_Core.Tests.Models.Entities;

public class AssetTests
{
    [Fact]
    public void Asset_InheritsFromBaseEntity()
    {
        // Arrange
        var type = typeof(Asset);

        // Act
        var isSubclass = type.IsSubclassOf(typeof(BaseEntity));

        // Assert
        isSubclass.Should().BeTrue("Asset must inherit from BaseEntity");
    }

    [Fact]
    public void Asset_FileNameProperty_IsStringType()
    {
        // Arrange
        var property = typeof(Asset).GetProperty(nameof(Asset.FileName));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(string));
    }

    [Fact]
    public void Asset_StorageKeyProperty_IsStringType()
    {
        // Arrange
        var property = typeof(Asset).GetProperty(nameof(Asset.StorageKey));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(string));
    }

    [Fact]
    public void Asset_ThumbnailKeyProperty_IsNullableStringType()
    {
        // Arrange
        var property = typeof(Asset).GetProperty(nameof(Asset.ThumbnailKey));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(string));

        var nullabilityContext = new NullabilityInfoContext();
        var nullabilityInfo = nullabilityContext.Create(property);
        nullabilityInfo.WriteState.Should().Be(NullabilityState.Nullable,
            "ThumbnailKey should be a nullable string (string?)");
    }

    [Fact]
    public void Asset_MimeTypeProperty_IsStringType()
    {
        // Arrange
        var property = typeof(Asset).GetProperty(nameof(Asset.MimeType));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(string));
    }

    [Fact]
    public void Asset_FileSizeProperty_IsLongType()
    {
        // Arrange
        var property = typeof(Asset).GetProperty(nameof(Asset.FileSize));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(long));
    }

    [Fact]
    public void Asset_ProcessingStatusProperty_IsProcessingStatusType()
    {
        // Arrange
        var property = typeof(Asset).GetProperty(nameof(Asset.ProcessingStatus));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(ProcessingStatus));
    }

    [Fact]
    public void Asset_KanbanTaskIdProperty_IsGuidType()
    {
        // Arrange
        var property = typeof(Asset).GetProperty(nameof(Asset.KanbanTaskId));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(Guid));
    }

    [Fact]
    public void Asset_KanbanTaskNavigationProperty_Exists()
    {
        // Arrange
        var property = typeof(Asset).GetProperty(nameof(Asset.KanbanTask));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(KanbanTask));
    }

    [Fact]
    public void Asset_AllProperties_HavePublicGettersAndSetters()
    {
        // Arrange
        var properties = new[]
        {
            "FileName", "StorageKey", "ThumbnailKey", "MimeType", "FileSize",
            "ProcessingStatus", "KanbanTaskId", "KanbanTask",
            "Id", "CreatedAt", "UpdatedAt"
        };

        // Act & Assert
        foreach (var name in properties)
        {
            var prop = typeof(Asset).GetProperty(name);
            prop.Should().NotBeNull($"property '{name}' should exist");
            prop!.GetMethod.Should().NotBeNull($"'{name}' should have a getter");
            prop.GetMethod!.IsPublic.Should().BeTrue($"'{name}' getter should be public");
            prop.SetMethod.Should().NotBeNull($"'{name}' should have a setter");
            prop.SetMethod!.IsPublic.Should().BeTrue($"'{name}' setter should be public");
        }
    }

    [Fact]
    public void Asset_DefaultPropertyValues_AreExpected()
    {
        // Arrange & Act
        var asset = new Asset();

        // Assert
        asset.FileName.Should().Be(string.Empty);
        asset.StorageKey.Should().Be(string.Empty);
        asset.ThumbnailKey.Should().BeNull();
        asset.MimeType.Should().Be(string.Empty);
        asset.FileSize.Should().Be(0);
        asset.ProcessingStatus.Should().Be(ProcessingStatus.Pending);
    }
}
