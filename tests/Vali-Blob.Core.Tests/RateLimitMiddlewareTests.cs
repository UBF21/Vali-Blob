using FluentAssertions;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Exceptions;
using ValiBlob.Core.Models;
using ValiBlob.Core.Options;
using ValiBlob.Core.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using ValiBlob.Core.DependencyInjection;
using ValiBlob.Core.Pipeline.Middlewares;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class RateLimitMiddlewareTests
{
    private static RateLimitMiddleware Make(int max, TimeSpan? window = null) =>
        new(new RateLimitOptions
        {
            Enabled = true,
            MaxRequestsPerWindow = max,
            Window = window ?? TimeSpan.FromMinutes(1)
        });

    private static StoragePipelineContext MakeContext(string? bucket = null) =>
        new(new UploadRequest
        {
            Path = StoragePath.From("file.txt"),
            Content = new MemoryStream(),
            BucketOverride = bucket
        });

    private static StorageMiddlewareDelegate NoopNext =>
        _ => Task.CompletedTask;

    [Fact]
    public async Task InvokeAsync_WhenDisabled_AlwaysPasses()
    {
        var middleware = new RateLimitMiddleware(new RateLimitOptions { Enabled = false, MaxRequestsPerWindow = 1 });
        var ctx = MakeContext();

        // Should not throw even on repeated calls beyond the limit
        for (var i = 0; i < 10; i++)
            await middleware.InvokeAsync(ctx, NoopNext);
    }

    [Fact]
    public async Task InvokeAsync_WithinLimit_PassesThrough()
    {
        var middleware = Make(max: 5);
        var ctx = MakeContext();

        var act = async () =>
        {
            for (var i = 0; i < 5; i++)
                await middleware.InvokeAsync(ctx, NoopNext);
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InvokeAsync_ExceedingLimit_ThrowsStorageValidationException()
    {
        var middleware = Make(max: 3);
        var ctx = MakeContext();

        for (var i = 0; i < 3; i++)
            await middleware.InvokeAsync(ctx, NoopNext);

        var act = async () => await middleware.InvokeAsync(ctx, NoopNext);

        await act.Should().ThrowAsync<StorageValidationException>()
            .WithMessage("*Rate limit exceeded*");
    }

    [Fact]
    public async Task InvokeAsync_DifferentScopes_TrackSeparately()
    {
        var middleware = Make(max: 2);

        // scope A uses up its limit
        var ctxA = MakeContext("bucket-a");
        await middleware.InvokeAsync(ctxA, NoopNext);
        await middleware.InvokeAsync(ctxA, NoopNext);

        // scope B still has capacity
        var ctxB = MakeContext("bucket-b");
        var act = async () => await middleware.InvokeAsync(ctxB, NoopNext);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InvokeAsync_AfterWindowExpires_ResetsCounter()
    {
        var middleware = Make(max: 2, window: TimeSpan.FromMilliseconds(50));
        var ctx = MakeContext();

        await middleware.InvokeAsync(ctx, NoopNext);
        await middleware.InvokeAsync(ctx, NoopNext);

        // Wait for window to expire
        await Task.Delay(100);

        // Counter should reset — no exception
        var act = async () => await middleware.InvokeAsync(ctx, NoopNext);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InvokeAsync_ErrorMessageContainsScope()
    {
        var middleware = Make(max: 1);
        var ctx = MakeContext("my-bucket");

        await middleware.InvokeAsync(ctx, NoopNext);

        var act = async () => await middleware.InvokeAsync(ctx, NoopNext);

        await act.Should().ThrowAsync<StorageValidationException>()
            .WithMessage("*my-bucket*");
    }

    [Fact]
    public async Task InvokeAsync_NoBucket_UsesGlobalScope()
    {
        var middleware = Make(max: 1);
        var ctx = MakeContext(null);

        await middleware.InvokeAsync(ctx, NoopNext);

        var act = async () => await middleware.InvokeAsync(ctx, NoopNext);

        await act.Should().ThrowAsync<StorageValidationException>()
            .WithMessage("*global*");
    }

    [Fact]
    public void WithRateLimit_RegistersMiddlewareInPipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        services.AddValiBlob()
            .WithRateLimit(o =>
            {
                o.MaxRequestsPerWindow = 50;
                o.Window = TimeSpan.FromSeconds(30);
            });

        var sp = services.BuildServiceProvider();
        var middlewares = sp.GetServices<IStorageMiddleware>();

        middlewares.Should().ContainItemsAssignableTo<RateLimitMiddleware>();
    }
}
