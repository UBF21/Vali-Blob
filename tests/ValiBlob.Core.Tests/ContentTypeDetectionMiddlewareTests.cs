using FluentAssertions;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;
using ValiBlob.Core.Pipeline.Middlewares;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class ContentTypeDetectionMiddlewareTests
{
    private static StoragePipelineContext MakeContext(byte[] content, string? contentType = null)
    {
        var request = new UploadRequest
        {
            Path = StoragePath.From("uploads/test.bin"),
            Content = new MemoryStream(content),
            ContentType = contentType,
            ContentLength = content.Length
        };
        return new StoragePipelineContext(request);
    }

    private static StorageMiddlewareDelegate NoOpNext => _ => Task.CompletedTask;

    private static ContentTypeDetectionMiddleware MakeMiddleware(
        bool enabled = true, bool overrideExisting = false)
        => new(new ContentTypeDetectionOptions { Enabled = enabled, OverrideExisting = overrideExisting });

    // 1. JPEG magic bytes → image/jpeg
    [Fact]
    public async Task JpegMagicBytes_SetsContentTypeToImageJpeg()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
        var ctx = MakeContext(bytes);
        var mw = MakeMiddleware();

        await mw.InvokeAsync(ctx, NoOpNext);

        ctx.Request.ContentType.Should().Be("image/jpeg");
    }

    // 2. PNG magic bytes → image/png
    [Fact]
    public async Task PngMagicBytes_SetsContentTypeToImagePng()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var ctx = MakeContext(bytes);
        var mw = MakeMiddleware();

        await mw.InvokeAsync(ctx, NoOpNext);

        ctx.Request.ContentType.Should().Be("image/png");
    }

    // 3. PDF magic bytes → application/pdf
    [Fact]
    public async Task PdfMagicBytes_SetsContentTypeToApplicationPdf()
    {
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 };
        var ctx = MakeContext(bytes);
        var mw = MakeMiddleware();

        await mw.InvokeAsync(ctx, NoOpNext);

        ctx.Request.ContentType.Should().Be("application/pdf");
    }

    // 4. GIF magic bytes → image/gif
    [Fact]
    public async Task GifMagicBytes_SetsContentTypeToImageGif()
    {
        var bytes = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00 };
        var ctx = MakeContext(bytes);
        var mw = MakeMiddleware();

        await mw.InvokeAsync(ctx, NoOpNext);

        ctx.Request.ContentType.Should().Be("image/gif");
    }

    // 5. ZIP magic bytes → application/zip
    [Fact]
    public async Task ZipMagicBytes_SetsContentTypeToApplicationZip()
    {
        var bytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00 };
        var ctx = MakeContext(bytes);
        var mw = MakeMiddleware();

        await mw.InvokeAsync(ctx, NoOpNext);

        ctx.Request.ContentType.Should().Be("application/zip");
    }

    // 6. Unknown bytes → does NOT change content type
    [Fact]
    public async Task UnknownBytes_DoesNotSetContentType()
    {
        var bytes = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        var ctx = MakeContext(bytes, contentType: null);
        var mw = MakeMiddleware();

        await mw.InvokeAsync(ctx, NoOpNext);

        ctx.Request.ContentType.Should().BeNull();
    }

    // 7. OverrideExisting = false and content type already set → does NOT override
    [Fact]
    public async Task OverrideExistingFalse_ContentTypeAlreadySet_DoesNotOverride()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
        var ctx = MakeContext(bytes, contentType: "application/octet-stream");
        var mw = MakeMiddleware(overrideExisting: false);

        await mw.InvokeAsync(ctx, NoOpNext);

        ctx.Request.ContentType.Should().Be("application/octet-stream");
    }

    // 8. OverrideExisting = true and content type already set → DOES override
    [Fact]
    public async Task OverrideExistingTrue_ContentTypeAlreadySet_OverridesWithDetected()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
        var ctx = MakeContext(bytes, contentType: "application/octet-stream");
        var mw = MakeMiddleware(overrideExisting: true);

        await mw.InvokeAsync(ctx, NoOpNext);

        ctx.Request.ContentType.Should().Be("image/jpeg");
    }

    // 9. Stream is rewound after detection (position == 0)
    [Fact]
    public async Task SeekableStream_IsRewoundAfterDetection()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var ctx = MakeContext(bytes);
        var mw = MakeMiddleware();

        await mw.InvokeAsync(ctx, NoOpNext);

        ctx.Request.Content.Position.Should().Be(0);
    }

    // 10. Non-seekable stream → detection still works via LeadingBytesStream
    [Fact]
    public async Task NonSeekableStream_DetectionStillWorks()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var inner = new MemoryStream(bytes);
        var nonSeekable = new NonSeekableStreamWrapper(inner);

        var request = new UploadRequest
        {
            Path = StoragePath.From("uploads/test.png"),
            Content = nonSeekable,
            ContentLength = bytes.Length
        };
        var ctx = new StoragePipelineContext(request);
        var mw = MakeMiddleware();

        await mw.InvokeAsync(ctx, NoOpNext);

        ctx.Request.ContentType.Should().Be("image/png");
    }

    // 11. Non-seekable stream → all bytes still available after wrapping
    [Fact]
    public async Task NonSeekableStream_AllBytesStillReadableAfterMiddleware()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03 };
        var inner = new MemoryStream(bytes);
        var nonSeekable = new NonSeekableStreamWrapper(inner);

        var request = new UploadRequest
        {
            Path = StoragePath.From("uploads/test.png"),
            Content = nonSeekable,
            ContentLength = bytes.Length
        };
        var ctx = new StoragePipelineContext(request);
        var mw = MakeMiddleware();

        byte[]? capturedBytes = null;
        await mw.InvokeAsync(ctx, c =>
        {
            using var ms = new MemoryStream();
            c.Request.Content.CopyTo(ms);
            capturedBytes = ms.ToArray();
            return Task.CompletedTask;
        });

        capturedBytes.Should().NotBeNull();
        capturedBytes.Should().BeEquivalentTo(bytes);
    }

    // 12. Empty stream → does not throw, does not set content type
    [Fact]
    public async Task EmptyStream_DoesNotThrowAndDoesNotSetContentType()
    {
        var ctx = MakeContext(Array.Empty<byte>());
        var mw = MakeMiddleware();

        var act = async () => await mw.InvokeAsync(ctx, NoOpNext);

        await act.Should().NotThrowAsync();
        ctx.Request.ContentType.Should().BeNull();
    }

    // 13. When Enabled = false → skips detection entirely
    [Fact]
    public async Task Disabled_SkipsDetectionEntirely()
    {
        // JPEG bytes — would normally be detected as image/jpeg
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
        var ctx = MakeContext(bytes, contentType: null);
        var mw = MakeMiddleware(enabled: false);

        await mw.InvokeAsync(ctx, NoOpNext);

        ctx.Request.ContentType.Should().BeNull();
    }

    // Helper: wraps a MemoryStream but reports CanSeek = false
    private sealed class NonSeekableStreamWrapper : Stream
    {
        private readonly MemoryStream _inner;
        public NonSeekableStreamWrapper(MemoryStream inner) => _inner = inner;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _inner.ReadAsync(buffer, offset, count, cancellationToken);
    }
}
