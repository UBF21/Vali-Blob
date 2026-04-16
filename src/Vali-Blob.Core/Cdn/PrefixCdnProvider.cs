using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Options;

namespace ValiBlob.Core.Cdn;

/// <summary>Simple CDN provider that maps storage paths to CDN URLs by replacing a base URL prefix.</summary>
public sealed class PrefixCdnProvider : ICdnProvider
{
    private readonly CdnOptions _options;

    public PrefixCdnProvider(IOptions<CdnOptions> options) => _options = options.Value;

    public string GetCdnUrl(string storagePath)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var path = storagePath.TrimStart('/');
        return $"{baseUrl}/{path}";
    }

    public Task InvalidateCacheAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        // No-op for prefix provider — override for real CDN implementations (CloudFront, Fastly, etc.)
        return Task.CompletedTask;
    }
}
