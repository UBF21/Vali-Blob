namespace ValiBlob.Core.Options;

public sealed class StorageGlobalOptions
{
    public const string SectionName = "ValiBlob";

    public string DefaultProvider { get; set; } = string.Empty;
    public bool EnableTelemetry { get; set; } = true;
    public bool EnableLogging { get; set; } = true;
}
