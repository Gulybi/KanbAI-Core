using Microsoft.Extensions.Options;

namespace KanbAI_Core.Models.Configuration;

/// <summary>
/// Validates <see cref="FileStorageOptions"/> at application startup.
/// If validation fails, the application refuses to start with a clear error message (fail-fast behavior).
/// </summary>
public sealed class FileStorageOptionsValidator : IValidateOptions<FileStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, FileStorageOptions options)
    {
        var failures = new List<string>();

        // Validate StoragePath
        if (string.IsNullOrWhiteSpace(options.StoragePath))
        {
            failures.Add("FileStorage:StoragePath is required and cannot be null or empty.");
        }
        else if (options.StoragePath.EndsWith('/') || options.StoragePath.EndsWith('\\'))
        {
            failures.Add("FileStorage:StoragePath must NOT include a trailing slash.");
        }

        // Validate MaxFileSizeBytes
        if (options.MaxFileSizeBytes <= 0)
        {
            failures.Add("FileStorage:MaxFileSizeBytes must be a positive integer greater than zero.");
        }

        // Validate AllowedExtensions
        if (options.AllowedExtensions == null || options.AllowedExtensions.Length == 0)
        {
            failures.Add("FileStorage:AllowedExtensions is required and must contain at least one valid file extension.");
        }
        else
        {
            // Validate each extension starts with a period
            var invalidExtensions = options.AllowedExtensions
                .Where(ext => string.IsNullOrWhiteSpace(ext) || !ext.StartsWith('.'))
                .ToList();

            if (invalidExtensions.Any())
            {
                failures.Add($"FileStorage:AllowedExtensions contains invalid entries (must start with '.' and not be empty): {string.Join(", ", invalidExtensions)}");
            }

            // Security check: disallow dangerous extensions
            var dangerousExtensions = new[]
            {
                ".exe", ".bat", ".cmd", ".sh", ".ps1", ".dll", ".so", ".dylib", ".com", ".msi", ".app",
                ".vbs", ".wsf", ".hta", ".jar",
                ".html", ".htm", ".asp", ".aspx", ".php"
            };

            var foundDangerousExtensions = options.AllowedExtensions
                .Where(ext => dangerousExtensions.Contains(ext.ToLowerInvariant()))
                .ToList();

            if (foundDangerousExtensions.Any())
            {
                failures.Add($"FileStorage:AllowedExtensions contains DANGEROUS extensions that must NEVER be allowed: {string.Join(", ", foundDangerousExtensions)}. Remove these to prevent malware uploads.");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
