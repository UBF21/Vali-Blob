using ValiBlob.Core.Models;

namespace ValiBlob.Core.Pipeline;

public sealed class StoragePipelineContext
{
    public StoragePipelineContext(UploadRequest request)
    {
        Request = request;
    }

    public UploadRequest Request { get; set; }
    public IDictionary<string, object> Items { get; } = new Dictionary<string, object>();
    public bool IsCancelled { get; set; }
    public string? CancellationReason { get; set; }
}
