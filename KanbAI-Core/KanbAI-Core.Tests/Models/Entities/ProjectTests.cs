using FluentAssertions;
using KanbAI_Core.Models.Entities;

namespace KanbAI_Core.Tests.Models.Entities;

public class ProjectTests
{
    [Fact]
    public void Project_InheritsFromBaseEntity()
    {
        // Arrange
        var type = typeof(Project);

        // Act
        var isSubclass = type.IsSubclassOf(typeof(BaseEntity));

        // Assert
        isSubclass.Should().BeTrue("Project must inherit from BaseEntity");
    }

    [Fact]
    public void Project_NameProperty_IsStringType()
    {
        // Arrange
        var property = typeof(Project).GetProperty(nameof(Project.Name));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(string));
    }

    [Fact]
    public void Project_MembersProperty_IsCollectionOfProjectMember()
    {
        // Arrange
        var property = typeof(Project).GetProperty(nameof(Project.Members));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().BeAssignableTo(typeof(ICollection<ProjectMember>));
    }

    [Fact]
    public void Project_AllProperties_HavePublicGettersAndSetters()
    {
        // Arrange
        var properties = new[] { "Name", "Description", "Members", "Columns", "Id", "CreatedAt", "UpdatedAt" };

        // Act & Assert
        foreach (var name in properties)
        {
            var prop = typeof(Project).GetProperty(name);
            prop.Should().NotBeNull($"property '{name}' should exist");
            prop!.GetMethod.Should().NotBeNull($"'{name}' should have a getter");
            prop.GetMethod!.IsPublic.Should().BeTrue($"'{name}' getter should be public");
            prop.SetMethod.Should().NotBeNull($"'{name}' should have a setter");
            prop.SetMethod!.IsPublic.Should().BeTrue($"'{name}' setter should be public");
        }
    }

    [Fact]
    public void Project_DefaultPropertyValues_AreExpected()
    {
        // Arrange & Act
        var project = new Project();

        // Assert
        project.Name.Should().Be(string.Empty);
        project.Description.Should().BeNull();
        project.Members.Should().NotBeNull();
        project.Members.Should().BeEmpty();
        project.Columns.Should().NotBeNull();
        project.Columns.Should().BeEmpty();
    }

    [Fact]
    public void Project_ColumnsProperty_IsCollectionOfBoardColumn()
    {
        // Arrange
        var property = typeof(Project).GetProperty(nameof(Project.Columns));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().BeAssignableTo(typeof(ICollection<BoardColumn>));
    }

    [Fact]
    public void Description_IsNullableStringType()
    {
        // Arrange
        var property = typeof(Project).GetProperty(nameof(Project.Description));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(string));

        var nullabilityContext = new System.Reflection.NullabilityInfoContext();
        var nullabilityInfo = nullabilityContext.Create(property);
        nullabilityInfo.WriteState.Should().Be(System.Reflection.NullabilityState.Nullable);
    }

    [Fact]
    public void Description_DefaultValue_IsNull()
    {
        // Arrange & Act
        var project = new Project();

        // Assert
        project.Description.Should().BeNull();
    }

    [Fact]
    public void Description_SetAndGet_RoundTrips()
    {
        // Arrange
        var project = new Project();
        var expected = "A meaningful project description";

        // Act
        project.Description = expected;

        // Assert
        project.Description.Should().Be(expected);
    }
}
