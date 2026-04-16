namespace ValiBlob.Core.Resumable;

/// <summary>Helper for reading chunks from a stream, handling both sized and unsized reads.</summary>
public static class StreamReadHelper
{
    /// <summary>
    /// Reads a chunk from a stream. If length is specified, reads exactly that many bytes.
    /// If length is null, copies entire stream to memory.
    /// </summary>
    public static async Task<byte[]> ReadChunkAsync(
        Stream data,
        long? length,
        CancellationToken cancellationToken = default)
    {
        if (length.HasValue)
        {
            var chunkBytes = new byte[length.Value];
            var read = 0;
            while (read < chunkBytes.Length)
            {
                var n = await data.ReadAsync(chunkBytes, read, chunkBytes.Length - read, cancellationToken)
                    .ConfigureAwait(false);
                if (n == 0) break;
                read += n;
            }
            if (read < chunkBytes.Length)
                Array.Resize(ref chunkBytes, read);
            return chunkBytes;
        }
        else
        {
            using var ms = new MemoryStream();
            await data.CopyToAsync(ms, 81920, cancellationToken).ConfigureAwait(false);
            return ms.ToArray();
        }
    }
}
