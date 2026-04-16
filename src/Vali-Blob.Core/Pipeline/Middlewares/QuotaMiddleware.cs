using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Exceptions;
using ValiBlob.Core.Options;

namespace ValiBlob.Core.Pipeline.Middlewares;

public sealed class QuotaMiddleware : IStorageMiddleware
{
    private readonly IStorageQuotaService _quotaService;
    private readonly QuotaOptions _options;

    public QuotaMiddleware(IStorageQuotaService quotaService, QuotaOptions options)
    {
        _quotaService = quotaService;
        _options = options;
    }

    public async Task InvokeAsync(StoragePipelineContext context, StorageMiddlewareDelegate next)
    {
        if (!_options.Enabled)
        {
            await next(context);
            return;
        }

        var scope = _options.ScopeResolver?.Invoke(context.Request)
                    ?? context.Request.BucketOverride
                    ?? "default";

        var limit = await _quotaService.GetQuotaLimitAsync(scope).ConfigureAwait(false);
        if (limit.HasValue && context.Request.ContentLength.HasValue)
        {
            var used = await _quotaService.GetUsedBytesAsync(scope).ConfigureAwait(false);
            if (used + context.Request.ContentLength.Value > limit.Value)
            {
                context.IsCancelled = true;
                throw new StorageValidationException(new[]
                {
                    $"Storage quota exceeded for scope '{scope}'. Used: {used:N0} bytes, Limit: {limit.Value:N0} bytes."
                });
            }
        }

        await next(context);
    }
}
