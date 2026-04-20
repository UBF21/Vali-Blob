using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Logging;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Events;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline.Middlewares;
using ValiBlob.HealthChecks.Extensions;
using ValiBlob.Local.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// ─── ValiBlob setup ───────────────────────────────────────────────────────────
// All providers are registered as keyed services (key = provider name).
// IStorageFactory resolves the default provider by that name at runtime.
//
// BEST PRACTICE: Use StorageProviderType enum for type-safe provider selection
// instead of string literals to avoid typos and get compile-time verification.
builder.Services.AddValiBlob()
    // Type-safe provider selection via enum ✅
    .WithDefaultProvider(StorageProviderType.Local)
    .UseLocal(o =>
    {
        o.BasePath = Path.Combine(Path.GetTempPath(), "valiblob-sample");
        o.PublicBaseUrl = "http://localhost:5000/files";
        o.CreateIfNotExists = true;
    })
    .WithPipeline(pipeline => pipeline
        .UseValidation(o =>
        {
            o.MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB
            o.BlockedExtensions.Add(".exe");
            o.BlockedExtensions.Add(".bat");
        })
        .UseCompression(o =>
        {
            o.Enabled = true;
            o.MinSizeBytes = 1024;
        })
        // IMPROVEMENT: Content-type detection via magic bytes
        .Use<ContentTypeDetectionMiddleware>()
        // IMPROVEMENT: File deduplication via SHA-256 hashing
        .Use<DeduplicationMiddleware>()
    )
    .WithResumableUploads(o =>
    {
        o.DefaultChunkSizeBytes = 5 * 1024 * 1024; // 5 MB
        o.EnableChecksumValidation = true;
    })
    // IMPROVEMENT: Add resilience policies (retry, circuit breaker)
    .WithResiliencePolicies(o =>
    {
        o.RetryCount = 3;
        o.RetryDelay = TimeSpan.FromMilliseconds(100);
        o.CircuitBreakerThreshold = 50;
    });

// ─── Health checks ────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddValiBlob();

// ─── IMPROVEMENT: Event handlers for storage operations ────────────────────────
// Listen to upload/download/delete events for auditing, logging, notifications
builder.Services.AddValiBlob()
    .WithEventHandler<SampleStorageEventHandler>();

// ─── IMPROVEMENT: Decorators for observability ────────────────────────────────
// StorageTelemetryDecorator: Adds OpenTelemetry instrumentation
// StorageEventDecorator: Enables event dispatching
// These can be optionally applied to providers:
//   var provider = new StorageTelemetryDecorator(baseProvider);
//   var provider = new StorageEventDecorator(provider, eventDispatcher);

// .NET 9 built-in OpenAPI (no Swashbuckle package required)
builder.Services.AddOpenApi();

// ─── API Key authentication ───────────────────────────────────────────────────
// Read the key from configuration. In production: use secrets manager, not appsettings.
// Set ValiBlob:ApiKey via environment variable or user-secrets before running.
builder.Services.AddAuthentication("ApiKey")
    .AddScheme<ApiKeyAuthOptions, ApiKeyAuthHandler>("ApiKey", _ => { });
builder.Services.AddAuthorization();

var app = builder.Build();

app.MapOpenApi();
app.MapHealthChecks("/health");
app.UseAuthentication();
app.UseAuthorization();

// ─── Helper: resolve default provider via factory ────────────────────────────
// Providers are keyed services; use IStorageFactory to resolve by name.
static IStorageProvider GetProvider(IServiceProvider sp)
    => sp.GetRequiredService<IStorageFactory>().Create();

// ─── Upload ───────────────────────────────────────────────────────────────────
app.MapPost("/files/{*path}", async (
    string path,
    IFormFile file,
    IServiceProvider sp) =>
{
    var storage = GetProvider(sp);
    var request = new UploadRequest
    {
        Path = StoragePath.From(path).Sanitize(),
        Content = file.OpenReadStream(),
        ContentType = file.ContentType,
        ContentLength = file.Length
    };

    var result = await storage.UploadAsync(request);
    return result.IsSuccess
        ? Results.Ok(new { result.Value!.Path, result.Value.SizeBytes, result.Value.ETag })
        : Results.BadRequest(result.ErrorMessage);
})
.DisableAntiforgery()
.RequireAuthorization();

// ─── Download ─────────────────────────────────────────────────────────────────
app.MapGet("/files/{*path}", async (string path, IServiceProvider sp) =>
{
    var storage = GetProvider(sp);
    var result = await storage.DownloadAsync(new DownloadRequest { Path = path });
    if (!result.IsSuccess)
        return Results.NotFound(result.ErrorMessage);

    var meta = await storage.GetMetadataAsync(path);
    var contentType = meta.IsSuccess
        ? meta.Value!.ContentType ?? "application/octet-stream"
        : "application/octet-stream";
    return Results.Stream(result.Value!, contentType);
});

// ─── Delete ───────────────────────────────────────────────────────────────────
app.MapDelete("/files/{*path}", async (string path, IServiceProvider sp) =>
{
    var storage = GetProvider(sp);
    var result = await storage.DeleteAsync(path);
    return result.IsSuccess ? Results.NoContent() : Results.NotFound(result.ErrorMessage);
})
.RequireAuthorization();

// ─── Exists ───────────────────────────────────────────────────────────────────
// path passed as query string to avoid catch-all routing limitation
app.MapGet("/files/exists", async (string path, IServiceProvider sp) =>
{
    var storage = GetProvider(sp);
    var result = await storage.ExistsAsync(path);
    return result.IsSuccess
        ? Results.Ok(new { path, exists = result.Value })
        : Results.BadRequest(result.ErrorMessage);
})
.RequireAuthorization();

// ─── List files ───────────────────────────────────────────────────────────────
app.MapGet("/files", async (string? prefix, IServiceProvider sp) =>
{
    var storage = GetProvider(sp);
    var result = await storage.ListFilesAsync(prefix);
    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.BadRequest(result.ErrorMessage);
})
.RequireAuthorization();

// ─── Metadata ─────────────────────────────────────────────────────────────────
// path passed as query string to avoid catch-all routing limitation
app.MapGet("/files/metadata", async (string path, IServiceProvider sp) =>
{
    var storage = GetProvider(sp);
    var result = await storage.GetMetadataAsync(path);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.ErrorMessage);
})
.RequireAuthorization();

// ─── Copy ─────────────────────────────────────────────────────────────────────
app.MapPost("/files/copy", async (
    string sourcePath,
    string destination,
    IServiceProvider sp) =>
{
    var storage = GetProvider(sp);
    var result = await storage.CopyAsync(sourcePath, destination);
    return result.IsSuccess
        ? Results.Ok(new { source = sourcePath, destination })
        : Results.BadRequest(result.ErrorMessage);
})
.RequireAuthorization();

// ─── Multi-provider example using IStorageFactory ────────────────────────────
// Shows how to target a specific named provider at runtime.
app.MapGet("/providers", (IStorageFactory factory) =>
{
    var providers = factory.GetAll().Select(p => p.ProviderName).ToList();
    return Results.Ok(new { defaultProvider = "Local", registeredProviders = providers });
});

// ─── Presigned URL ────────────────────────────────────────────────────────────
app.MapGet("/files/presigned-download", async (string path, IServiceProvider sp) =>
{
    var storage = GetProvider(sp);
    if (storage is not IPresignedUrlProvider presigned)
        return Results.StatusCode(501);

    var result = await presigned.GetPresignedDownloadUrlAsync(path, TimeSpan.FromHours(1));
    return result.IsSuccess ? Results.Ok(new { url = result.Value }) : Results.BadRequest(result.ErrorMessage);
});

app.MapPost("/files/presigned-upload", async (string path, IServiceProvider sp) =>
{
    var storage = GetProvider(sp);
    if (storage is not IPresignedUrlProvider presigned)
        return Results.StatusCode(501);

    var result = await presigned.GetPresignedUploadUrlAsync(path, TimeSpan.FromHours(1));
    return result.IsSuccess ? Results.Ok(new { url = result.Value }) : Results.BadRequest(result.ErrorMessage);
});

// ─── Resumable uploads ────────────────────────────────────────────────────────
app.MapPost("/uploads/start", async (StartUploadRequest body, IServiceProvider sp) =>
{
    var storage = GetProvider(sp);
    if (storage is not IResumableUploadProvider resumable)
        return Results.StatusCode(501);

    var result = await resumable.StartResumableUploadAsync(new ResumableUploadRequest
    {
        Path = StoragePath.From(body.Path).Sanitize(),
        ContentType = body.ContentType,
        TotalSize = body.TotalSize
    });

    return result.IsSuccess
        ? Results.Ok(new { result.Value!.UploadId, result.Value.ExpiresAt })
        : Results.BadRequest(result.ErrorMessage);
});

app.MapPut("/uploads/{uploadId}/chunks/{offset:long}", async (
    string uploadId,
    long offset,
    IFormFile chunk,
    IServiceProvider sp) =>
{
    var storage = GetProvider(sp);
    if (storage is not IResumableUploadProvider resumable)
        return Results.StatusCode(501);

    var result = await resumable.UploadChunkAsync(new ResumableChunkRequest
    {
        UploadId = uploadId,
        Data = chunk.OpenReadStream(),
        Offset = offset,
        Length = chunk.Length   // long? — matches ResumableChunkRequest.Length
    });

    return result.IsSuccess
        ? Results.Ok(new { result.Value!.BytesUploaded, result.Value.TotalSize, result.Value.IsReadyToComplete })
        : Results.BadRequest(result.ErrorMessage);
})
.DisableAntiforgery();

app.MapGet("/uploads/{uploadId}/status", async (string uploadId, IServiceProvider sp) =>
{
    var storage = GetProvider(sp);
    if (storage is not IResumableUploadProvider resumable)
        return Results.StatusCode(501);

    var result = await resumable.GetUploadStatusAsync(uploadId);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.ErrorMessage);
});

app.MapPost("/uploads/{uploadId}/complete", async (string uploadId, IServiceProvider sp) =>
{
    var storage = GetProvider(sp);
    if (storage is not IResumableUploadProvider resumable)
        return Results.StatusCode(501);

    var result = await resumable.CompleteResumableUploadAsync(uploadId);
    return result.IsSuccess
        ? Results.Ok(new { result.Value!.Path, result.Value.SizeBytes, result.Value.ETag })
        : Results.BadRequest(result.ErrorMessage);
});

app.MapDelete("/uploads/{uploadId}", async (string uploadId, IServiceProvider sp) =>
{
    var storage = GetProvider(sp);
    if (storage is not IResumableUploadProvider resumable)
        return Results.StatusCode(501);

    await resumable.AbortResumableUploadAsync(uploadId);
    return Results.NoContent();
});

app.Run();

// ─── API Key auth handler ────────────────────────────────────────────────────
public sealed class ApiKeyAuthOptions : AuthenticationSchemeOptions { }

public sealed class ApiKeyAuthHandler : AuthenticationHandler<ApiKeyAuthOptions>
{
    private const string ApiKeyHeader = "X-Api-Key";

    public ApiKeyAuthHandler(
        IOptionsMonitor<ApiKeyAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var configuredKey = Context.RequestServices
            .GetRequiredService<IConfiguration>()["ValiBlob:ApiKey"];

        if (string.IsNullOrWhiteSpace(configuredKey))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey)
            || !string.Equals(configuredKey, providedKey, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid or missing X-Api-Key header."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "api-client")], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}

// ─── Request models ───────────────────────────────────────────────────────────
record StartUploadRequest(string Path, string ContentType, long TotalSize);

// ─── Sample event handler for storage operations ─────────────────────────────
/// <summary>
/// Example event handler demonstrating how to listen to storage events
/// for auditing, metrics collection, or external notifications.
/// </summary>
internal class SampleStorageEventHandler : IStorageEventHandler
{
    private readonly ILogger<SampleStorageEventHandler> _logger;

    public SampleStorageEventHandler(ILogger<SampleStorageEventHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>Fired when upload completes successfully.</summary>
    public Task OnUploadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Upload] {Provider} | Path: {Path} | Size: {Size} bytes | Duration: {Duration}ms",
            context.ProviderName,
            context.Path,
            context.FileSizeBytes ?? 0,
            context.Duration.TotalMilliseconds);
        return Task.CompletedTask;
    }

    /// <summary>Fired when upload fails.</summary>
    public Task OnUploadFailedAsync(StorageEventContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "[Upload Failed] {Provider} | Path: {Path} | Error: {Error}",
            context.ProviderName,
            context.Path,
            context.ErrorMessage);
        return Task.CompletedTask;
    }

    /// <summary>Fired when download completes successfully.</summary>
    public Task OnDownloadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Download] {Provider} | Path: {Path} | Duration: {Duration}ms",
            context.ProviderName,
            context.Path,
            context.Duration.TotalMilliseconds);
        return Task.CompletedTask;
    }

    /// <summary>Fired when delete completes successfully.</summary>
    public Task OnDeleteCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Delete] {Provider} | Path: {Path}",
            context.ProviderName,
            context.Path);
        return Task.CompletedTask;
    }
}
