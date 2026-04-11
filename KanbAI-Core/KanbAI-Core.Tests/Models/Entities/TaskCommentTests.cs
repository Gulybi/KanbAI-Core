using FluentAssertions;
using KanbAI_Core.Models.Entities;

namespace KanbAI_Core.Tests.Models.Entities;

public class TaskCommentTests
{
    [Fact]
    public void TaskComment_InheritsFromBaseEntity()
    {
        // Arrange
        var type = typeof(TaskComment);

        // Act
        var isSubclass = type.IsSubclassOf(typeof(BaseEntity));

        // Assert
        isSubclass.Should().BeTrue("TaskComment must inherit from BaseEntity");
    }

    [Fact]
    public void TaskComment_ContentProperty_IsStringType()
    {
        // Arrange
        var property = typeof(TaskComment).GetProperty(nameof(TaskComment.Content));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(string));
    }

    [Fact]
    public void TaskComment_KanbanTaskIdProperty_IsGuidType()
    {
        // Arrange
        var property = typeof(TaskComment).GetProperty(nameof(TaskComment.KanbanTaskId));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(Guid));
    }

    [Fact]
    public void TaskComment_KanbanTaskNavigationProperty_Exists()
    {
        // Arrange
        var property = typeof(TaskComment).GetProperty(nameof(TaskComment.KanbanTask));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(KanbanTask));
    }

    [Fact]
    public void TaskComment_AuthorIdProperty_IsGuidType()
    {
        // Arrange
        var property = typeof(TaskComment).GetProperty(nameof(TaskComment.AuthorId));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(Guid));
    }

    [Fact]
    public void TaskComment_AuthorNavigationProperty_Exists()
    {
        // Arrange
        var property = typeof(TaskComment).GetProperty(nameof(TaskComment.Author));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(User));
    }

    [Fact]
    public void TaskComment_AllProperties_HavePublicGettersAndSetters()
    {
        // Arrange
        var properties = new[]
        {
            "Content", "KanbanTaskId", "KanbanTask", "AuthorId", "Author",
            "Id", "CreatedAt", "UpdatedAt"
        };

        // Act & Assert
        foreach (var name in properties)
        {
            var prop = typeof(TaskComment).GetProperty(name);
            prop.Should().NotBeNull($"property '{name}' should exist");
            prop!.GetMethod.Should().NotBeNull($"'{name}' should have a getter");
            prop.GetMethod!.IsPublic.Should().BeTrue($"'{name}' getter should be public");
            prop.SetMethod.Should().NotBeNull($"'{name}' should have a setter");
            prop.SetMethod!.IsPublic.Should().BeTrue($"'{name}' setter should be public");
        }
    }

    [Fact]
    public void TaskComment_DefaultPropertyValues_AreExpected()
    {
        // Arrange & Act
        var comment = new TaskComment();

        // Assert
        comment.Content.Should().Be(string.Empty);
        comment.KanbanTaskId.Should().Be(Guid.Empty);
        comment.AuthorId.Should().Be(Guid.Empty);
    }
}
