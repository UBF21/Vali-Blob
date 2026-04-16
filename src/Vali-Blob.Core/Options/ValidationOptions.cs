namespace ValiBlob.Core.Options;

public sealed class ValidationOptions
{
    public long MaxFileSizeBytes { get; set; } = 500L * 1024 * 1024; // 500MB
    public IList<string> AllowedExtensions { get; set; } = new List<string>();
    public IList<string> BlockedExtensions { get; set; } = new List<string> { ".exe", ".bat", ".cmd", ".sh" };
    public IList<string> AllowedContentTypes { get; set; } = new List<string>();
}
