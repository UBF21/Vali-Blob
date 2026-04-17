using FluentAssertions;
using ValiBlob.Core.Pipeline;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class PipelineContextKeysTests
{
    [Fact]
    public void All_Keys_Are_Unique()
    {
        var keys = new[]
        {
            PipelineContextKeys.DeduplicationHash,
            PipelineContextKeys.DeduplicationIsDuplicate,
            PipelineContextKeys.DetectedContentType,
            PipelineContextKeys.ConflictResolutionAction,
            PipelineContextKeys.ConflictResolutionPath,
            PipelineContextKeys.VirusScanStatus,
            PipelineContextKeys.VirusScanError
        };

        // Ensure no duplicate keys exist
        keys.Distinct().Should().HaveCount(keys.Length);
    }

    [Fact]
    public void All_Keys_Follow_Naming_Convention()
    {
        var keys = new[]
        {
            PipelineContextKeys.DeduplicationHash,
            PipelineContextKeys.DeduplicationIsDuplicate,
            PipelineContextKeys.DetectedContentType,
            PipelineContextKeys.ConflictResolutionAction,
            PipelineContextKeys.ConflictResolutionPath,
            PipelineContextKeys.VirusScanStatus,
            PipelineContextKeys.VirusScanError
        };

        // All keys should be lowercase with dots as separators
        // Underscores are allowed in segment names
        foreach (var key in keys)
        {
            key.Should().NotContainAny(new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z" });
            key.Should().ContainAll(new[] { "." });
        }
    }

    [Fact]
    public void Deduplication_Keys_Are_Grouped()
    {
        PipelineContextKeys.DeduplicationHash.Should().StartWith("valiblob.dedup.");
        PipelineContextKeys.DeduplicationIsDuplicate.Should().StartWith("valiblob.dedup.");
    }

    [Fact]
    public void Virus_Scan_Keys_Are_Grouped()
    {
        PipelineContextKeys.VirusScanStatus.Should().StartWith("valiblob.virus.");
        PipelineContextKeys.VirusScanError.Should().StartWith("valiblob.virus.");
    }

    [Fact]
    public void ConflictResolution_Keys_Are_Grouped()
    {
        PipelineContextKeys.ConflictResolutionAction.Should().StartWith("valiblob.conflict.");
        PipelineContextKeys.ConflictResolutionPath.Should().StartWith("valiblob.conflict.");
    }

    [Fact]
    public void Keys_Enable_Type_Safe_Context_Access()
    {
        var context = new StoragePipelineContext(
            new ValiBlob.Core.Models.UploadRequest
            {
                Path = ValiBlob.Core.Models.StoragePath.From("test.txt"),
                Content = new MemoryStream(new byte[] { 1, 2, 3 }),
                ContentLength = 3
            });

        // Store value using constant key
        context.Items[PipelineContextKeys.DeduplicationHash] = "abc123";

        // Retrieve value using same constant
        context.Items.Should().ContainKey(PipelineContextKeys.DeduplicationHash);
        context.Items[PipelineContextKeys.DeduplicationHash].Should().Be("abc123");

        // Cannot accidentally use different string literal with same value
        context.Items.Keys.Should().NotContain("deduplication.hash"); // Old magic string
    }
}
