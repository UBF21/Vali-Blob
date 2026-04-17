namespace ValiBlob.Core.Options;

public sealed class StorageGlobalOptions
{
    public const string SectionName = "ValiBlob";

    /// <summary>
    /// The default storage provider name or type to use when no explicit provider is specified.
    /// Supports either:
    /// - Built-in provider names: "Local", "InMemory", "AWS", "Azure", "GCP", "OCI", "Supabase"
    /// - Custom provider key for third-party implementations
    /// Default: empty string (caller must specify provider per operation).
    /// </summary>
    public string DefaultProvider { get; set; } = string.Empty;

    public bool EnableTelemetry { get; set; } = true;
    public bool EnableLogging { get; set; } = true;

    /// <summary>
    /// When true, wraps all resolved providers with StorageTelemetryDecorator for OpenTelemetry instrumentation.
    /// </summary>
    public bool ApplyTelemetryDecorator { get; set; } = false;

    /// <summary>
    /// When true, wraps all resolved providers with StorageEventDecorator for event dispatching.
    /// </summary>
    public bool ApplyEventDecorator { get; set; } = false;

    /// <summary>
    /// Attempts to convert the DefaultProvider string to a StorageProviderType enum.
    /// Returns StorageProviderType.None if not found (custom provider or empty).
    /// </summary>
    public StorageProviderType GetDefaultProviderType() =>
        Enum.TryParse<StorageProviderType>(DefaultProvider, ignoreCase: true, out var providerType)
            ? providerType
            : StorageProviderType.Custom;
}
