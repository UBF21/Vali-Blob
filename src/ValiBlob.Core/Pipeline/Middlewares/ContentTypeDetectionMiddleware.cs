using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Options;

namespace ValiBlob.Core.Pipeline.Middlewares;

public sealed class ContentTypeDetectionMiddleware : IStorageMiddleware
{
    private readonly ContentTypeDetectionOptions _options;

    public ContentTypeDetectionMiddleware(ContentTypeDetectionOptions options)
    {
        _options = options;
    }

    public async Task InvokeAsync(StoragePipelineContext context, StorageMiddlewareDelegate next)
    {
        if (!_options.Enabled)
        {
            await next(context);
            return;
        }

        // If OverrideExisting is false and ContentType is already set, skip detection
        if (!_options.OverrideExisting && context.Request.ContentType is not null)
        {
            await next(context);
            return;
        }

        var stream = context.Request.Content;
        var buffer = new byte[16];

        if (stream.CanSeek)
        {
            var read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            stream.Seek(0, SeekOrigin.Begin);

            var detected = DetectMimeType(buffer, read);
            if (detected is not null)
                context.Request = context.Request.WithContentType(detected);
        }
        else
        {
            // Non-seekable: read leading bytes, then wrap in LeadingBytesStream
            var read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            var wrappedStream = new LeadingBytesStream(stream, buffer, read);

            var detected = DetectMimeType(buffer, read);
            if (detected is not null)
                context.Request = context.Request.WithContentType(detected).WithContent(wrappedStream);
            else
                context.Request = context.Request.WithContent(wrappedStream);
        }

        await next(context);
    }

    private static string? DetectMimeType(byte[] bytes, int length)
    {
        if (length < 2) return null;

        // JPEG: FF D8 FF
        if (length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";

        // PNG: 89 50 4E 47
        if (length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";

        // GIF: 47 49 46
        if (length >= 3 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            return "image/gif";

        // PDF: 25 50 44 46
        if (length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46)
            return "application/pdf";

        // ZIP / DOCX / XLSX: 50 4B 03 04
        if (length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04)
            return "application/zip";

        // RAR: 52 61 72 21
        if (length >= 4 && bytes[0] == 0x52 && bytes[1] == 0x61 && bytes[2] == 0x72 && bytes[3] == 0x21)
            return "application/x-rar";

        // GZip: 1F 8B
        if (length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
            return "application/gzip";

        // BMP: 42 4D
        if (length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
            return "image/bmp";

        // TIFF: 49 49 2A 00 (little-endian) or 4D 4D 00 2A (big-endian)
        if (length >= 4 &&
            ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00) ||
             (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A)))
            return "image/tiff";

        // MP4: "ftyp" at offset 4 (bytes 4-7 = 66 74 79 70)
        if (length >= 8 && bytes[4] == 0x66 && bytes[5] == 0x74 && bytes[6] == 0x79 && bytes[7] == 0x70)
            return "video/mp4";

        // MP3: ID3 tag (49 44 33) or MPEG sync (FF FB or FF F3)
        if (length >= 3 && bytes[0] == 0x49 && bytes[1] == 0x44 && bytes[2] == 0x33)
            return "audio/mpeg";
        if (length >= 2 && bytes[0] == 0xFF && (bytes[1] == 0xFB || bytes[1] == 0xF3))
            return "audio/mpeg";

        return null;
    }
}
