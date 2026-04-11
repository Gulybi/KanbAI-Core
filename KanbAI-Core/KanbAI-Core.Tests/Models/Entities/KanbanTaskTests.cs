using System.Reflection;
using FluentAssertions;
using KanbAI_Core.Models.Entities;

namespace KanbAI_Core.Tests.Models.Entities;

public class KanbanTaskTests
{
    [Fact]
    public void KanbanTask_InheritsFromBaseEntity()
    {
        // Arrange
        var type = typeof(KanbanTask);

        // Act
        var isSubclass = type.IsSubclassOf(typeof(BaseEntity));

        // Assert
        isSubclass.Should().BeTrue("KanbanTask must inherit from BaseEntity");
    }

    [Fact]
    public void KanbanTask_TitleProperty_IsStringType()
    {
        // Arrange
        var property = typeof(KanbanTask).GetProperty(nameof(KanbanTask.Title));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(string));
    }

    [Fact]
    public void KanbanTask_ContentProperty_IsNullableStringType()
    {
        // Arrange
        var property = typeof(KanbanTask).GetProperty(nameof(KanbanTask.Content));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(string));

        var nullabilityContext = new NullabilityInfoContext();
        var nullabilityInfo = nullabilityContext.Create(property);
        nullabilityInfo.WriteState.Should().Be(NullabilityState.Nullable,
            "Content should be a nullable string (string?)");
    }

    [Fact]
    public void KanbanTask_TaskOrderProperty_IsIntType()
    {
        // Arrange
        var property = typeof(KanbanTask).GetProperty(nameof(KanbanTask.TaskOrder));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(int));
    }

    [Fact]
    public void KanbanTask_ColumnIdProperty_IsGuidType()
    {
        // Arrange
        var property = typeof(KanbanTask).GetProperty(nameof(KanbanTask.ColumnId));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(Guid));
    }

    [Fact]
    public void KanbanTask_ColumnNavigationProperty_Exists()
    {
        // Arrange
        var property = typeof(KanbanTask).GetProperty(nameof(KanbanTask.Column));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(BoardColumn));
    }

    [Fact]
    public void KanbanTask_AssignedIdProperty_IsNullableGuidType()
    {
        // Arrange
        var property = typeof(KanbanTask).GetProperty(nameof(KanbanTask.AssignedId));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(Guid?));
    }

    [Fact]
    public void KanbanTask_AssignedUserNavigationProperty_Exists()
    {
        // Arrange
        var property = typeof(KanbanTask).GetProperty(nameof(KanbanTask.AssignedUser));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(User));

        var nullabilityContext = new NullabilityInfoContext();
        var nullabilityInfo = nullabilityContext.Create(property);
        nullabilityInfo.WriteState.Should().Be(NullabilityState.Nullable,
            "AssignedUser should be nullable (User?)");
    }

    [Fact]
    public void KanbanTask_AllProperties_HavePublicGettersAndSetters()
    {
        // Arrange
        var properties = new[]
        {
            "Title", "Content", "TaskOrder", "ColumnId", "Column",
            "AssignedId", "AssignedUser", "Id", "CreatedAt", "UpdatedAt"
        };

        // Act & Assert
        foreach (var name in properties)
        {
            var prop = typeof(KanbanTask).GetProperty(name);
            prop.Should().NotBeNull($"property '{name}' should exist");
            prop!.GetMethod.Should().NotBeNull($"'{name}' should have a getter");
            prop.GetMethod!.IsPublic.Should().BeTrue($"'{name}' getter should be public");
            prop.SetMethod.Should().NotBeNull($"'{name}' should have a setter");
            prop.SetMethod!.IsPublic.Should().BeTrue($"'{name}' setter should be public");
        }
    }

    [Fact]
    public void KanbanTask_DefaultPropertyValues_AreExpected()
    {
        // Arrange & Act
        var task = new KanbanTask();

        // Assert
        task.Title.Should().Be(string.Empty);
        task.Content.Should().BeNull();
        task.TaskOrder.Should().Be(0);
        task.AssignedId.Should().BeNull();
        task.AssignedUser.Should().BeNull();
    }

    [Fact]
    public void KanbanTask_AssetsProperty_IsCollectionOfAsset()
    {
        // Arrange
        var property = typeof(KanbanTask).GetProperty(nameof(KanbanTask.Assets));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().BeAssignableTo(typeof(ICollection<Asset>));
    }

    [Fact]
    public void KanbanTask_CommentsProperty_IsCollectionOfTaskComment()
    {
        // Arrange
        var property = typeof(KanbanTask).GetProperty(nameof(KanbanTask.Comments));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().BeAssignableTo(typeof(ICollection<TaskComment>));
    }
}
