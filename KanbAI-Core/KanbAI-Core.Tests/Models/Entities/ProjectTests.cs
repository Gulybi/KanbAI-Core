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
        var properties = new[] { "Name", "Members", "Id", "CreatedAt", "UpdatedAt" };

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
        project.Members.Should().NotBeNull();
        project.Members.Should().BeEmpty();
    }
}
