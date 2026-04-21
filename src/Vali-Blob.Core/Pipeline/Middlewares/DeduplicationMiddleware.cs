using System.Security.Cryptography;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;

namespace ValiBlob.Core.Pipeline.Middlewares;

public sealed class DeduplicationMiddleware : IStorageMiddleware
{
    private readonly IStorageProvider _provider;
    private readonly DeduplicationOptions _options;
    private readonly IDeduplicationHashIndex _hashIndex;

    public DeduplicationMiddleware(
        IStorageProvider provider,
        DeduplicationOptions options,
        IDeduplicationHashIndex hashIndex)
    {
        _provider = provider;
        _options = options;
        _hashIndex = hashIndex;
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

        // Stamp the hash in metadata for future lookups (and for external providers)
        var metadata = new Dictionary<string, string>(
            context.Request.Metadata ?? new Dictionary<string, string>())
        {
            [_options.MetadataHashKey] = hash
        };
        context.Request = context.Request.WithMetadata(metadata);

        if (_options.CheckBeforeUpload)
        {
            // O(1) index lookup — replaces the previous O(n) GetMetadataAsync loop
            var existingPath = await _hashIndex.FindPathByHashAsync(hash, context.CancellationToken).ConfigureAwait(false);
            if (existingPath is not null)
            {
                context.Items[PipelineContextKeys.DeduplicationHash] = hash;
                context.Items[PipelineContextKeys.DeduplicationIsDuplicate] = true;
                context.IsCancelled = true;
                context.CancellationReason = "Duplicate file detected.";
                return;
            }
        }

        await next(context);

        // After a successful upload, index the hash so future uploads hit the O(1) path.
        // The request path is the canonical path; conflict-resolution renames are stored in Items if applicable.
        if (!context.IsCancelled)
        {
            var uploadedPath = context.Items.TryGetValue(PipelineContextKeys.ConflictResolutionPath, out var resolved)
                ? resolved?.ToString() ?? context.Request.Path.ToString()
                : context.Request.Path.ToString();

            await _hashIndex.IndexAsync(hash, uploadedPath, context.CancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken = default)
    {
        byte[] hashBytes;

        if (stream.CanSeek)
        {
            var position = stream.Position;
            hashBytes = await ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
            stream.Seek(position, SeekOrigin.Begin);
        }
        else
        {
            hashBytes = await ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }

    private static async Task<byte[]> ComputeHashAsync(Stream stream, CancellationToken cancellationToken = default)
    {
#if NET5_0_OR_GREATER
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, 81920, cancellationToken).ConfigureAwait(false);
        return SHA256.HashData(ms.ToArray());
#else
        using var sha256 = SHA256.Create();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
        {
            sha256.TransformBlock(buffer, 0, read, null, 0);
        }
        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return sha256.Hash!;
#endif
    }
}
