using System.IO;
using FluentAssertions;
using ValiBlob.Core.Models;
using Xunit;

namespace ValiBlob.Core.Tests;

// ─── StoragePath ──────────────────────────────────────────────────────────────

public sealed class StoragePathTests
{
    // ─── From factory ────────────────────────────────────────────────────────

    [Fact]
    public void From_SingleSegment_CreatesPath()
    {
        var path = StoragePath.From("file.txt");
        path.ToString().Should().Be("file.txt");
    }

    [Fact]
    public void From_MultipleSegments_JoinsWithSlash()
    {
        var path = StoragePath.From("folder", "sub", "file.txt");
        path.ToString().Should().Be("folder/sub/file.txt");
    }

    [Fact]
    public void From_PreJoinedString_SplitsCorrectly()
    {
        var path = StoragePath.From("folder/sub/file.txt");
        path.Segments.Should().Equal("folder", "sub", "file.txt");
    }

    [Fact]
    public void From_WithLeadingTrailingSlashes_Cleans()
    {
        var path = StoragePath.From("/folder/file.txt/");
        path.ToString().Should().Be("folder/file.txt");
    }

    [Fact]
    public void From_WithInternalSpaces_TrimsSegments()
    {
        var path = StoragePath.From("  folder  ", "  file.txt  ");
        path.ToString().Should().Be("folder/file.txt");
    }

    [Fact]
    public void From_WithNullSegments_ThrowsArgumentException()
    {
        var act = () => StoragePath.From(null!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void From_WithEmptySegmentsArray_ThrowsArgumentException()
    {
        var act = () => StoragePath.From([]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void From_WithAllEmptyStringSegments_ThrowsArgumentException()
    {
        var act = () => StoragePath.From("  ", "   ");
        act.Should().Throw<ArgumentException>().WithMessage("*empty*");
    }

    // ─── Append / operator ───────────────────────────────────────────────────

    [Fact]
    public void Append_AddsSegmentToPath()
    {
        var path = StoragePath.From("folder");
        var result = path.Append("file.txt");
        result.ToString().Should().Be("folder/file.txt");
    }

    [Fact]
    public void Append_ReturnsNewInstance()
    {
        var original = StoragePath.From("folder");
        var result = original.Append("file.txt");
        result.Should().NotBeSameAs(original);
        original.ToString().Should().Be("folder");
    }

    [Fact]
    public void OperatorSlash_AppendsSegment()
    {
        var path = StoragePath.From("folder");
        var result = path / "sub" / "file.txt";
        result.ToString().Should().Be("folder/sub/file.txt");
    }

    // ─── Parent ───────────────────────────────────────────────────────────────

    [Fact]
    public void Parent_WithMultipleSegments_ReturnsParent()
    {
        var path = StoragePath.From("folder", "sub", "file.txt");
        path.Parent!.ToString().Should().Be("folder/sub");
    }

    [Fact]
    public void Parent_WithTwoSegments_ReturnsSingleSegment()
    {
        var path = StoragePath.From("folder", "file.txt");
        path.Parent!.ToString().Should().Be("folder");
    }

    [Fact]
    public void Parent_WithSingleSegment_ReturnsNull()
    {
        var path = StoragePath.From("file.txt");
        path.Parent.Should().BeNull();
    }

    // ─── FileName ─────────────────────────────────────────────────────────────

    [Fact]
    public void FileName_ReturnsLastSegment()
    {
        var path = StoragePath.From("folder", "sub", "file.txt");
        path.FileName.Should().Be("file.txt");
    }

    [Fact]
    public void FileName_WithSingleSegment_ReturnsSelf()
    {
        var path = StoragePath.From("file.txt");
        path.FileName.Should().Be("file.txt");
    }

    // ─── Extension ───────────────────────────────────────────────────────────

    [Fact]
    public void Extension_WithDot_ReturnsExtensionWithDot()
    {
        var path = StoragePath.From("file.pdf");
        path.Extension.Should().Be(".pdf");
    }

    [Fact]
    public void Extension_WithMultipleDots_ReturnsLastExtension()
    {
        var path = StoragePath.From("archive.tar.gz");
        path.Extension.Should().Be(".gz");
    }

    [Fact]
    public void Extension_WithoutDot_ReturnsNull()
    {
        var path = StoragePath.From("Makefile");
        path.Extension.Should().BeNull();
    }

    // ─── Equality ─────────────────────────────────────────────────────────────

    [Fact]
    public void Equals_SameSegments_ReturnsTrue()
    {
        var a = StoragePath.From("folder", "file.txt");
        var b = StoragePath.From("folder", "file.txt");
        a.Equals(b).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentSegments_ReturnsFalse()
    {
        var a = StoragePath.From("folder", "file.txt");
        var b = StoragePath.From("other", "file.txt");
        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var path = StoragePath.From("file.txt");
        path.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void OperatorEquals_SameSegments_ReturnsTrue()
    {
        var a = StoragePath.From("folder/file.txt");
        var b = StoragePath.From("folder/file.txt");
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void OperatorNotEquals_DifferentPaths_ReturnsTrue()
    {
        var a = StoragePath.From("a.txt");
        var b = StoragePath.From("b.txt");
        (a != b).Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_SameSegments_ReturnsSameHash()
    {
        var a = StoragePath.From("folder/file.txt");
        var b = StoragePath.From("folder/file.txt");
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentSegments_ReturnsDifferentHash()
    {
        var a = StoragePath.From("file-a.txt");
        var b = StoragePath.From("file-b.txt");
        a.GetHashCode().Should().NotBe(b.GetHashCode());
    }

    // ─── Implicit conversions ────────────────────────────────────────────────

    [Fact]
    public void ImplicitToString_JoinsSegments()
    {
        StoragePath path = StoragePath.From("folder", "file.txt");
        string str = path;
        str.Should().Be("folder/file.txt");
    }

    [Fact]
    public void ImplicitFromString_SplitsOnSlash()
    {
        StoragePath path = "folder/sub/file.txt";
        path.Segments.Should().Equal("folder", "sub", "file.txt");
    }

    [Fact]
    public void ImplicitFromString_CanBeUsedInDictionary()
    {
        var dict = new Dictionary<StoragePath, string>
        {
            [StoragePath.From("key.txt")] = "value"
        };
        StoragePath lookup = StoragePath.From("key.txt");
        dict.ContainsKey(lookup).Should().BeTrue();
    }
}

// ─── UploadRequest ────────────────────────────────────────────────────────────

public sealed class UploadRequestTests
{
    private static UploadRequest CreateBase() => new()
    {
        Path = StoragePath.From("folder/file.txt"),
        Content = new MemoryStream([1, 2, 3]),
        ContentType = "application/octet-stream"
    };

    [Fact]
    public void WithContent_ReturnsNewInstance()
    {
        var original = CreateBase();
        var newContent = new MemoryStream([4, 5]);

        var result = original.WithContent(newContent);

        result.Should().NotBeSameAs(original);
        result.Content.Should().BeSameAs(newContent);
    }

    [Fact]
    public void WithContent_PreservesOtherFields()
    {
        var original = CreateBase();
        var result = original.WithContent(new MemoryStream());

        result.Path.Should().Be(original.Path);
        result.ContentType.Should().Be(original.ContentType);
    }

    [Fact]
    public void WithMetadata_ReturnsNewInstance()
    {
        var original = CreateBase();
        var meta = new Dictionary<string, string> { ["key"] = "val" };

        var result = original.WithMetadata(meta);

        result.Should().NotBeSameAs(original);
        result.Metadata.Should().BeSameAs(meta);
    }

    [Fact]
    public void WithMetadata_PreservesOtherFields()
    {
        var original = CreateBase();
        var result = original.WithMetadata(new Dictionary<string, string>());

        result.Path.Should().Be(original.Path);
        result.Content.Should().BeSameAs(original.Content);
    }

    [Fact]
    public void WithContentType_ReturnsNewInstance()
    {
        var original = CreateBase();

        var result = original.WithContentType("image/png");

        result.Should().NotBeSameAs(original);
        result.ContentType.Should().Be("image/png");
    }

    [Fact]
    public void WithContentType_PreservesOtherFields()
    {
        var original = CreateBase();
        var result = original.WithContentType("image/png");

        result.Path.Should().Be(original.Path);
        result.Content.Should().BeSameAs(original.Content);
    }

    [Fact]
    public void WithPath_ReturnsNewInstance()
    {
        var original = CreateBase();
        var newPath = StoragePath.From("new/path.txt");

        var result = original.WithPath(newPath);

        result.Should().NotBeSameAs(original);
        result.Path.Should().Be(newPath);
    }

    [Fact]
    public void WithPath_PreservesOtherFields()
    {
        var original = CreateBase();
        var result = original.WithPath(StoragePath.From("new.txt"));

        result.Content.Should().BeSameAs(original.Content);
        result.ContentType.Should().Be(original.ContentType);
    }

    [Fact]
    public void ChainedBuilders_BuildsCorrectObject()
    {
        var content = new MemoryStream([1]);
        var meta = new Dictionary<string, string> { ["author"] = "test" };

        var result = new UploadRequest { Path = StoragePath.From("base.txt"), Content = content }
            .WithContentType("text/plain")
            .WithMetadata(meta)
            .WithPath(StoragePath.From("final.txt"));

        result.ContentType.Should().Be("text/plain");
        result.Metadata.Should().BeSameAs(meta);
        result.Path.ToString().Should().Be("final.txt");
    }
}

// ─── StorageResult ────────────────────────────────────────────────────────────

public sealed class StorageResultTests
{
    // ─── Non-generic StorageResult ───────────────────────────────────────────

    [Fact]
    public void Success_IsSuccess()
    {
        var result = StorageResult.Success();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Success_HasNullErrorMessage()
    {
        var result = StorageResult.Success();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Failure_IsNotSuccess()
    {
        var result = StorageResult.Failure("Something failed");
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Failure_HasErrorMessage()
    {
        var result = StorageResult.Failure("Something failed");
        result.ErrorMessage.Should().Be("Something failed");
    }

    [Fact]
    public void Failure_WithDefaultCode_UsesProviderError()
    {
        var result = StorageResult.Failure("err");
        result.ErrorCode.Should().Be(StorageErrorCode.ProviderError);
    }

    [Fact]
    public void ImplicitBoolConversion_Success_ReturnsTrue()
    {
        bool value = StorageResult.Success();
        value.Should().BeTrue();
    }

    [Fact]
    public void ImplicitBoolConversion_Failure_ReturnsFalse()
    {
        bool value = StorageResult.Failure("error");
        value.Should().BeFalse();
    }

    // ─── Generic StorageResult<T> ────────────────────────────────────────────

    [Fact]
    public void Generic_Success_HasValue()
    {
        var result = StorageResult<int>.Success(42);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Generic_Failure_HasNoValue()
    {
        var result = StorageResult<int>.Failure("error");
        result.IsSuccess.Should().BeFalse();
        result.Value.Should().Be(default);
    }

    [Fact]
    public void Generic_Failure_HasErrorMessage()
    {
        var result = StorageResult<string>.Failure("Something went wrong");
        result.ErrorMessage.Should().Be("Something went wrong");
    }

    [Fact]
    public void Generic_ImplicitBoolConversion_Success_ReturnsTrue()
    {
        bool value = StorageResult<string>.Success("hello");
        value.Should().BeTrue();
    }

    [Fact]
    public void Generic_ImplicitBoolConversion_Failure_ReturnsFalse()
    {
        bool value = StorageResult<string>.Failure("err");
        value.Should().BeFalse();
    }

    [Fact]
    public void Map_OnSuccess_TransformsValue()
    {
        var result = StorageResult<int>.Success(5);
        var mapped = result.Map(x => x * 2);
        mapped.IsSuccess.Should().BeTrue();
        mapped.Value.Should().Be(10);
    }

    [Fact]
    public void Map_OnFailure_PropagatesError()
    {
        var result = StorageResult<int>.Failure("original error");
        var mapped = result.Map(x => x * 2);
        mapped.IsSuccess.Should().BeFalse();
        mapped.ErrorMessage.Should().Be("original error");
    }

    [Fact]
    public void Failure_WithException_StoresException()
    {
        var ex = new InvalidOperationException("boom");
        var result = StorageResult.Failure("err", ex: ex);
        result.Exception.Should().BeSameAs(ex);
    }
}
