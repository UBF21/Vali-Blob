namespace ValiBlob.Core.Models;

public static class StoragePathExtensions
{
    /// <summary>Prepends a UTC date prefix: "2026/03/17/original-path"</summary>
    public static StoragePath WithDatePrefix(this StoragePath path)
    {
        var now = DateTimeOffset.UtcNow;
        var prefix = $"{now:yyyy/MM/dd}";
        return StoragePath.From($"{prefix}/{path}");
    }

    /// <summary>Prepends a timestamp with time precision: "2026/03/17/14-30-00/original-path"</summary>
    public static StoragePath WithTimestampPrefix(this StoragePath path)
    {
        var now = DateTimeOffset.UtcNow;
        var prefix = $"{now:yyyy/MM/dd/HH-mm-ss}";
        return StoragePath.From($"{prefix}/{path}");
    }

    /// <summary>Appends a short SHA-256 hash suffix to the filename (before extension).
    /// E.g. "photo.jpg" → "photo_a3f2b1c4.jpg"</summary>
    public static StoragePath WithHashSuffix(this StoragePath path, string content)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
        var shortHash = BitConverter.ToString(hash, 0, 4).Replace("-", "").ToLowerInvariant();

        var pathStr = path.ToString();
        var ext = System.IO.Path.GetExtension(pathStr);
        var nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(pathStr);
        var dir = System.IO.Path.GetDirectoryName(pathStr) ?? "";

        var newName = string.IsNullOrEmpty(dir)
            ? $"{nameWithoutExt}_{shortHash}{ext}"
            : $"{dir}/{nameWithoutExt}_{shortHash}{ext}";
        return StoragePath.From(newName);
    }

    /// <summary>Appends a short random suffix to avoid collisions.
    /// E.g. "photo.jpg" → "photo_a3f2b1c4.jpg"</summary>
    public static StoragePath WithRandomSuffix(this StoragePath path)
    {
        var suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        var pathStr = path.ToString();
        var ext = System.IO.Path.GetExtension(pathStr);
        var nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(pathStr);
        var dir = System.IO.Path.GetDirectoryName(pathStr) ?? "";

        var newName = string.IsNullOrEmpty(dir)
            ? $"{nameWithoutExt}_{suffix}{ext}"
            : $"{dir}/{nameWithoutExt}_{suffix}{ext}";
        return StoragePath.From(newName);
    }

    /// <summary>Sanitizes the path by replacing invalid characters with underscores and normalizing slashes.</summary>
    public static StoragePath Sanitize(this StoragePath path)
    {
        var pathStr = path.ToString();
        // Replace backslashes with forward slashes
        pathStr = pathStr.Replace('\\', '/');
        // Remove consecutive slashes
        while (pathStr.Contains("//"))
            pathStr = pathStr.Replace("//", "/");
        // Replace invalid chars (keep alphanumeric, dash, underscore, dot, slash)
        var chars = pathStr.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.' && c != '/')
                chars[i] = '_';
        }
        // Remove leading/trailing slashes
        var result = new string(chars).Trim('/');
        return StoragePath.From(result);
    }
}
