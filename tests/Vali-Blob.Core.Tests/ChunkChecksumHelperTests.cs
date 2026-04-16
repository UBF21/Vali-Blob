using System.Security.Cryptography;
using FluentAssertions;
using ValiBlob.Core.Resumable;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class ChunkChecksumHelperTests
{
    [Fact]
    public void ComputeMd5Base64_EmptyArray_ReturnsKnownHash()
    {
        var result = ChunkChecksumHelper.ComputeMd5Base64(Array.Empty<byte>());

        // MD5 of empty input is d41d8cd98f00b204e9800998ecf8427e — base64: 1B2M2Y8AsgTpgAmY7PhCfg==
        result.Should().Be("1B2M2Y8AsgTpgAmY7PhCfg==");
    }

    [Fact]
    public void ComputeMd5Base64_KnownData_ReturnsCorrectHash()
    {
        var data = System.Text.Encoding.UTF8.GetBytes("hello");

        var result = ChunkChecksumHelper.ComputeMd5Base64(data);

        // MD5 of "hello" is 5d41402abc4b2a76b9719d911017c592 — base64: XUFAKrxLKna5cZ2REBfFkg==
        result.Should().Be("XUFAKrxLKna5cZ2REBfFkg==");
    }

    [Fact]
    public void Validate_MatchingChecksums_ReturnsNull()
    {
        var hash = ChunkChecksumHelper.ComputeMd5Base64(System.Text.Encoding.UTF8.GetBytes("test-data"));

        var error = ChunkChecksumHelper.Validate(hash, hash);

        error.Should().BeNull();
    }

    [Fact]
    public void Validate_MismatchChecksums_ReturnsErrorMessage()
    {
        var actual = ChunkChecksumHelper.ComputeMd5Base64(System.Text.Encoding.UTF8.GetBytes("actual"));
        var expected = ChunkChecksumHelper.ComputeMd5Base64(System.Text.Encoding.UTF8.GetBytes("different"));

        var error = ChunkChecksumHelper.Validate(actual, expected);

        error.Should().NotBeNull();
        error.Should().Contain("mismatch");
    }

    [Fact]
    public void Validate_ErrorMessage_ContainsExpectedAndActual()
    {
        var actual = ChunkChecksumHelper.ComputeMd5Base64(System.Text.Encoding.UTF8.GetBytes("actual-bytes"));
        var expected = ChunkChecksumHelper.ComputeMd5Base64(System.Text.Encoding.UTF8.GetBytes("expected-bytes"));

        var error = ChunkChecksumHelper.Validate(actual, expected);

        error.Should().NotBeNull();
        error.Should().Contain(expected);
        error.Should().Contain(actual);
    }

    [Fact]
    public void Validate_CaseInsensitive_Matches()
    {
        var lower = "abcdef==";
        var upper = "ABCDEF==";

        var error = ChunkChecksumHelper.Validate(lower, upper);

        error.Should().BeNull();
    }

    [Fact]
    public void ComputeMd5Base64_SameInputTwice_ReturnsSameHash()
    {
        var data = new byte[256];
        new Random(99).NextBytes(data);

        var first = ChunkChecksumHelper.ComputeMd5Base64(data);
        var second = ChunkChecksumHelper.ComputeMd5Base64(data);

        first.Should().Be(second);
    }
}
