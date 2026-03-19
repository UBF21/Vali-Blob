using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Pipeline;

namespace ValiBlob.ImageSharp;

public sealed class ImageProcessingMiddleware : IStorageMiddleware
{
    private readonly ImageProcessingOptions _options;
    private readonly IStorageProvider _provider; // needed for thumbnail upload

    public ImageProcessingMiddleware(ImageProcessingOptions options, IStorageProvider provider)
    {
        _options = options;
        _provider = provider;
    }

    public async Task InvokeAsync(StoragePipelineContext context, StorageMiddlewareDelegate next)
    {
        if (!_options.Enabled) { await next(context); return; }

        var contentType = context.Request.ContentType;
        if (contentType is null || !_options.ProcessableContentTypes.Contains(contentType))
        {
            await next(context);
            return;
        }

        // Load image
        using var image = await Image.LoadAsync(context.Request.Content);

        // Resize if needed
        if (_options.MaxWidth.HasValue || _options.MaxHeight.HasValue)
        {
            var targetWidth = _options.MaxWidth ?? image.Width;
            var targetHeight = _options.MaxHeight ?? image.Height;

            if (image.Width > targetWidth || image.Height > targetHeight)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(targetWidth, targetHeight),
                    Mode = ResizeMode.Max
                }));
            }
        }

        // Encode to output stream
        var outputMs = new MemoryStream();
        string outputContentType;

        switch (_options.OutputFormat)
        {
            case ImageOutputFormat.Jpeg:
                await image.SaveAsJpegAsync(outputMs, new JpegEncoder { Quality = _options.JpegQuality });
                outputContentType = "image/jpeg";
                break;
            case ImageOutputFormat.Png:
                await image.SaveAsPngAsync(outputMs);
                outputContentType = "image/png";
                break;
            case ImageOutputFormat.Webp:
                await image.SaveAsWebpAsync(outputMs);
                outputContentType = "image/webp";
                break;
            default:
                // Keep original format
                await image.SaveAsync(outputMs, image.Metadata.DecodedImageFormat!);
                outputContentType = contentType;
                break;
        }

        outputMs.Seek(0, SeekOrigin.Begin);
        context.Request = context.Request
            .WithContent(outputMs)
            .WithContentType(outputContentType);

        await next(context);

        // Generate thumbnail AFTER main upload (if enabled and if provider is available)
        if (_options.Thumbnail?.Enabled == true)
        {
            await GenerateThumbnailAsync(image, context.Request, _options.Thumbnail, CancellationToken.None);
        }
    }

    private async Task GenerateThumbnailAsync(
        Image image,
        UploadRequest original,
        ThumbnailOptions thumbOptions,
        CancellationToken ct)
    {
        try
        {
            using var thumb = image.Clone(x => x.Resize(new ResizeOptions
            {
                Size = new Size(thumbOptions.Width, thumbOptions.Height),
                Mode = ResizeMode.Max
            }));

            var thumbMs = new MemoryStream();
            await thumb.SaveAsJpegAsync(thumbMs, new JpegEncoder { Quality = 80 });
            thumbMs.Seek(0, SeekOrigin.Begin);

            var originalPath = original.Path.ToString();
            var ext = Path.GetExtension(originalPath);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(originalPath);
            var dir = Path.GetDirectoryName(originalPath) ?? "";
            var thumbPath = string.IsNullOrEmpty(dir)
                ? $"{nameWithoutExt}{thumbOptions.Suffix}.jpg"
                : $"{dir}/{nameWithoutExt}{thumbOptions.Suffix}.jpg";

            var thumbRequest = new UploadRequest
            {
                Path = StoragePath.From(thumbPath),
                Content = thumbMs,
                ContentType = "image/jpeg",
                ContentLength = thumbMs.Length
            };

            await _provider.UploadAsync(thumbRequest, null, ct);
        }
        catch
        {
            // thumbnail failure is non-fatal
        }
    }
}
