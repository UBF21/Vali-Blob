using FluentAssertions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Resumable;
using ValiBlob.GCP;
using ValiBlob.OCI;
using Xunit;

namespace ValiBlob.Core.Tests;

/// <summary>
/// Lightweight unit tests covering GCP and OCI options/model classes and shared
/// resumable-upload helpers. No cloud SDKs are invoked.
/// </summary>
public sealed class GcpOciFeatureParityTests
{
    // ─── GcpOptions ───────────────────────────────────────────────────────────

    [Fact]
    public void GcpOptions_CanBeCreated_WithAllProperties()
    {
        var options = new GCPStorageOptions
        {
            ProjectId = "my-gcp-project",
            Bucket = "my-gcp-bucket",
            CredentialsPath = "/var/secrets/service-account.json",
            CredentialsJson = "{\"type\":\"service_account\"}"
        };

        options.ProjectId.Should().Be("my-gcp-project");
        options.Bucket.Should().Be("my-gcp-bucket");
        options.CredentialsPath.Should().Be("/var/secrets/service-account.json");
        options.CredentialsJson.Should().Be("{\"type\":\"service_account\"}");
    }

    [Fact]
    public void GcpOptions_WithNoCredentials_IsValidOptionsObject()
    {
        var options = new GCPStorageOptions
        {
            ProjectId = "project-without-credentials",
            Bucket = "my-bucket"
        };

        options.CredentialsPath.Should().BeNull();
        options.CredentialsJson.Should().BeNull();
        options.ProjectId.Should().NotBeNullOrEmpty();
        options.Bucket.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GcpOptions_SectionName_IsExpected()
    {
        GCPStorageOptions.SectionName.Should().Be("ValiBlob:GCP");
    }

    // ─── OciOptions ───────────────────────────────────────────────────────────

    [Fact]
    public void OciOptions_CanBeCreated_WithAllRequiredProperties()
    {
        var options = new OCIStorageOptions
        {
            TenancyId = "ocid1.tenancy.oc1..example",
            UserId = "ocid1.user.oc1..example",
            Region = "sa-saopaulo-1",
            Fingerprint = "aa:bb:cc:dd:ee:ff",
            PrivateKeyPath = "/home/user/.oci/oci_api_key.pem",
            Bucket = "my-oci-bucket",
            Namespace = "my-namespace"
        };

        options.TenancyId.Should().Be("ocid1.tenancy.oc1..example");
        options.UserId.Should().Be("ocid1.user.oc1..example");
        options.Region.Should().Be("sa-saopaulo-1");
        options.Fingerprint.Should().Be("aa:bb:cc:dd:ee:ff");
        options.PrivateKeyPath.Should().Be("/home/user/.oci/oci_api_key.pem");
        options.Bucket.Should().Be("my-oci-bucket");
        options.Namespace.Should().Be("my-namespace");
    }

    [Fact]
    public void OciOptions_Region_IsConfigurable()
    {
        var options = new OCIStorageOptions();

        // Region has no hardcoded default — users must configure it explicitly
        options.Region.Should().Be(string.Empty);

        // Users set the region during configuration
        options.Region = "ap-southeast-1";
        options.Region.Should().Be("ap-southeast-1");
    }

    [Fact]
    public void OciOptions_SectionName_IsExpected()
    {
        OCIStorageOptions.SectionName.Should().Be("ValiBlob:OCI");
    }

    // ─── ResumableUploadOptions defaults ─────────────────────────────────────

    [Fact]
    public void ResumableUploadOptions_DefaultChunkSizeBytes_IsEightMb()
    {
        var options = new ResumableUploadOptions();

        // Default is 8 MB
        options.DefaultChunkSizeBytes.Should().Be(8 * 1024 * 1024);
    }

    [Fact]
    public void ResumableUploadOptions_MinPartSizeBytes_IsFiveMb()
    {
        var options = new ResumableUploadOptions();

        options.MinPartSizeBytes.Should().Be(5 * 1024 * 1024);
    }

    [Fact]
    public void ResumableUploadOptions_MaxConcurrentChunks_DefaultIsOne()
    {
        var options = new ResumableUploadOptions();

        options.MaxConcurrentChunks.Should().Be(1);
    }

    [Fact]
    public void ResumableUploadOptions_EnableChecksumValidation_DefaultIsTrue()
    {
        var options = new ResumableUploadOptions();

        options.EnableChecksumValidation.Should().BeTrue();
    }

    [Fact]
    public void ResumableUploadOptions_SessionExpiration_DefaultIsTwentyFourHours()
    {
        var options = new ResumableUploadOptions();

        options.SessionExpiration.Should().Be(TimeSpan.FromHours(24));
    }

    // ─── ResumableUploadRequest builder ──────────────────────────────────────

    [Fact]
    public void ResumableUploadRequest_CanBeBuilt_WithAllProperties()
    {
        var metadata = new Dictionary<string, string> { ["author"] = "Felipe", ["version"] = "2" };

        var request = new ResumableUploadRequest
        {
            Path = StoragePath.From("uploads", "large-file.bin"),
            ContentType = "application/octet-stream",
            TotalSize = 100 * 1024 * 1024,  // 100 MB
            Metadata = metadata,
            BucketOverride = "custom-bucket",
            Options = new ResumableUploadRequestOptions
            {
                ChunkSizeBytes = 10 * 1024 * 1024,
                SessionExpiration = TimeSpan.FromHours(48)
            }
        };

        request.Path.ToString().Should().Be("uploads/large-file.bin");
        request.ContentType.Should().Be("application/octet-stream");
        request.TotalSize.Should().Be(100 * 1024 * 1024);
        request.BucketOverride.Should().Be("custom-bucket");
        request.Metadata.Should().ContainKey("author").WhoseValue.Should().Be("Felipe");
        request.Options!.ChunkSizeBytes.Should().Be(10 * 1024 * 1024);
        request.Options.SessionExpiration.Should().Be(TimeSpan.FromHours(48));
    }

    // ─── ChunkChecksumHelper — determinism ───────────────────────────────────

    [Fact]
    public void ChunkChecksumHelper_ComputeMd5Base64_IsDeterministic()
    {
        var data = System.Text.Encoding.UTF8.GetBytes("ValiBlob deterministic checksum test");

        var first = ChunkChecksumHelper.ComputeMd5Base64(data);
        var second = ChunkChecksumHelper.ComputeMd5Base64(data);

        first.Should().Be(second);
        first.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ChunkChecksumHelper_ComputeMd5Base64_DifferentInputs_ProduceDifferentHashes()
    {
        var dataA = System.Text.Encoding.UTF8.GetBytes("data-set-alpha");
        var dataB = System.Text.Encoding.UTF8.GetBytes("data-set-beta");

        var hashA = ChunkChecksumHelper.ComputeMd5Base64(dataA);
        var hashB = ChunkChecksumHelper.ComputeMd5Base64(dataB);

        hashA.Should().NotBe(hashB);
    }

    // ─── ResumableChunkRequest — ExpectedMd5 ─────────────────────────────────

    [Fact]
    public void ResumableChunkRequest_ExpectedMd5_IsSetCorrectly()
    {
        var chunkData = System.Text.Encoding.UTF8.GetBytes("chunk payload data");
        var expectedMd5 = ChunkChecksumHelper.ComputeMd5Base64(chunkData);

        var request = new ResumableChunkRequest
        {
            UploadId = "session-abc-123",
            Data = new MemoryStream(chunkData),
            Offset = 0,
            Length = chunkData.Length,
            ExpectedMd5 = expectedMd5
        };

        request.ExpectedMd5.Should().Be(expectedMd5);
        request.ExpectedMd5.Should().NotBeNullOrEmpty();
        request.UploadId.Should().Be("session-abc-123");
        request.Offset.Should().Be(0);
        request.Length.Should().Be(chunkData.Length);
    }

    [Fact]
    public void ResumableChunkRequest_WithNullExpectedMd5_IsAllowed()
    {
        var request = new ResumableChunkRequest
        {
            UploadId = "no-checksum-session",
            Data = new MemoryStream(new byte[128]),
            Offset = 0
        };

        request.ExpectedMd5.Should().BeNull();
    }
}
