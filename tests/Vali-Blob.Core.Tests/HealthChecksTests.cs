using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Models;
using ValiBlob.HealthChecks;
using ValiBlob.HealthChecks.Extensions;
using ValiBlob.Testing;
using ValiBlob.Testing.Extensions;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class HealthChecksTests
{
    // ─── Stub: always throws on ListFilesAsync ────────────────────────────────

    private sealed class ThrowingProvider : IStorageProvider
    {
        public string ProviderName => "Throwing";

        public Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesAsync(
            string? prefix = null,
            ListOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated failure");

        public Task<StorageResult<UploadResult>> UploadAsync(UploadRequest request, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult<Stream>> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult> DeleteAsync(string path, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult<bool>> ExistsAsync(string path, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult<string>> GetUrlAsync(string path, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult> CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult> MoveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult<FileMetadata>> GetMetadataAsync(string path, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult> SetMetadataAsync(string path, IDictionary<string, string> metadata, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult<BatchDeleteResult>> DeleteManyAsync(IEnumerable<StoragePath> paths, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public IAsyncEnumerable<FileEntry> ListAllAsync(string? prefix = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult> DeleteFolderAsync(string prefix, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult<IReadOnlyList<string>>> ListFoldersAsync(string? prefix = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult<UploadResult>> UploadFromUrlAsync(string sourceUrl, StoragePath destinationPath, string? bucketOverride = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    // ─── Stub: ListFilesAsync returns a failure result ────────────────────────

    private sealed class FailingListProvider : IStorageProvider
    {
        public string ProviderName => "FailingList";

        public Task<StorageResult<IReadOnlyList<FileEntry>>> ListFilesAsync(
            string? prefix = null,
            ListOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(StorageResult<IReadOnlyList<FileEntry>>.Failure(
                "Storage is unavailable", StorageErrorCode.ProviderError));

        public Task<StorageResult<UploadResult>> UploadAsync(UploadRequest request, IProgress<UploadProgress>? progress = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult<Stream>> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult> DeleteAsync(string path, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult<bool>> ExistsAsync(string path, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult<string>> GetUrlAsync(string path, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult> CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult> MoveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult<FileMetadata>> GetMetadataAsync(string path, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult> SetMetadataAsync(string path, IDictionary<string, string> metadata, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult<BatchDeleteResult>> DeleteManyAsync(IEnumerable<StoragePath> paths, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public IAsyncEnumerable<FileEntry> ListAllAsync(string? prefix = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult> DeleteFolderAsync(string prefix, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult<IReadOnlyList<string>>> ListFoldersAsync(string? prefix = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<StorageResult<UploadResult>> UploadFromUrlAsync(string sourceUrl, StoragePath destinationPath, string? bucketOverride = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static HealthCheckContext MakeContext(string checkName = "test-check")
        => new()
        {
            Registration = new HealthCheckRegistration(
                checkName,
                _ => null!,
                HealthStatus.Unhealthy,
                null)
        };

    private static InMemoryStorageProvider BuildInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddValiBlob().UseInMemory();
        return services.BuildServiceProvider().GetRequiredService<InMemoryStorageProvider>();
    }

    // ─── Test 1: StorageProviderHealthCheck implements IHealthCheck ───────────

    [Fact]
    public void StorageProviderHealthCheck_ImplementsIHealthCheck()
    {
        var provider = BuildInMemoryProvider();
        var check = new StorageProviderHealthCheck(provider, new StorageHealthCheckOptions());
        check.Should().BeAssignableTo<IHealthCheck>();
    }

    // ─── Test 2: Returns Healthy when ListFilesAsync succeeds ─────────────────

    [Fact]
    public async Task StorageProviderHealthCheck_ReturnsHealthy_WhenListFilesSucceeds()
    {
        var provider = BuildInMemoryProvider();
        var check = new StorageProviderHealthCheck(provider, new StorageHealthCheckOptions());
        var context = MakeContext();

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    // ─── Test 3: Returns Unhealthy when provider throws ───────────────────────

    [Fact]
    public async Task StorageProviderHealthCheck_ReturnsUnhealthy_WhenProviderThrows()
    {
        var provider = new ThrowingProvider();
        var check = new StorageProviderHealthCheck(provider, new StorageHealthCheckOptions());
        var context = MakeContext();

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    // ─── Test 4: Returns Degraded when ListFilesAsync returns failure ─────────

    [Fact]
    public async Task StorageProviderHealthCheck_ReturnsDegraded_WhenListFilesReturnsFail()
    {
        var provider = new FailingListProvider();
        var check = new StorageProviderHealthCheck(provider, new StorageHealthCheckOptions());
        var context = MakeContext();

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    // ─── Test 5: Description contains provider name when unhealthy ────────────

    [Fact]
    public async Task StorageProviderHealthCheck_Description_ContainsProviderName_WhenUnhealthy()
    {
        var provider = new ThrowingProvider();
        var check = new StorageProviderHealthCheck(provider, new StorageHealthCheckOptions());
        var context = MakeContext();

        var result = await check.CheckHealthAsync(context);

        result.Description.Should().Contain(provider.ProviderName);
    }

    // ─── Test 6: Description contains provider name when degraded ────────────

    [Fact]
    public async Task StorageProviderHealthCheck_Description_ContainsProviderName_WhenDegraded()
    {
        var provider = new FailingListProvider();
        var check = new StorageProviderHealthCheck(provider, new StorageHealthCheckOptions());
        var context = MakeContext();

        var result = await check.CheckHealthAsync(context);

        result.Description.Should().Contain(provider.ProviderName);
    }

    // ─── Test 7: StorageHealthCheckOptions defaults ───────────────────────────

    [Fact]
    public void StorageHealthCheckOptions_DefaultTimeout_IsFiveSeconds()
    {
        var opts = new StorageHealthCheckOptions();
        opts.Timeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    // ─── Test 8: StorageHealthCheckOptions ProbePefix is null by default ──────

    [Fact]
    public void StorageHealthCheckOptions_ProbePrefix_IsNullByDefault()
    {
        var opts = new StorageHealthCheckOptions();
        opts.ProbePrefix.Should().BeNull();
    }

    // ─── Test 9: HealthStatus.Healthy on success ──────────────────────────────

    [Fact]
    public async Task StorageProviderHealthCheck_HealthCheckResultStatus_IsHealthyOnSuccess()
    {
        var provider = BuildInMemoryProvider();
        var check = new StorageProviderHealthCheck(provider, new StorageHealthCheckOptions());
        var context = MakeContext();

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    // ─── Test 10: HealthStatus.Unhealthy on exception ────────────────────────

    [Fact]
    public async Task StorageProviderHealthCheck_HealthCheckResultStatus_IsUnhealthyOnException()
    {
        var provider = new ThrowingProvider();
        var check = new StorageProviderHealthCheck(provider, new StorageHealthCheckOptions());
        var context = MakeContext();

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    // ─── Test 11: DI registration via AddValiBlob + AddValiHealthChecks ───────

    [Fact]
    public void DI_AddValiBlob_RegistersHealthCheck()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddValiBlob().UseInMemory();
        services.AddHealthChecks().AddValiBlob();

        var sp = services.BuildServiceProvider();

        // IHealthCheckService should be resolvable after health check registration
        var hcs = sp.GetService<HealthCheckService>();
        hcs.Should().NotBeNull();
    }

    // ─── Test 12: Health check name defaults to "valiblob" ───────────────────

    [Fact]
    public void DI_AddValiBlob_DefaultHealthCheckName_IsValiblob()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddValiBlob().UseInMemory();
        services.AddHealthChecks().AddValiBlob();

        // Verify the registration name by resolving HealthCheckRegistration via options
        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        options.Value.Registrations.Should().Contain(r => r.Name == "valiblob");
    }

    // ─── Test 13: Custom health check name when specified ────────────────────

    [Fact]
    public void DI_AddValiBlob_CustomHealthCheckName_IsRegisteredCorrectly()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddValiBlob().UseInMemory();
        services.AddHealthChecks().AddValiBlob(name: "my-storage");

        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        options.Value.Registrations.Should().Contain(r => r.Name == "my-storage");
    }

    // ─── Test 14: Health check completes within reasonable time ──────────────

    [Fact]
    public async Task StorageProviderHealthCheck_CompletesWithinReasonableTime()
    {
        var provider = BuildInMemoryProvider();
        var check = new StorageProviderHealthCheck(provider, new StorageHealthCheckOptions());
        var context = MakeContext();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await check.CheckHealthAsync(context, cts.Token);

        cts.IsCancellationRequested.Should().BeFalse("health check should complete well before the 10-second timeout");
        result.Status.Should().Be(HealthStatus.Healthy);
    }

    // ─── Test 15: Named provider health check name uses provider name ─────────

    [Fact]
    public void DI_AddValiBlob_WithProviderName_CheckNameIncludesProviderName()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddValiBlob().UseInMemory();
        services.AddHealthChecks().AddValiBlob(providerName: "InMemory");

        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();
        options.Value.Registrations.Should().Contain(r => r.Name.Contains("inmemory", StringComparison.OrdinalIgnoreCase));
    }

    // ─── Test 16: Unhealthy result carries Exception ──────────────────────────

    [Fact]
    public async Task StorageProviderHealthCheck_WhenProviderThrows_ResultExceptionIsSet()
    {
        var provider = new ThrowingProvider();
        var check = new StorageProviderHealthCheck(provider, new StorageHealthCheckOptions());
        var context = MakeContext();

        var result = await check.CheckHealthAsync(context);

        result.Exception.Should().NotBeNull();
        result.Exception.Should().BeOfType<InvalidOperationException>();
    }

    // ─── Test 17: ProbePrefix is forwarded to ListFilesAsync ─────────────────

    [Fact]
    public async Task StorageProviderHealthCheck_WithProbePrefix_StillReturnsHealthy()
    {
        var provider = BuildInMemoryProvider();
        var opts = new StorageHealthCheckOptions { ProbePrefix = "health-probe/" };
        var check = new StorageProviderHealthCheck(provider, opts);
        var context = MakeContext();

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Healthy);
    }
}
