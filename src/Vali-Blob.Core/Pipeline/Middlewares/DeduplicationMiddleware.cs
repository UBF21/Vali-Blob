using System.Security.Cryptography;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;

namespace ValiBlob.Core.Pipeline.Middlewares;

public sealed class DeduplicationMiddleware : IStorageMiddleware
{
    private readonly IStorageProvider _provider;
    private readonly DeduplicationOptions _options;

    public DeduplicationMiddleware(IStorageProvider provider, DeduplicationOptions options)
    {
        _provider = provider;
        _options = options;
    }

    public async Task InvokeAsync(StoragePipelineContext context, StorageMiddlewareDelegate next)
    {
        if (!_options.Enabled)
        {
            await next(context);
            return;
        }

        // Compute hash and rewind stream
        var hash = await ComputeSha256Async(context.Request.Content, context.CancellationToken).ConfigureAwait(false);
        if (context.Request.Content.CanSeek)
            context.Request.Content.Seek(0, SeekOrigin.Begin);

        context.Items[PipelineContextKeys.DeduplicationHash] = hash;

        // Stamp the hash in metadata for future lookups
        var metadata = new Dictionary<string, string>(
            context.Request.Metadata ?? new Dictionary<string, string>())
        {
            [_options.MetadataHashKey] = hash
        };
        context.Request = context.Request.WithMetadata(metadata);

        if (_options.CheckBeforeUpload)
        {
            var existingPath = await FindByHashAsync(hash, context.CancellationToken).ConfigureAwait(false);
            if (existingPath is not null)
            {
                context.Items[PipelineContextKeys.DeduplicationHash] = hash;
                context.Items[PipelineContextKeys.DeduplicationIsDuplicate] = true;
                context.IsCancelled = true;
                context.CancellationReason = $"Duplicate file detected. Existing path: {existingPath}";
                return;
            }
        }

        await next(context);
    }

    private static async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken = default)
    {
        using var sha256 = SHA256.Create();
        byte[] hashBytes;

        if (stream.CanSeek)
        {
            var position = stream.Position;
            hashBytes = await ComputeHashAsync(sha256, stream, cancellationToken).ConfigureAwait(false);
            stream.Seek(position, SeekOrigin.Begin);
        }
        else
        {
            hashBytes = await ComputeHashAsync(sha256, stream, cancellationToken).ConfigureAwait(false);
        }

        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }

    private static async Task<byte[]> ComputeHashAsync(HashAlgorithm algorithm, Stream stream, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
        {
            algorithm.TransformBlock(buffer, 0, read, null, 0);
        }
        algorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return algorithm.Hash!;
    }

    private async Task<string?> FindByHashAsync(string hash, CancellationToken ct)
    {
        var listResult = await _provider.ListFilesAsync(prefix: null, options: null, cancellationToken: ct)
            .ConfigureAwait(false);

        if (!listResult.IsSuccess || listResult.Value is null)
            return null;

        // We can't read metadata of all files efficiently without N+1 calls.
        // As a best-effort prefix scan, we look through the listed files' metadata.
        foreach (var entry in listResult.Value)
        {
            var metaResult = await _provider.GetMetadataAsync(entry.Path, ct).ConfigureAwait(false);
            if (!metaResult.IsSuccess || metaResult.Value is null)
                continue;

            if (metaResult.Value.CustomMetadata is not null &&
                metaResult.Value.CustomMetadata.TryGetValue(_options.MetadataHashKey, out var storedHash) &&
                string.Equals(storedHash, hash, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Path;
            }
        }

        return null;
    }
}
