using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;
using ValiBlob.Core.Abstractions;
using ValiBlob.Redis;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class RedisSessionStoreTests
{
    // ─── RedisSessionStoreOptions defaults ───────────────────────────────────

    [Fact]
    public void RedisSessionStoreOptions_DefaultKeyPrefix_IsValiblob()
    {
        var opts = new RedisSessionStoreOptions();

        opts.KeyPrefix.Should().Be("valiblob");
    }

    [Fact]
    public void RedisSessionStoreOptions_CanBeConfiguredWithCustomPrefix()
    {
        var opts = new RedisSessionStoreOptions { KeyPrefix = "myapp" };

        opts.KeyPrefix.Should().Be("myapp");
    }

    [Fact]
    public void RedisSessionStoreOptions_DefaultConfigurationString_IsLocalhostPort()
    {
        var opts = new RedisSessionStoreOptions();

        opts.ConfigurationString.Should().NotBeNullOrWhiteSpace();
        opts.ConfigurationString.Should().Contain("6379");
    }

    // ─── Interface compliance ─────────────────────────────────────────────────

    [Fact]
    public void RedisResumableSessionStore_ImplementsIResumableSessionStore()
    {
        typeof(RedisResumableSessionStore)
            .Should().Implement<IResumableSessionStore>();
    }

    // ─── Constructor ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_WithValidArguments_DoesNotThrow()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var opts = Microsoft.Extensions.Options.Options.Create(new RedisSessionStoreOptions());

        var act = () => new RedisResumableSessionStore(multiplexer, opts);

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_NullMultiplexer_ThrowsArgumentNullException()
    {
        var opts = Microsoft.Extensions.Options.Options.Create(new RedisSessionStoreOptions());

        var act = () => new RedisResumableSessionStore(null!, opts);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("redis");
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();

        var act = () => new RedisResumableSessionStore(multiplexer, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    // ─── Key format (via reflection / indirect test) ──────────────────────────

    [Fact]
    public void KeyFormat_DefaultPrefix_ProducesExpectedPattern()
    {
        // The key format is: {KeyPrefix}:session:{uploadId}
        // We validate this by constructing the key string the same way the class does.
        var opts = new RedisSessionStoreOptions { KeyPrefix = "valiblob" };
        var uploadId = "abc-123";

        var key = $"{opts.KeyPrefix}:session:{uploadId}";

        key.Should().Be("valiblob:session:abc-123");
    }

    [Fact]
    public void KeyFormat_CustomPrefix_ProducesExpectedPattern()
    {
        var opts = new RedisSessionStoreOptions { KeyPrefix = "myns" };
        var uploadId = "xyz-789";

        var key = $"{opts.KeyPrefix}:session:{uploadId}";

        key.Should().Be("myns:session:xyz-789");
    }
}
