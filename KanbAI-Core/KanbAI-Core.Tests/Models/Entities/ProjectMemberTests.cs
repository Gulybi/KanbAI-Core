using FluentAssertions;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;

namespace KanbAI_Core.Tests.Models.Entities;

public class ProjectMemberTests
{
    [Fact]
    public void ProjectMember_InheritsFromBaseEntity()
    {
        // Arrange
        var type = typeof(ProjectMember);

        // Act
        var isSubclass = type.IsSubclassOf(typeof(BaseEntity));

        // Assert
        isSubclass.Should().BeTrue("ProjectMember must inherit from BaseEntity");
    }

    [Fact]
    public void ProjectMember_ProjectIdProperty_IsGuidType()
    {
        // Arrange
        var property = typeof(ProjectMember).GetProperty(nameof(ProjectMember.ProjectId));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(Guid));
    }

    [Fact]
    public void ProjectMember_UserIdProperty_IsGuidType()
    {
        // Arrange
        var property = typeof(ProjectMember).GetProperty(nameof(ProjectMember.UserId));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(Guid));
    }

    [Fact]
    public void ProjectMember_RoleProperty_IsProjectRoleType()
    {
        // Arrange
        var property = typeof(ProjectMember).GetProperty(nameof(ProjectMember.Role));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(ProjectRole));
    }

    [Fact]
    public void ProjectMember_ProjectNavigationProperty_Exists()
    {
        // Arrange
        var property = typeof(ProjectMember).GetProperty(nameof(ProjectMember.Project));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(Project));
    }

    [Fact]
    public void ProjectMember_UserNavigationProperty_Exists()
    {
        // Arrange
        var property = typeof(ProjectMember).GetProperty(nameof(ProjectMember.User));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(User));
    }

    [Fact]
    public void ProjectMember_AllProperties_HavePublicGettersAndSetters()
    {
        // Arrange
        var properties = new[]
        {
            "ProjectId", "Project", "UserId", "User", "Role",
            "Id", "CreatedAt", "UpdatedAt"
        };

        // Act & Assert
        foreach (var name in properties)
        {
            var prop = typeof(ProjectMember).GetProperty(name);
            prop.Should().NotBeNull($"property '{name}' should exist");
            prop!.GetMethod.Should().NotBeNull($"'{name}' should have a getter");
            prop.GetMethod!.IsPublic.Should().BeTrue($"'{name}' getter should be public");
            prop.SetMethod.Should().NotBeNull($"'{name}' should have a setter");
            prop.SetMethod!.IsPublic.Should().BeTrue($"'{name}' setter should be public");
        }
    }

    [Fact]
    public void ProjectMember_DefaultRoleValue_IsMember()
    {
        // Arrange & Act
        var member = new ProjectMember();

        // Assert
        member.Role.Should().Be(ProjectRole.Member);
    }
}
