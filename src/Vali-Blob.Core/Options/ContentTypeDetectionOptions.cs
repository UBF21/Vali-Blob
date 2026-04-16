namespace ValiBlob.Core.Options;

public class ContentTypeDetectionOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// If true, overrides the caller-provided ContentType.
    /// If false, only sets it when ContentType is null.
    /// </summary>
    public bool OverrideExisting { get; set; } = false;
}
