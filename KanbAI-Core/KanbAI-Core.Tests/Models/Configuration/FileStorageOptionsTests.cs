using FluentAssertions;
using KanbAI_Core.Models.Configuration;

namespace KanbAI_Core.Tests.Models.Configuration;

/// <summary>
/// Unit tests for <see cref="FileStorageOptions"/>.
/// Tests constants and default values before configuration binding.
/// </summary>
public class FileStorageOptionsTests
{
    [Fact]
    public void SectionName_IsFileStorage()
    {
        // Arrange & Act
        var sectionName = FileStorageOptions.SectionName;

        // Assert
        sectionName.Should().Be("FileStorage");
    }

    [Fact]
    public void DefaultValues_AreEmpty()
    {
        // Arrange & Act
        var options = new FileStorageOptions();

        // Assert
        options.StoragePath.Should().BeEmpty();
        options.MaxFileSizeBytes.Should().Be(0);
        options.AllowedExtensions.Should().BeEmpty();
    }
}
