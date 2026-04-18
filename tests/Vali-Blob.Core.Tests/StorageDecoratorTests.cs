using System.IO;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Events;
using ValiBlob.Core.Models;
using ValiBlob.Core.Providers;
using Xunit;

namespace ValiBlob.Core.Tests;

// ─── StorageTelemetryDecorator ────────────────────────────────────────────────

public sealed class StorageTelemetryDecoratorTests
{
    private readonly IStorageProvider _inner = Substitute.For<IStorageProvider>();

    public StorageTelemetryDecoratorTests()
    {
        _inner.ProviderName.Returns("InMemory");
    }

    [Fact]
    public void Constructor_WithNullInner_ThrowsArgumentNullException()
    {
        var act = () => new StorageTelemetryDecorator(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("inner");
    }

    [Fact]
    public void ProviderName_DelegatesToInner()
    {
        var decorator = new StorageTelemetryDecorator(_inner);
        decorator.ProviderName.Should().Be("InMemory");
    }

    [Fact]
    public async Task DownloadAsync_WhenInnerSucceeds_ReturnsSameResult()
    {
        var stream = new MemoryStream([1, 2, 3]);
        var request = new DownloadRequest { Path = "test/file.txt" };
        _inner.DownloadAsync(request, Arg.Any<CancellationToken>())
            .Returns(StorageResult<Stream>.Success(stream));

        var decorator = new StorageTelemetryDecorator(_inner);
        var result = await decorator.DownloadAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(stream);
    }

    [Fact]
    public async Task DownloadAsync_WhenInnerFails_ReturnsSameFailure()
    {
        var request = new DownloadRequest { Path = "missing/file.txt" };
        _inner.DownloadAsync(request, Arg.Any<CancellationToken>())
            .Returns(StorageResult<Stream>.Failure("Not found"));

        var decorator = new StorageTelemetryDecorator(_inner);
        var result = await decorator.DownloadAsync(request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Not found");
    }

    [Fact]
    public async Task DownloadAsync_WhenInnerThrows_PropagatesException()
    {
        var request = new DownloadRequest { Path = "test/file.txt" };
        _inner.DownloadAsync(request, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Storage error"));

        var decorator = new StorageTelemetryDecorator(_inner);
        var act = async () => await decorator.DownloadAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Storage error");
    }

    [Fact]
    public async Task DownloadAsync_WhenCancelled_PropagatesOperationCanceledException()
    {
        var request = new DownloadRequest { Path = "test/file.txt" };
        var cts = new CancellationTokenSource();
        _inner.DownloadAsync(request, Arg.Any<CancellationToken>())
            .Throws(new OperationCanceledException());

        var decorator = new StorageTelemetryDecorator(_inner);
        var act = async () => await decorator.DownloadAsync(request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task UploadAsync_WhenInnerSucceeds_ReturnsSameResult()
    {
        var request = new UploadRequest { Path = "folder/file.txt", Content = new MemoryStream([1]) };
        var uploadResult = new UploadResult { Path = "folder/file.txt" };
        _inner.UploadAsync(request, null, Arg.Any<CancellationToken>())
            .Returns(StorageResult<UploadResult>.Success(uploadResult));

        var decorator = new StorageTelemetryDecorator(_inner);
        var result = await decorator.UploadAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(uploadResult);
    }

    [Fact]
    public async Task UploadAsync_WhenInnerFails_ReturnsSameFailure()
    {
        var request = new UploadRequest { Path = "folder/file.txt", Content = new MemoryStream() };
        _inner.UploadAsync(request, null, Arg.Any<CancellationToken>())
            .Returns(StorageResult<UploadResult>.Failure("Upload failed"));

        var decorator = new StorageTelemetryDecorator(_inner);
        var result = await decorator.UploadAsync(request);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Upload failed");
    }

    [Fact]
    public async Task DeleteAsync_WhenInnerSucceeds_ReturnsSameResult()
    {
        _inner.DeleteAsync("test/file.txt", Arg.Any<CancellationToken>())
            .Returns(StorageResult.Success());

        var decorator = new StorageTelemetryDecorator(_inner);
        var result = await decorator.DeleteAsync("test/file.txt");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_WhenInnerFails_ReturnsSameFailure()
    {
        _inner.DeleteAsync("test/file.txt", Arg.Any<CancellationToken>())
            .Returns(StorageResult.Failure("Delete failed"));

        var decorator = new StorageTelemetryDecorator(_inner);
        var result = await decorator.DeleteAsync("test/file.txt");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_DelegatesToInner()
    {
        _inner.ExistsAsync("file.txt", Arg.Any<CancellationToken>())
            .Returns(StorageResult<bool>.Success(true));

        var decorator = new StorageTelemetryDecorator(_inner);
        var result = await decorator.ExistsAsync("file.txt");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        await _inner.Received(1).ExistsAsync("file.txt", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetUrlAsync_DelegatesToInner()
    {
        _inner.GetUrlAsync("file.txt", Arg.Any<CancellationToken>())
            .Returns(StorageResult<string>.Success("https://example.com/file.txt"));

        var decorator = new StorageTelemetryDecorator(_inner);
        var result = await decorator.GetUrlAsync("file.txt");

        result.Value.Should().Be("https://example.com/file.txt");
        await _inner.Received(1).GetUrlAsync("file.txt", Arg.Any<CancellationToken>());
    }
}

// ─── StorageEventDecorator ────────────────────────────────────────────────────

public sealed class StorageEventDecoratorTests
{
    private readonly IStorageProvider _inner = Substitute.For<IStorageProvider>();
    private readonly IStorageEventDispatcher _dispatcher = Substitute.For<IStorageEventDispatcher>();

    public StorageEventDecoratorTests()
    {
        _inner.ProviderName.Returns("InMemory");
    }

    [Fact]
    public void Constructor_WithNullInner_ThrowsArgumentNullException()
    {
        var act = () => new StorageEventDecorator(null!, _dispatcher);
        act.Should().Throw<ArgumentNullException>().WithParameterName("inner");
    }

    [Fact]
    public void Constructor_WithNullDispatcher_ThrowsArgumentNullException()
    {
        var act = () => new StorageEventDecorator(_inner, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dispatcher");
    }

    [Fact]
    public void ProviderName_DelegatesToInner()
    {
        var decorator = new StorageEventDecorator(_inner, _dispatcher);
        decorator.ProviderName.Should().Be("InMemory");
    }

    [Fact]
    public async Task UploadAsync_WhenSucceeds_DispatchesUploadCompleted()
    {
        var request = new UploadRequest { Path = "folder/file.txt", Content = new MemoryStream([1]) };
        _inner.UploadAsync(request, null, Arg.Any<CancellationToken>())
            .Returns(StorageResult<UploadResult>.Success(new UploadResult { Path = request.Path }));

        var decorator = new StorageEventDecorator(_inner, _dispatcher);
        await decorator.UploadAsync(request);

        await _dispatcher.Received(1).DispatchUploadCompletedAsync(
            Arg.Is<StorageEventContext>(ctx =>
                ctx.Path == "folder/file.txt" &&
                ctx.IsSuccess == true &&
                ctx.OperationType == "Upload"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadAsync_WhenFails_DispatchesUploadFailed()
    {
        var request = new UploadRequest { Path = "folder/file.txt", Content = new MemoryStream() };
        _inner.UploadAsync(request, null, Arg.Any<CancellationToken>())
            .Returns(StorageResult<UploadResult>.Failure("Upload error"));

        var decorator = new StorageEventDecorator(_inner, _dispatcher);
        await decorator.UploadAsync(request);

        await _dispatcher.Received(1).DispatchUploadFailedAsync(
            Arg.Is<StorageEventContext>(ctx =>
                ctx.Path == "folder/file.txt" &&
                ctx.IsSuccess == false &&
                ctx.ErrorMessage == "Upload error"),
            Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().DispatchUploadCompletedAsync(Arg.Any<StorageEventContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadAsync_WhenSucceeds_DispatchesDownloadCompleted()
    {
        var request = new DownloadRequest { Path = "folder/file.txt" };
        _inner.DownloadAsync(request, Arg.Any<CancellationToken>())
            .Returns(StorageResult<Stream>.Success(new MemoryStream()));

        var decorator = new StorageEventDecorator(_inner, _dispatcher);
        await decorator.DownloadAsync(request);

        await _dispatcher.Received(1).DispatchDownloadCompletedAsync(
            Arg.Is<StorageEventContext>(ctx =>
                ctx.Path == "folder/file.txt" &&
                ctx.IsSuccess == true &&
                ctx.OperationType == "Download"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadAsync_WhenFails_DoesNotDispatchEvent()
    {
        var request = new DownloadRequest { Path = "missing/file.txt" };
        _inner.DownloadAsync(request, Arg.Any<CancellationToken>())
            .Returns(StorageResult<Stream>.Failure("Not found"));

        var decorator = new StorageEventDecorator(_inner, _dispatcher);
        await decorator.DownloadAsync(request);

        await _dispatcher.DidNotReceive().DispatchDownloadCompletedAsync(Arg.Any<StorageEventContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_WhenSucceeds_DispatchesDeleteCompleted()
    {
        _inner.DeleteAsync("file.txt", Arg.Any<CancellationToken>())
            .Returns(StorageResult.Success());

        var decorator = new StorageEventDecorator(_inner, _dispatcher);
        await decorator.DeleteAsync("file.txt");

        await _dispatcher.Received(1).DispatchDeleteCompletedAsync(
            Arg.Is<StorageEventContext>(ctx =>
                ctx.Path == "file.txt" &&
                ctx.IsSuccess == true &&
                ctx.OperationType == "Delete"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_WhenFails_DoesNotDispatchEvent()
    {
        _inner.DeleteAsync("file.txt", Arg.Any<CancellationToken>())
            .Returns(StorageResult.Failure("Delete error"));

        var decorator = new StorageEventDecorator(_inner, _dispatcher);
        await decorator.DeleteAsync("file.txt");

        await _dispatcher.DidNotReceive().DispatchDeleteCompletedAsync(Arg.Any<StorageEventContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExistsAsync_DelegatesToInnerWithoutDispatching()
    {
        _inner.ExistsAsync("file.txt", Arg.Any<CancellationToken>())
            .Returns(StorageResult<bool>.Success(true));

        var decorator = new StorageEventDecorator(_inner, _dispatcher);
        await decorator.ExistsAsync("file.txt");

        await _inner.Received(1).ExistsAsync("file.txt", Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().DispatchUploadCompletedAsync(Arg.Any<StorageEventContext>(), Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().DispatchDownloadCompletedAsync(Arg.Any<StorageEventContext>(), Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().DispatchDeleteCompletedAsync(Arg.Any<StorageEventContext>(), Arg.Any<CancellationToken>());
    }
}
