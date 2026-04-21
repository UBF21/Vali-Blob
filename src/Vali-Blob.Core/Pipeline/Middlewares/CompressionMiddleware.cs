using System.IO.Compression;
using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Options;

namespace ValiBlob.Core.Pipeline.Middlewares;

public sealed class CompressionMiddleware : IStorageMiddleware
{
    private readonly CompressionOptions _options;

    public CompressionMiddleware(IOptions<CompressionOptions> options)
    {
        _options = options.Value;
    }

    public async Task InvokeAsync(StoragePipelineContext context, StorageMiddlewareDelegate next)
    {
        if (_options.Enabled && ShouldCompress(context.Request.ContentType, context.Request.ContentLength))
        {
            var compressedStream = new MemoryStream();
            // GZipStream does not implement IAsyncDisposable in netstandard2.0 — use sync using
            using (var gzip = new GZipStream(compressedStream, CompressionLevel.Fastest, leaveOpen: true))
            {
                await context.Request.Content.CopyToAsync(gzip);
            }
            compressedStream.Position = 0;

            var metadata = new Dictionary<string, string>(
                context.Request.Metadata ?? new Dictionary<string, string>())
            {
                ["x-vali-compressed"] = "gzip",
                ["x-vali-original-size"] = (context.Request.ContentLength ?? 0).ToString()
            };

            context.Request = context.Request.WithContent(compressedStream).WithMetadata(metadata);
        }

        await next(context);
    }

    private bool ShouldCompress(string? contentType, long? contentLength)
    {
        if (contentLength.HasValue && contentLength.Value < _options.MinSizeBytes)
            return false;

        if (contentType is null)
            return false;

        return _options.CompressibleContentTypes.Any(ct =>
            contentType.StartsWith(ct, StringComparison.OrdinalIgnoreCase));
    }
}
