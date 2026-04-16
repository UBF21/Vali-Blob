using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Exceptions;

namespace ValiBlob.Core.Pipeline.Middlewares;

public sealed class VirusScanMiddleware : IStorageMiddleware
{
    private readonly IVirusScanner _scanner;

    public VirusScanMiddleware(IVirusScanner scanner) => _scanner = scanner;

    public async Task InvokeAsync(StoragePipelineContext context, StorageMiddlewareDelegate next)
    {
        var result = await _scanner.ScanAsync(
            context.Request.Content,
            Path.GetFileName(context.Request.Path),
            context.CancellationToken).ConfigureAwait(false);

        if (!result.IsClean)
        {
            context.IsCancelled = true;
            context.CancellationReason = $"File rejected by virus scanner '{result.ScannerName}': {result.ThreatName}";
            throw new StorageValidationException(new[] { context.CancellationReason });
        }

        // Rewind stream if seekable so subsequent middleware/provider can read from the beginning
        if (context.Request.Content.CanSeek)
            context.Request.Content.Seek(0, SeekOrigin.Begin);

        await next(context);
    }
}
