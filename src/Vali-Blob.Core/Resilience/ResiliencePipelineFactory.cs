using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using ValiBlob.Core.Options;

namespace ValiBlob.Core.Resilience;

internal static class ResiliencePipelineFactory
{
    internal static ResiliencePipeline BuildPipeline(
        ResilienceOptions options,
        ILogger logger,
        string providerName)
    {
        var builder = new ResiliencePipelineBuilder();

        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = options.Timeout
        });

        builder.AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = options.RetryCount,
            Delay = options.RetryDelay,
            UseJitter = true,
            BackoffType = options.UseExponentialBackoff
                ? DelayBackoffType.Exponential
                : DelayBackoffType.Constant,
            OnRetry = args =>
            {
                logger.LogWarning("[{Provider}] Retry attempt {Attempt} after {Delay}ms",
                    providerName, args.AttemptNumber, args.RetryDelay.TotalMilliseconds);
                return default;
            }
        });

        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = options.CircuitBreakerThreshold,
            BreakDuration = options.CircuitBreakerDuration,
            OnOpened = args =>
            {
                logger.LogError("[{Provider}] Circuit breaker opened for {Duration}s",
                    providerName, args.BreakDuration.TotalSeconds);
                return default;
            }
        });

        return builder.Build();
    }
}
