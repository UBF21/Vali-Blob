using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Models;

namespace ValiBlob.Core.Migration;

public sealed class StorageMigrator : IStorageMigrator
{
    private readonly IStorageFactory _factory;
    private readonly ILogger<StorageMigrator> _logger;

    public StorageMigrator(IStorageFactory factory, ILogger<StorageMigrator> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<MigrationResult> MigrateAsync(
        string sourceProviderName,
        string destinationProviderName,
        MigrationOptions? options = null,
        IProgress<MigrationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new MigrationOptions();
        var sw = Stopwatch.StartNew();

        var source = _factory.Create(sourceProviderName)
            ?? throw new InvalidOperationException($"Source provider '{sourceProviderName}' was not found.");
        var destination = _factory.Create(destinationProviderName)
            ?? throw new InvalidOperationException($"Destination provider '{destinationProviderName}' was not found.");

        // Enumerate all files from source
        var allFiles = new List<FileEntry>();
        await foreach (var entry in source.ListAllAsync(options.Prefix, cancellationToken))
        {
            allFiles.Add(entry);
            if (options.MaxFiles.HasValue && allFiles.Count >= options.MaxFiles.Value)
                break;
        }

        var migrated = 0;
        var skipped = 0;
        var failed = 0;
        var errors = new List<MigrationError>();
        long totalBytes = 0;

        for (int i = 0; i < allFiles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = allFiles[i];

            progress?.Report(new MigrationProgress
            {
                TotalFiles = allFiles.Count,
                ProcessedFiles = i,
                CurrentFile = file.Path
            });

            try
            {
                // Check if already exists in destination
                if (options.SkipExisting)
                {
                    var existsResult = await destination.ExistsAsync(file.Path, cancellationToken);
                    if (existsResult.IsSuccess && existsResult.Value)
                    {
                        skipped++;
                        continue;
                    }
                }

                if (options.DryRun)
                {
                    migrated++;
                    continue;
                }

                // Download from source
                var downloadResult = await source.DownloadAsync(
                    new DownloadRequest { Path = file.Path }, cancellationToken);

                if (!downloadResult.IsSuccess)
                {
                    failed++;
                    errors.Add(new MigrationError { Path = file.Path, Reason = downloadResult.ErrorMessage ?? "Download failed" });
                    continue;
                }

                // Upload to destination
                using var stream = downloadResult.Value!;
                var uploadRequest = new UploadRequest
                {
                    Path = StoragePath.From(file.Path),
                    Content = stream,
                    ContentType = file.ContentType,
                    ContentLength = file.SizeBytes > 0 ? file.SizeBytes : (long?)null
                };

                var uploadResult = await destination.UploadAsync(uploadRequest, null, cancellationToken);

                if (!uploadResult.IsSuccess)
                {
                    failed++;
                    errors.Add(new MigrationError { Path = file.Path, Reason = uploadResult.ErrorMessage ?? "Upload failed" });
                    continue;
                }

                totalBytes += file.SizeBytes;
                migrated++;

                // Optionally delete from source
                if (options.DeleteSourceAfterCopy)
                    await source.DeleteAsync(file.Path, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Migration failed for file {Path}", file.Path);
                failed++;
                errors.Add(new MigrationError { Path = file.Path, Reason = ex.Message });
            }
        }

        return new MigrationResult
        {
            TotalFiles = allFiles.Count,
            Migrated = migrated,
            Skipped = skipped,
            Failed = failed,
            Errors = errors.AsReadOnly(),
            Duration = sw.Elapsed,
            TotalBytesTransferred = totalBytes
        };
    }
}
