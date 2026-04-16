using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Models;
using ValiBlob.Core.Pipeline;
using ValiBlob.ImageSharp;
using ValiBlob.Testing;
using ValiBlob.Testing.Extensions;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class ImageProcessingTests
{
    // ─── Helper: create synthetic images ─────────────────────────────────────

    private static MemoryStream CreateTestImage(int width, int height, string format = "jpeg")
    {
        // A blank (all-black) image is a perfectly valid image for test purposes —
        // we only care about dimensions and encoded format, not pixel values.
        using var image = new Image<Rgba32>(width, height);
        var ms = new MemoryStream();
        if (format == "png")
            image.SaveAsPng(ms);
        else if (format == "webp")
            image.SaveAsWebp(ms);
        else
            image.SaveAsJpeg(ms, new JpegEncoder { Quality = 85 });
        ms.Seek(0, SeekOrigin.Begin);
        return ms;
    }

    private static InMemoryStorageProvider BuildInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddValiBlob().UseInMemory();
        return services.BuildServiceProvider().GetRequiredService<InMemoryStorageProvider>();
    }

    private static StoragePipelineContext MakeContext(Stream content, string contentType)
        => new(new UploadRequest
        {
            Path = StoragePath.From("test", "image.jpg"),
            Content = content,
            ContentType = contentType,
            ContentLength = content.Length
        });

    // ─── Test 1: ImageProcessingOptions defaults ──────────────────────────────

    [Fact]
    public void ImageProcessingOptions_Defaults_AreCorrect()
    {
        var opts = new ImageProcessingOptions();
        opts.Enabled.Should().BeTrue();
        opts.JpegQuality.Should().Be(85);
        opts.MaxWidth.Should().BeNull();
        opts.MaxHeight.Should().BeNull();
        opts.OutputFormat.Should().BeNull();
        opts.Thumbnail.Should().BeNull();
    }

    // ─── Test 2: ProcessableContentTypes contains jpeg, png, webp ────────────

    [Fact]
    public void ImageProcessingOptions_ProcessableContentTypes_ContainsCommonImageTypes()
    {
        var opts = new ImageProcessingOptions();
        opts.ProcessableContentTypes.Should().Contain("image/jpeg");
        opts.ProcessableContentTypes.Should().Contain("image/png");
        opts.ProcessableContentTypes.Should().Contain("image/webp");
    }

    // ─── Test 3: ThumbnailOptions defaults ───────────────────────────────────

    [Fact]
    public void ThumbnailOptions_Defaults_AreCorrect()
    {
        var opts = new ThumbnailOptions();
        opts.Width.Should().Be(200);
        opts.Height.Should().Be(200);
        opts.Suffix.Should().Be("_thumb");
        opts.Enabled.Should().BeTrue();
    }

    // ─── Test 4: Enabled=false → skips processing, calls next ────────────────

    [Fact]
    public async Task Middleware_WhenDisabled_CallsNextWithoutProcessing()
    {
        var provider = BuildInMemoryProvider();
        var opts = new ImageProcessingOptions { Enabled = false };
        var middleware = new ImageProcessingMiddleware(opts, provider);

        using var imageStream = CreateTestImage(400, 400);
        var originalBytes = imageStream.ToArray();
        imageStream.Seek(0, SeekOrigin.Begin);

        var context = MakeContext(imageStream, "image/jpeg");
        var nextCalled = false;

        await middleware.InvokeAsync(context, ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        nextCalled.Should().BeTrue("next should be called when middleware is disabled");
        // The content stream should be the original one (unchanged)
        context.Request.Content.Should().BeSameAs(imageStream);
    }

    // ─── Test 5: Non-image content type → skips processing, calls next ────────

    [Fact]
    public async Task Middleware_NonImageContentType_CallsNextWithoutProcessing()
    {
        var provider = BuildInMemoryProvider();
        var opts = new ImageProcessingOptions { Enabled = true };
        var middleware = new ImageProcessingMiddleware(opts, provider);

        var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello world"));
        var context = MakeContext(content, "text/plain");
        var nextCalled = false;

        await middleware.InvokeAsync(context, ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        nextCalled.Should().BeTrue("next should be called for non-image content types");
        context.Request.Content.Should().BeSameAs(content);
    }

    // ─── Test 6: JPEG image → middleware resizes to MaxWidth ─────────────────

    [Fact]
    public async Task Middleware_JpegImage_ResizesToMaxWidth()
    {
        var provider = BuildInMemoryProvider();
        var opts = new ImageProcessingOptions
        {
            Enabled = true,
            MaxWidth = 100,
            OutputFormat = ImageOutputFormat.Jpeg
        };
        var middleware = new ImageProcessingMiddleware(opts, provider);

        using var imageStream = CreateTestImage(400, 300);
        var context = MakeContext(imageStream, "image/jpeg");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Request.Content.Seek(0, SeekOrigin.Begin);
        using var output = await Image.LoadAsync(context.Request.Content);
        output.Width.Should().BeLessThanOrEqualTo(100);
    }

    // ─── Test 7: PNG image → middleware processes without error ──────────────

    [Fact]
    public async Task Middleware_PngImage_ProcessesWithoutError()
    {
        var provider = BuildInMemoryProvider();
        var opts = new ImageProcessingOptions
        {
            Enabled = true,
            MaxWidth = 200,
            OutputFormat = ImageOutputFormat.Png
        };
        var middleware = new ImageProcessingMiddleware(opts, provider);

        using var imageStream = CreateTestImage(400, 300, "png");
        var context = MakeContext(imageStream, "image/png");
        Exception? caught = null;

        try
        {
            await middleware.InvokeAsync(context, _ => Task.CompletedTask);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        caught.Should().BeNull("PNG processing should not throw");
    }

    // ─── Test 8: OutputFormat.Jpeg → output content type is image/jpeg ────────

    [Fact]
    public async Task Middleware_OutputFormatJpeg_SetsContentTypeToJpeg()
    {
        var provider = BuildInMemoryProvider();
        var opts = new ImageProcessingOptions
        {
            Enabled = true,
            OutputFormat = ImageOutputFormat.Jpeg
        };
        var middleware = new ImageProcessingMiddleware(opts, provider);

        using var imageStream = CreateTestImage(100, 100);
        var context = MakeContext(imageStream, "image/jpeg");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Request.ContentType.Should().Be("image/jpeg");
    }

    // ─── Test 9: OutputFormat.Png → output content type is image/png ──────────

    [Fact]
    public async Task Middleware_OutputFormatPng_SetsContentTypeToPng()
    {
        var provider = BuildInMemoryProvider();
        var opts = new ImageProcessingOptions
        {
            Enabled = true,
            OutputFormat = ImageOutputFormat.Png
        };
        var middleware = new ImageProcessingMiddleware(opts, provider);

        using var imageStream = CreateTestImage(100, 100);
        var context = MakeContext(imageStream, "image/jpeg");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Request.ContentType.Should().Be("image/png");
    }

    // ─── Test 10: OutputFormat.Webp → output content type is image/webp ───────

    [Fact]
    public async Task Middleware_OutputFormatWebp_SetsContentTypeToWebp()
    {
        var provider = BuildInMemoryProvider();
        var opts = new ImageProcessingOptions
        {
            Enabled = true,
            OutputFormat = ImageOutputFormat.Webp
        };
        var middleware = new ImageProcessingMiddleware(opts, provider);

        using var imageStream = CreateTestImage(100, 100);
        var context = MakeContext(imageStream, "image/jpeg");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Request.ContentType.Should().Be("image/webp");
    }

    // ─── Test 11: Resized image width ≤ MaxWidth ─────────────────────────────

    [Fact]
    public async Task Middleware_ResizedImage_WidthIsLessThanOrEqualToMaxWidth()
    {
        var provider = BuildInMemoryProvider();
        var maxWidth = 150;
        var opts = new ImageProcessingOptions
        {
            Enabled = true,
            MaxWidth = maxWidth,
            OutputFormat = ImageOutputFormat.Jpeg
        };
        var middleware = new ImageProcessingMiddleware(opts, provider);

        using var imageStream = CreateTestImage(600, 400);
        var context = MakeContext(imageStream, "image/jpeg");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Request.Content.Seek(0, SeekOrigin.Begin);
        using var output = await Image.LoadAsync(context.Request.Content);
        output.Width.Should().BeLessThanOrEqualTo(maxWidth);
    }

    // ─── Test 12: Resized image height ≤ MaxHeight ────────────────────────────

    [Fact]
    public async Task Middleware_ResizedImage_HeightIsLessThanOrEqualToMaxHeight()
    {
        var provider = BuildInMemoryProvider();
        var maxHeight = 80;
        var opts = new ImageProcessingOptions
        {
            Enabled = true,
            MaxHeight = maxHeight,
            OutputFormat = ImageOutputFormat.Jpeg
        };
        var middleware = new ImageProcessingMiddleware(opts, provider);

        using var imageStream = CreateTestImage(400, 600);
        var context = MakeContext(imageStream, "image/jpeg");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Request.Content.Seek(0, SeekOrigin.Begin);
        using var output = await Image.LoadAsync(context.Request.Content);
        output.Height.Should().BeLessThanOrEqualTo(maxHeight);
    }

    // ─── Test 13: Small image smaller than MaxWidth is NOT upscaled ───────────

    [Fact]
    public async Task Middleware_SmallImage_SmallerThanMaxWidth_IsNotUpscaled()
    {
        var provider = BuildInMemoryProvider();
        var opts = new ImageProcessingOptions
        {
            Enabled = true,
            MaxWidth = 500,   // MaxWidth much larger than image
            MaxHeight = 500,
            OutputFormat = ImageOutputFormat.Jpeg
        };
        var middleware = new ImageProcessingMiddleware(opts, provider);

        // Source image is 50x50 — well below the 500px MaxWidth/MaxHeight
        using var imageStream = CreateTestImage(50, 50);
        var context = MakeContext(imageStream, "image/jpeg");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Request.Content.Seek(0, SeekOrigin.Begin);
        using var output = await Image.LoadAsync(context.Request.Content);
        output.Width.Should().Be(50, "image smaller than MaxWidth should not be upscaled");
        output.Height.Should().Be(50, "image smaller than MaxHeight should not be upscaled");
    }

    // ─── Test 14: No MaxWidth/MaxHeight → image dimensions preserved ──────────

    [Fact]
    public async Task Middleware_NoMaxDimensions_PreservesOriginalDimensions()
    {
        var provider = BuildInMemoryProvider();
        var opts = new ImageProcessingOptions
        {
            Enabled = true,
            OutputFormat = ImageOutputFormat.Jpeg
            // MaxWidth and MaxHeight are both null
        };
        var middleware = new ImageProcessingMiddleware(opts, provider);

        using var imageStream = CreateTestImage(300, 200);
        var context = MakeContext(imageStream, "image/jpeg");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Request.Content.Seek(0, SeekOrigin.Begin);
        using var output = await Image.LoadAsync(context.Request.Content);
        output.Width.Should().Be(300);
        output.Height.Should().Be(200);
    }

    // ─── Test 15: Both MaxWidth and MaxHeight → aspect ratio maintained ────────

    [Fact]
    public async Task Middleware_BothMaxDimensions_AspectRatioIsMaintained()
    {
        var provider = BuildInMemoryProvider();
        var opts = new ImageProcessingOptions
        {
            Enabled = true,
            MaxWidth = 100,
            MaxHeight = 100,
            OutputFormat = ImageOutputFormat.Jpeg
        };
        var middleware = new ImageProcessingMiddleware(opts, provider);

        // Wide image 400x200 — aspect ratio 2:1
        using var imageStream = CreateTestImage(400, 200);
        var context = MakeContext(imageStream, "image/jpeg");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Request.Content.Seek(0, SeekOrigin.Begin);
        using var output = await Image.LoadAsync(context.Request.Content);
        output.Width.Should().BeLessThanOrEqualTo(100);
        output.Height.Should().BeLessThanOrEqualTo(100);
    }

    // ─── Test 16: OutputFormat null → keeps original content type ─────────────

    [Fact]
    public async Task Middleware_OutputFormatNull_KeepsOriginalContentType()
    {
        var provider = BuildInMemoryProvider();
        var opts = new ImageProcessingOptions
        {
            Enabled = true,
            OutputFormat = null,
            MaxWidth = 200
        };
        var middleware = new ImageProcessingMiddleware(opts, provider);

        using var imageStream = CreateTestImage(400, 300);
        var context = MakeContext(imageStream, "image/jpeg");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Request.ContentType.Should().Be("image/jpeg");
    }

    // ─── Test 17: Middleware produces non-empty output stream ─────────────────

    [Fact]
    public async Task Middleware_AfterProcessing_OutputStreamIsNotEmpty()
    {
        var provider = BuildInMemoryProvider();
        var opts = new ImageProcessingOptions
        {
            Enabled = true,
            MaxWidth = 100,
            OutputFormat = ImageOutputFormat.Jpeg
        };
        var middleware = new ImageProcessingMiddleware(opts, provider);

        using var imageStream = CreateTestImage(300, 200);
        var context = MakeContext(imageStream, "image/jpeg");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Request.Content.Seek(0, SeekOrigin.Begin);
        var outputBytes = new MemoryStream();
        await context.Request.Content.CopyToAsync(outputBytes);
        outputBytes.Length.Should().BeGreaterThan(0);
    }

    // ─── Test 18: Thumbnail generation uploads a second file ─────────────────

    [Fact]
    public async Task Middleware_ThumbnailEnabled_UploadsThumbFile()
    {
        var provider = BuildInMemoryProvider();
        var opts = new ImageProcessingOptions
        {
            Enabled = true,
            OutputFormat = ImageOutputFormat.Jpeg,
            Thumbnail = new ThumbnailOptions
            {
                Enabled = true,
                Width = 50,
                Height = 50,
                Suffix = "_thumb"
            }
        };
        var middleware = new ImageProcessingMiddleware(opts, provider);

        using var imageStream = CreateTestImage(300, 200);
        var context = new StoragePipelineContext(new UploadRequest
        {
            Path = StoragePath.From("uploads", "photo.jpg"),
            Content = imageStream,
            ContentType = "image/jpeg",
            ContentLength = imageStream.Length
        });

        // The main upload goes to provider inside the "next" delegate
        await middleware.InvokeAsync(context, async ctx =>
        {
            await provider.UploadAsync(ctx.Request);
        });

        // The thumbnail should have been uploaded as photo_thumb.jpg
        provider.HasFile("uploads/photo_thumb.jpg").Should().BeTrue(
            "thumbnail file should be written to the in-memory store");
    }
}
