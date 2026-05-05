namespace KanbAI_Core.Models.Configuration;

/// <summary>
/// Configuration options for local file storage.
/// Binds to the "FileStorage" section in appsettings.json.
/// </summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>
    /// Relative path from the application root to the storage directory (e.g., "wwwroot/uploads").
    /// Must NOT include a trailing slash.
    /// </summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>
    /// Maximum file size in bytes. Files exceeding this limit are rejected before being written to disk.
    /// Production default: 10 MB (10485760 bytes).
    /// </summary>
    public long MaxFileSizeBytes { get; set; }

    /// <summary>
    /// Whitelist of allowed file extensions (lowercase, including leading period, e.g., ".jpg", ".pdf").
    /// Extensions are compared case-insensitively.
    /// Executable extensions (.exe, .bat, .sh, etc.) must NEVER be included.
    /// </summary>
    public string[] AllowedExtensions { get; set; } = Array.Empty<string>();
}
