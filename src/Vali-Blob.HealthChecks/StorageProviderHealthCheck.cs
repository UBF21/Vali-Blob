using Microsoft.Extensions.Diagnostics.HealthChecks;
using ValiBlob.Core.Abstractions;

namespace ValiBlob.HealthChecks;

public sealed class StorageProviderHealthCheck : IHealthCheck
{
    private readonly IStorageProvider _provider;
    private readonly StorageHealthCheckOptions _options;

    public StorageProviderHealthCheck(IStorageProvider provider, StorageHealthCheckOptions options)
    {
        _provider = provider;
        _options = options;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check by trying to list files with a probe prefix — lightweight operation
            var result = await _provider.ListFilesAsync(
                _options.ProbePrefix,
                new Core.Models.ListOptions { MaxResults = 1 },
                cancellationToken);

            if (result.IsSuccess)
            {
                return HealthCheckResult.Healthy(
                    $"Storage provider '{_provider.ProviderName}' is reachable.",
                    new Dictionary<string, object> { ["provider"] = _provider.ProviderName });
            }

            return HealthCheckResult.Degraded(
                $"Storage provider '{_provider.ProviderName}' returned: {result.ErrorMessage}",
                data: new Dictionary<string, object>
                {
                    ["provider"] = _provider.ProviderName,
                    ["errorCode"] = result.ErrorCode.ToString()
                });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"Storage provider '{_provider.ProviderName}' threw an exception.",
                ex,
                new Dictionary<string, object> { ["provider"] = _provider.ProviderName });
        }
    }
}
