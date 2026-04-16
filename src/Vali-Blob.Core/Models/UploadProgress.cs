namespace ValiBlob.Core.Models;

public sealed class UploadProgress
{
    public UploadProgress(long bytesTransferred, long? totalBytes)
    {
        BytesTransferred = bytesTransferred;
        TotalBytes = totalBytes;
    }

    public long BytesTransferred { get; }
    public long? TotalBytes { get; }
    public double? Percentage => TotalBytes.HasValue && TotalBytes > 0
        ? Math.Round((double)BytesTransferred / TotalBytes.Value * 100, 2)
        : null;

    public override string ToString() =>
        Percentage.HasValue
            ? $"{BytesTransferred:N0} / {TotalBytes:N0} bytes ({Percentage:F1}%)"
            : $"{BytesTransferred:N0} bytes transferred";
}
