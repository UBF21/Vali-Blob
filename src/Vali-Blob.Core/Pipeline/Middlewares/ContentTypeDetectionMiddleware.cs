using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Options;

namespace ValiBlob.Core.Pipeline.Middlewares;

public sealed class ContentTypeDetectionMiddleware : IStorageMiddleware
{
    private static readonly (byte[] Signature, string ContentType)[] MagicByteTable =
    [
        // JPEG: FF D8 FF
        (new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg"),

        // PNG: 89 50 4E 47
        (new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png"),

        // GIF: 47 49 46
        (new byte[] { 0x47, 0x49, 0x46 }, "image/gif"),

        // PDF: 25 50 44 46
        (new byte[] { 0x25, 0x50, 0x44, 0x46 }, "application/pdf"),

        // ZIP / DOCX / XLSX: 50 4B 03 04
        (new byte[] { 0x50, 0x4B, 0x03, 0x04 }, "application/zip"),

        // RAR: 52 61 72 21
        (new byte[] { 0x52, 0x61, 0x72, 0x21 }, "application/x-rar"),

        // GZip: 1F 8B
        (new byte[] { 0x1F, 0x8B }, "application/gzip"),

        // BMP: 42 4D
        (new byte[] { 0x42, 0x4D }, "image/bmp"),

        // TIFF little-endian: 49 49 2A 00
        (new byte[] { 0x49, 0x49, 0x2A, 0x00 }, "image/tiff"),

        // TIFF big-endian: 4D 4D 00 2A
        (new byte[] { 0x4D, 0x4D, 0x00, 0x2A }, "image/tiff"),

        // MP3 ID3 tag: 49 44 33
        (new byte[] { 0x49, 0x44, 0x33 }, "audio/mpeg"),
    ];

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

        // Check standard magic byte signatures
        foreach (var (signature, contentType) in MagicByteTable)
        {
            if (length >= signature.Length && CheckSignatureMatch(bytes, signature))
                return contentType;
        }

        // MP4: "ftyp" at offset 4 (bytes 4-7 = 66 74 79 70)
        if (length >= 8 && bytes[4] == 0x66 && bytes[5] == 0x74 && bytes[6] == 0x79 && bytes[7] == 0x70)
            return "video/mp4";

        // MP3 MPEG sync: FF FB or FF F3
        if (length >= 2 && bytes[0] == 0xFF && (bytes[1] == 0xFB || bytes[1] == 0xF3))
            return "audio/mpeg";

        return null;
    }

    private static bool CheckSignatureMatch(byte[] buffer, byte[] signature)
    {
        for (int i = 0; i < signature.Length; i++)
        {
            if (buffer[i] != signature[i])
                return false;
        }
        return true;
    }
}
