namespace ValiBlob.Core.Abstractions;

public interface IStorageMigrator
{
    /// <summary>Migrates files from one provider to another.</summary>
    Task<MigrationResult> MigrateAsync(
        string sourceProviderName,
        string destinationProviderName,
        MigrationOptions? options = null,
        IProgress<MigrationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class MigrationOptions
{
    /// <summary>Only migrate files with this prefix.</summary>
    public string? Prefix { get; init; }

    /// <summary>If true, only reports what would be migrated without doing it.</summary>
    public bool DryRun { get; init; } = false;

    /// <summary>If true, deletes files from source after successful copy.</summary>
    public bool DeleteSourceAfterCopy { get; init; } = false;

    /// <summary>Skip files that already exist in destination.</summary>
    public bool SkipExisting { get; init; } = true;

    /// <summary>Max files to migrate. null = all.</summary>
    public int? MaxFiles { get; init; }
}

public sealed class MigrationResult
{
    public int TotalFiles { get; init; }
    public int Migrated { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }
    public IReadOnlyList<MigrationError> Errors { get; init; } = Array.Empty<MigrationError>();
    public TimeSpan Duration { get; init; }
    public long TotalBytesTransferred { get; init; }
}

public sealed class MigrationError
{
    public string Path { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed class MigrationProgress
{
    public int TotalFiles { get; init; }
    public int ProcessedFiles { get; init; }
    public string CurrentFile { get; init; } = string.Empty;
    public double Percentage => TotalFiles > 0 ? (double)ProcessedFiles / TotalFiles * 100 : 0;
}
