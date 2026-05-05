using FluentAssertions;
using KanbAI_Core.Models.Configuration;
using Microsoft.Extensions.Options;

namespace KanbAI_Core.Tests.Models.Configuration;

/// <summary>
/// Unit tests for <see cref="FileStorageOptionsValidator"/>.
/// Tests all validation rules including happy path, missing values, format validation, and security constraints.
/// </summary>
public class FileStorageOptionsValidatorTests
{
    private readonly FileStorageOptionsValidator _validator = new();

    #region Happy Path

    [Fact]
    public void Validate_ValidConfiguration_ReturnsSuccess()
    {
        // Arrange
        var options = new FileStorageOptions
        {
            StoragePath = "wwwroot/uploads",
            MaxFileSizeBytes = 10485760,
            AllowedExtensions = new[] { ".jpg", ".png", ".pdf" }
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    #endregion

    #region StoragePath Validation

    [Fact]
    public void Validate_StoragePathIsNull_ReturnsFailure()
    {
        // Arrange
        var options = new FileStorageOptions
        {
            StoragePath = null!,
            MaxFileSizeBytes = 10485760,
            AllowedExtensions = new[] { ".jpg" }
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("FileStorage:StoragePath is required and cannot be null or empty");
    }

    [Fact]
    public void Validate_StoragePathIsEmpty_ReturnsFailure()
    {
        // Arrange
        var options = new FileStorageOptions
        {
            StoragePath = "",
            MaxFileSizeBytes = 10485760,
            AllowedExtensions = new[] { ".jpg" }
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("FileStorage:StoragePath is required and cannot be null or empty");
    }

    [Fact]
    public void Validate_StoragePathIsWhitespace_ReturnsFailure()
    {
        // Arrange
        var options = new FileStorageOptions
        {
            StoragePath = "   ",
            MaxFileSizeBytes = 10485760,
            AllowedExtensions = new[] { ".jpg" }
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("FileStorage:StoragePath is required and cannot be null or empty");
    }

    [Fact]
    public void Validate_StoragePathEndsWithForwardSlash_ReturnsFailure()
    {
        // Arrange
        var options = new FileStorageOptions
        {
            StoragePath = "wwwroot/uploads/",
            MaxFileSizeBytes = 10485760,
            AllowedExtensions = new[] { ".jpg" }
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("FileStorage:StoragePath must NOT include a trailing slash");
    }

    [Fact]
    public void Validate_StoragePathEndsWithBackslash_ReturnsFailure()
    {
        // Arrange
        var options = new FileStorageOptions
        {
            StoragePath = "wwwroot\\uploads\\",
            MaxFileSizeBytes = 10485760,
            AllowedExtensions = new[] { ".jpg" }
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("FileStorage:StoragePath must NOT include a trailing slash");
    }

    #endregion

    #region MaxFileSizeBytes Validation

    [Fact]
    public void Validate_MaxFileSizeBytesIsZero_ReturnsFailure()
    {
        // Arrange
        var options = new FileStorageOptions
        {
            StoragePath = "wwwroot/uploads",
            MaxFileSizeBytes = 0,
            AllowedExtensions = new[] { ".jpg" }
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("FileStorage:MaxFileSizeBytes must be a positive integer greater than zero");
    }

    [Fact]
    public void Validate_MaxFileSizeBytesIsNegative_ReturnsFailure()
    {
        // Arrange
        var options = new FileStorageOptions
        {
            StoragePath = "wwwroot/uploads",
            MaxFileSizeBytes = -1,
            AllowedExtensions = new[] { ".jpg" }
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("FileStorage:MaxFileSizeBytes must be a positive integer greater than zero");
    }

    #endregion

    #region AllowedExtensions Validation

    [Fact]
    public void Validate_AllowedExtensionsIsNull_ReturnsFailure()
    {
        // Arrange
        var options = new FileStorageOptions
        {
            StoragePath = "wwwroot/uploads",
            MaxFileSizeBytes = 10485760,
            AllowedExtensions = null!
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("FileStorage:AllowedExtensions is required and must contain at least one valid file extension");
    }

    [Fact]
    public void Validate_AllowedExtensionsIsEmpty_ReturnsFailure()
    {
        // Arrange
        var options = new FileStorageOptions
        {
            StoragePath = "wwwroot/uploads",
            MaxFileSizeBytes = 10485760,
            AllowedExtensions = Array.Empty<string>()
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("FileStorage:AllowedExtensions is required and must contain at least one valid file extension");
    }

    [Fact]
    public void Validate_AllowedExtensionsContainsInvalidEntry_ReturnsFailure()
    {
        // Arrange
        var options = new FileStorageOptions
        {
            StoragePath = "wwwroot/uploads",
            MaxFileSizeBytes = 10485760,
            AllowedExtensions = new[] { ".jpg", "txt" } // Missing period on "txt"
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("FileStorage:AllowedExtensions contains invalid entries");
        result.FailureMessage.Should().Contain("must start with '.'");
        result.FailureMessage.Should().Contain("txt");
    }

    [Fact]
    public void Validate_AllowedExtensionsContainsEmptyString_ReturnsFailure()
    {
        // Arrange
        var options = new FileStorageOptions
        {
            StoragePath = "wwwroot/uploads",
            MaxFileSizeBytes = 10485760,
            AllowedExtensions = new[] { ".jpg", "" }
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("FileStorage:AllowedExtensions contains invalid entries");
    }

    [Fact]
    public void Validate_AllowedExtensionsContainsWhitespace_ReturnsFailure()
    {
        // Arrange
        var options = new FileStorageOptions
        {
            StoragePath = "wwwroot/uploads",
            MaxFileSizeBytes = 10485760,
            AllowedExtensions = new[] { ".jpg", "   " }
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("FileStorage:AllowedExtensions contains invalid entries");
    }

    #endregion

    #region Security: Dangerous Extensions

    [Fact]
    public void Validate_AllowedExtensionsContainsDangerousExtension_Exe_ReturnsFailure()
    {
        // Arrange
        var options = new FileStorageOptions
        {
            StoragePath = "wwwroot/uploads",
            MaxFileSizeBytes = 10485760,
            AllowedExtensions = new[] { ".jpg", ".exe" }
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("DANGEROUS extensions that must NEVER be allowed");
        result.FailureMessage.Should().Contain(".exe");
        result.FailureMessage.Should().Contain("prevent malware uploads");
    }

    [Fact]
    public void Validate_AllowedExtensionsContainsDangerousExtension_Bat_ReturnsFailure()
    {
        // Arrange
        var options = new FileStorageOptions
        {
            StoragePath = "wwwroot/uploads",
            MaxFileSizeBytes = 10485760,
            AllowedExtensions = new[] { ".jpg", ".bat" }
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("DANGEROUS extensions that must NEVER be allowed");
        result.FailureMessage.Should().Contain(".bat");
    }

    [Fact]
    public void Validate_AllowedExtensionsContainsDangerousExtension_Sh_ReturnsFailure()
    {
        // Arrange
        var options = new FileStorageOptions
        {
            StoragePath = "wwwroot/uploads",
            MaxFileSizeBytes = 10485760,
            AllowedExtensions = new[] { ".jpg", ".sh" }
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("DANGEROUS extensions that must NEVER be allowed");
        result.FailureMessage.Should().Contain(".sh");
    }

    [Fact]
    public void Validate_AllowedExtensionsContainsDangerousExtension_Html_ReturnsFailure()
    {
        // Arrange
        var options = new FileStorageOptions
        {
            StoragePath = "wwwroot/uploads",
            MaxFileSizeBytes = 10485760,
            AllowedExtensions = new[] { ".jpg", ".html" }
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("DANGEROUS extensions that must NEVER be allowed");
        result.FailureMessage.Should().Contain(".html");
    }

    [Fact]
    public void Validate_AllowedExtensionsContainsDangerousExtension_Php_ReturnsFailure()
    {
        // Arrange
        var options = new FileStorageOptions
        {
            StoragePath = "wwwroot/uploads",
            MaxFileSizeBytes = 10485760,
            AllowedExtensions = new[] { ".jpg", ".php" }
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("DANGEROUS extensions that must NEVER be allowed");
        result.FailureMessage.Should().Contain(".php");
    }

    #endregion

    #region Multiple Failures

    [Fact]
    public void Validate_MultipleFailures_ReturnsAllErrorMessages()
    {
        // Arrange
        var options = new FileStorageOptions
        {
            StoragePath = "", // Invalid: empty
            MaxFileSizeBytes = 0, // Invalid: zero
            AllowedExtensions = Array.Empty<string>() // Invalid: empty
        };

        // Act
        var result = _validator.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("FileStorage:StoragePath is required and cannot be null or empty");
        result.FailureMessage.Should().Contain("FileStorage:MaxFileSizeBytes must be a positive integer greater than zero");
        result.FailureMessage.Should().Contain("FileStorage:AllowedExtensions is required and must contain at least one valid file extension");
    }

    #endregion
}
