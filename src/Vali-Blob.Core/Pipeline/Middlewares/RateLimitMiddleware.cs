using System.Collections.Concurrent;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Exceptions;
using ValiBlob.Core.Options;

namespace ValiBlob.Core.Pipeline.Middlewares;

public sealed class RateLimitMiddleware : IStorageMiddleware
{
    private readonly RateLimitOptions _options;
    // scope → timestamps of requests within the current window
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _windows = new();
    private readonly object _lock = new();

    public RateLimitMiddleware(RateLimitOptions options)
    {
        _options = options;
    }

    public async Task InvokeAsync(StoragePipelineContext context, StorageMiddlewareDelegate next)
    {
        if (!_options.Enabled)
        {
            await next(context);
            return;
        }

        var scope = _options.ScopeResolver?.Invoke(context.Request.BucketOverride)
                    ?? context.Request.BucketOverride
                    ?? "global";

        var now = DateTimeOffset.UtcNow;
        var windowStart = now - _options.Window;

        lock (_lock)
        {
            var queue = _windows.GetOrAdd(scope, _ => new Queue<DateTimeOffset>());

            // Evict timestamps outside the current window
            while (queue.Count > 0 && queue.Peek() < windowStart)
                queue.Dequeue();

            if (queue.Count >= _options.MaxRequestsPerWindow)
            {
                throw new StorageValidationException(new[]
                {
                    $"Rate limit exceeded for scope '{scope}': " +
                    $"max {_options.MaxRequestsPerWindow} requests per {_options.Window.TotalSeconds:0}s window."
                });
            }

            queue.Enqueue(now);
        }

        await next(context);
    }
}
