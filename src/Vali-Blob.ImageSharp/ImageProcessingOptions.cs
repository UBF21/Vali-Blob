namespace ValiBlob.ImageSharp;

public class ImageProcessingOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum width in pixels. null = no limit.</summary>
    public int? MaxWidth { get; set; }

    /// <summary>Maximum height in pixels. null = no limit.</summary>
    public int? MaxHeight { get; set; }

    /// <summary>JPEG quality (1-100). Default 85.</summary>
    public int JpegQuality { get; set; } = 85;

    /// <summary>Auto-convert images to this format on upload. null = keep original.</summary>
    public ImageOutputFormat? OutputFormat { get; set; }

    /// <summary>Content types considered as images and eligible for processing.</summary>
    public HashSet<string> ProcessableContentTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/bmp", "image/webp", "image/tiff"
    };

    /// <summary>Generate a thumbnail alongside the main upload.</summary>
    public ThumbnailOptions? Thumbnail { get; set; }
}

public class ThumbnailOptions
{
    public bool Enabled { get; set; } = true;
    public int Width { get; set; } = 200;
    public int Height { get; set; } = 200;
    /// <summary>Suffix appended to filename for thumbnail. E.g. "_thumb" → "photo_thumb.jpg"</summary>
    public string Suffix { get; set; } = "_thumb";
}

public enum ImageOutputFormat { Jpeg, Png, Webp }
