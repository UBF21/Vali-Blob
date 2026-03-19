# ValiBlob.ImageSharp

Image processing middleware for ValiBlob using [ImageSharp](https://github.com/SixLabors/ImageSharp).

## Features

- Resize images on upload (max width/height with aspect-ratio preservation)
- Convert to a target format (JPEG, PNG, WebP)
- Control JPEG quality
- Optionally generate a thumbnail alongside the main upload

## Installation

```
dotnet add package ValiBlob.ImageSharp
```

## Usage

```csharp
services.AddValiBlob()
    .WithImageProcessing(opts =>
    {
        opts.MaxWidth = 1920;
        opts.MaxHeight = 1080;
        opts.JpegQuality = 80;
        opts.OutputFormat = ImageOutputFormat.Jpeg;
        opts.Thumbnail = new ThumbnailOptions
        {
            Width = 200,
            Height = 200,
            Suffix = "_thumb"
        };
    });
```

Only content types listed in `ProcessableContentTypes` (default: jpeg, png, gif, bmp, webp, tiff) are processed; all others pass through unchanged.
