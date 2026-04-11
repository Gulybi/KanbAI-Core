using FluentAssertions;
using KanbAI_Core.Models.Enums;

namespace KanbAI_Core.Tests.Models.Enums;

public class ProcessingStatusTests
{
    [Fact]
    public void ProcessingStatus_HasExpectedValues()
    {
        // Arrange & Act & Assert
        ((int)ProcessingStatus.Pending).Should().Be(0);
        ((int)ProcessingStatus.Processing).Should().Be(1);
        ((int)ProcessingStatus.Completed).Should().Be(2);
        ((int)ProcessingStatus.Failed).Should().Be(3);
    }

    [Fact]
    public void ProcessingStatus_DefaultValue_IsPending()
    {
        // Arrange & Act
        var defaultStatus = default(ProcessingStatus);

        // Assert
        defaultStatus.Should().Be(ProcessingStatus.Pending);
    }
}
