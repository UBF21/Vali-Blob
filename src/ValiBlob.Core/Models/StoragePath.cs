using System.Collections.Generic;

namespace ValiBlob.Core.Models;

/// <summary>
/// Represents a cloud storage path as typed segments, internally concatenated with '/'.
/// Avoids raw string manipulation and path separator bugs.
/// </summary>
public sealed class StoragePath : IEquatable<StoragePath>
{
    private readonly string[] _segments;

    private StoragePath(string[] segments)
    {
        if (segments.Length == 0)
            throw new ArgumentException("StoragePath must have at least one segment.", nameof(segments));

        _segments = segments;
    }

    /// <summary>Creates a StoragePath from one or more path segments.</summary>
    /// <example>StoragePath.From("documents", "invoices", "2024", "file.pdf")</example>
    public static StoragePath From(params string[] segments)
    {
        if (segments is null || segments.Length == 0)
            throw new ArgumentException("At least one segment is required.", nameof(segments));

        var cleaned = new List<string>();
        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment))
                continue;
            // support passing a pre-joined path string as single segment
            foreach (var part in segment.Split('/'))
            {
                var trimmed = part.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    cleaned.Add(trimmed);
            }
        }

        if (cleaned.Count == 0)
            throw new ArgumentException("All segments were empty after cleaning.", nameof(segments));

        return new StoragePath(cleaned.ToArray());
    }

    /// <summary>Returns a new StoragePath with the additional segment appended.</summary>
    public StoragePath Append(string segment)
    {
        var combined = new string[_segments.Length + 1];
        _segments.CopyTo(combined, 0);
        combined[_segments.Length] = segment;
        return From(combined);
    }

    /// <summary>Returns the parent path (all segments except last), or null if root.</summary>
    public StoragePath? Parent
    {
        get
        {
            if (_segments.Length <= 1)
                return null;
            var parentSegments = new string[_segments.Length - 1];
            Array.Copy(_segments, parentSegments, parentSegments.Length);
            return new StoragePath(parentSegments);
        }
    }

    /// <summary>The file name — last segment.</summary>
    public string FileName => _segments[_segments.Length - 1];

    /// <summary>Extension of the last segment including the dot, e.g. ".pdf".</summary>
    public string? Extension
    {
        get
        {
            var name = FileName;
            var dotIndex = name.LastIndexOf('.');
            return dotIndex >= 0 ? name.Substring(dotIndex) : null;
        }
    }

    /// <summary>All segments that form this path.</summary>
    public IReadOnlyList<string> Segments => _segments;

    /// <summary>Appends a segment using the / operator.</summary>
    public static StoragePath operator /(StoragePath left, string right) => left.Append(right);

    /// <summary>Implicit conversion to string — joins segments with '/'.</summary>
    public static implicit operator string(StoragePath path) => path.ToString();

    /// <summary>Implicit conversion from string — splits on '/'.</summary>
    public static implicit operator StoragePath(string path) => From(path);

    public override string ToString() => string.Join("/", _segments);

    public bool Equals(StoragePath? other)
    {
        if (other is null) return false;
        if (_segments.Length != other._segments.Length) return false;
        for (var i = 0; i < _segments.Length; i++)
            if (!string.Equals(_segments[i], other._segments[i], StringComparison.Ordinal))
                return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is StoragePath other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var s in _segments)
                hash = hash * 31 + (s?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public static bool operator ==(StoragePath? left, StoragePath? right)
        => left?.Equals(right) ?? right is null;
    public static bool operator !=(StoragePath? left, StoragePath? right) => !(left == right);
}
