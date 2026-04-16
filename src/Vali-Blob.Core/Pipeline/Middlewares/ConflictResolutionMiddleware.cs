using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Exceptions;
using ValiBlob.Core.Models;

namespace ValiBlob.Core.Pipeline.Middlewares;

public sealed class ConflictResolutionMiddleware : IStorageMiddleware
{
    private readonly IStorageProvider _provider;

    public ConflictResolutionMiddleware(IStorageProvider provider) => _provider = provider;

    public async Task InvokeAsync(StoragePipelineContext context, StorageMiddlewareDelegate next)
    {
        if (context.Request.ConflictResolution == ConflictResolution.Overwrite)
        {
            await next(context);
            return;
        }

        var existsResult = await _provider.ExistsAsync(context.Request.Path).ConfigureAwait(false);
        if (!existsResult.IsSuccess || !existsResult.Value)
        {
            await next(context);
            return;
        }

        // File exists — apply the resolution strategy
        if (context.Request.ConflictResolution == ConflictResolution.Fail)
        {
            context.IsCancelled = true;
            throw new StorageValidationException(new[]
            {
                $"File already exists at path '{context.Request.Path}' and ConflictResolution is set to Fail."
            });
        }

        if (context.Request.ConflictResolution == ConflictResolution.Rename)
        {
            var newPath = await FindAvailablePathAsync(context.Request.Path, context.CancellationToken)
                .ConfigureAwait(false);
            context.Request = context.Request.WithPath(newPath);
        }

        await next(context);
    }

    private async Task<StoragePath> FindAvailablePathAsync(StoragePath original, CancellationToken ct)
    {
        var originalStr = original.ToString();
        var dir = Path.GetDirectoryName(originalStr)?.Replace('\\', '/') ?? "";
        var name = Path.GetFileNameWithoutExtension(originalStr);
        var ext = Path.GetExtension(originalStr);

        for (var i = 1; i <= 1000; i++)
        {
            var candidate = string.IsNullOrEmpty(dir)
                ? $"{name}_{i}{ext}"
                : $"{dir}/{name}_{i}{ext}";

            var exists = await _provider.ExistsAsync(candidate, ct).ConfigureAwait(false);
            if (!exists.IsSuccess || !exists.Value)
                return StoragePath.From(candidate);
        }

        // Fallback: append a GUID to guarantee uniqueness
        var guidSuffix = $"{name}_{Guid.NewGuid():N}{ext}";
        var fallback = string.IsNullOrEmpty(dir) ? guidSuffix : $"{dir}/{guidSuffix}";
        return StoragePath.From(fallback);
    }
}
