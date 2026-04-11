using System.Reflection;
using FluentAssertions;
using KanbAI_Core.Models.Entities;

namespace KanbAI_Core.Tests.Models.Entities;

public class BoardColumnTests
{
    [Fact]
    public void BoardColumn_InheritsFromBaseEntity()
    {
        // Arrange
        var type = typeof(BoardColumn);

        // Act
        var isSubclass = type.IsSubclassOf(typeof(BaseEntity));

        // Assert
        isSubclass.Should().BeTrue("BoardColumn must inherit from BaseEntity");
    }

    [Fact]
    public void BoardColumn_NameProperty_IsStringType()
    {
        // Arrange
        var property = typeof(BoardColumn).GetProperty(nameof(BoardColumn.Name));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(string));
    }

    [Fact]
    public void BoardColumn_ColorCodeProperty_IsNullableStringType()
    {
        // Arrange
        var property = typeof(BoardColumn).GetProperty(nameof(BoardColumn.ColorCode));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(string));

        var nullabilityContext = new NullabilityInfoContext();
        var nullabilityInfo = nullabilityContext.Create(property);
        nullabilityInfo.WriteState.Should().Be(NullabilityState.Nullable,
            "ColorCode should be a nullable string (string?)");
    }

    [Fact]
    public void BoardColumn_ColumnOrderProperty_IsIntType()
    {
        // Arrange
        var property = typeof(BoardColumn).GetProperty(nameof(BoardColumn.ColumnOrder));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(int));
    }

    [Fact]
    public void BoardColumn_ProjectIdProperty_IsGuidType()
    {
        // Arrange
        var property = typeof(BoardColumn).GetProperty(nameof(BoardColumn.ProjectId));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(Guid));
    }

    [Fact]
    public void BoardColumn_ProjectNavigationProperty_Exists()
    {
        // Arrange
        var property = typeof(BoardColumn).GetProperty(nameof(BoardColumn.Project));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(Project));
    }

    [Fact]
    public void BoardColumn_TasksProperty_IsCollectionOfKanbanTask()
    {
        // Arrange
        var property = typeof(BoardColumn).GetProperty(nameof(BoardColumn.Tasks));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().BeAssignableTo(typeof(ICollection<KanbanTask>));
    }

    [Fact]
    public void BoardColumn_AllProperties_HavePublicGettersAndSetters()
    {
        // Arrange
        var properties = new[]
        {
            "Name", "ColorCode", "ColumnOrder", "ProjectId", "Project", "Tasks",
            "Id", "CreatedAt", "UpdatedAt"
        };

        // Act & Assert
        foreach (var name in properties)
        {
            var prop = typeof(BoardColumn).GetProperty(name);
            prop.Should().NotBeNull($"property '{name}' should exist");
            prop!.GetMethod.Should().NotBeNull($"'{name}' should have a getter");
            prop.GetMethod!.IsPublic.Should().BeTrue($"'{name}' getter should be public");
            prop.SetMethod.Should().NotBeNull($"'{name}' should have a setter");
            prop.SetMethod!.IsPublic.Should().BeTrue($"'{name}' setter should be public");
        }
    }

    [Fact]
    public void BoardColumn_DefaultPropertyValues_AreExpected()
    {
        // Arrange & Act
        var column = new BoardColumn();

        // Assert
        column.Name.Should().Be(string.Empty);
        column.ColorCode.Should().BeNull();
        column.ColumnOrder.Should().Be(0);
        column.Tasks.Should().NotBeNull();
        column.Tasks.Should().BeEmpty();
    }
}
