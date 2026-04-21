using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ValiBlob.Core.Events;
using Xunit;

namespace ValiBlob.Core.Tests;

public sealed class StorageEventDispatcherTests
{
    private static StorageEventDispatcher CreateDispatcher(
        IEnumerable<IStorageEventHandler>? handlers = null,
        ILogger<StorageEventDispatcher>? logger = null)
    {
        return new StorageEventDispatcher(
            handlers ?? [],
            logger ?? Substitute.For<ILogger<StorageEventDispatcher>>());
    }

    private static StorageEventContext CreateContext(string path = "test/file.txt") => new()
    {
        Path = path,
        ProviderName = "InMemory",
        OperationType = "Upload",
        IsSuccess = true
    };

    // ─── No handlers ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchUploadCompletedAsync_WithNoHandlers_DoesNotThrow()
    {
        var dispatcher = CreateDispatcher();
        var act = async () => await dispatcher.DispatchUploadCompletedAsync(CreateContext());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DispatchUploadFailedAsync_WithNoHandlers_DoesNotThrow()
    {
        var dispatcher = CreateDispatcher();
        var act = async () => await dispatcher.DispatchUploadFailedAsync(CreateContext());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DispatchDownloadCompletedAsync_WithNoHandlers_DoesNotThrow()
    {
        var dispatcher = CreateDispatcher();
        var act = async () => await dispatcher.DispatchDownloadCompletedAsync(CreateContext());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DispatchDeleteCompletedAsync_WithNoHandlers_DoesNotThrow()
    {
        var dispatcher = CreateDispatcher();
        var act = async () => await dispatcher.DispatchDeleteCompletedAsync(CreateContext());
        await act.Should().NotThrowAsync();
    }

    // ─── Single handler dispatches ────────────────────────────────────────────

    [Fact]
    public async Task DispatchUploadCompletedAsync_InvokesHandlerOnUploadCompleted()
    {
        var handler = Substitute.For<IStorageEventHandler>();
        var ctx = CreateContext();
        var dispatcher = CreateDispatcher([handler]);

        await dispatcher.DispatchUploadCompletedAsync(ctx);

        await handler.Received(1).OnUploadCompletedAsync(ctx, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchUploadFailedAsync_InvokesHandlerOnUploadFailed()
    {
        var handler = Substitute.For<IStorageEventHandler>();
        var ctx = CreateContext();
        var dispatcher = CreateDispatcher([handler]);

        await dispatcher.DispatchUploadFailedAsync(ctx);

        await handler.Received(1).OnUploadFailedAsync(ctx, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchDownloadCompletedAsync_InvokesHandlerOnDownloadCompleted()
    {
        var handler = Substitute.For<IStorageEventHandler>();
        var ctx = CreateContext();
        var dispatcher = CreateDispatcher([handler]);

        await dispatcher.DispatchDownloadCompletedAsync(ctx);

        await handler.Received(1).OnDownloadCompletedAsync(ctx, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchDeleteCompletedAsync_InvokesHandlerOnDeleteCompleted()
    {
        var handler = Substitute.For<IStorageEventHandler>();
        var ctx = CreateContext();
        var dispatcher = CreateDispatcher([handler]);

        await dispatcher.DispatchDeleteCompletedAsync(ctx);

        await handler.Received(1).OnDeleteCompletedAsync(ctx, Arg.Any<CancellationToken>());
    }

    // ─── Multiple handlers ─────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchUploadCompletedAsync_WithMultipleHandlers_InvokesAll()
    {
        var handler1 = Substitute.For<IStorageEventHandler>();
        var handler2 = Substitute.For<IStorageEventHandler>();
        var handler3 = Substitute.For<IStorageEventHandler>();
        var ctx = CreateContext();
        var dispatcher = CreateDispatcher([handler1, handler2, handler3]);

        await dispatcher.DispatchUploadCompletedAsync(ctx);

        await handler1.Received(1).OnUploadCompletedAsync(ctx, Arg.Any<CancellationToken>());
        await handler2.Received(1).OnUploadCompletedAsync(ctx, Arg.Any<CancellationToken>());
        await handler3.Received(1).OnUploadCompletedAsync(ctx, Arg.Any<CancellationToken>());
    }

    // ─── Handler isolation (fire-and-observe) ─────────────────────────────────

    [Fact]
    public async Task DispatchUploadCompletedAsync_WhenHandlerThrows_DoesNotPropagate()
    {
        var throwingHandler = Substitute.For<IStorageEventHandler>();
        throwingHandler.OnUploadCompletedAsync(Arg.Any<StorageEventContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Handler failed"));

        var dispatcher = CreateDispatcher([throwingHandler]);
        var act = async () => await dispatcher.DispatchUploadCompletedAsync(CreateContext());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DispatchUploadCompletedAsync_WhenOneHandlerThrows_OtherHandlersStillInvoked()
    {
        var throwingHandler = Substitute.For<IStorageEventHandler>();
        throwingHandler.OnUploadCompletedAsync(Arg.Any<StorageEventContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Handler failed"));

        var goodHandler = Substitute.For<IStorageEventHandler>();
        var ctx = CreateContext();
        var dispatcher = CreateDispatcher([throwingHandler, goodHandler]);

        await dispatcher.DispatchUploadCompletedAsync(ctx);

        await goodHandler.Received(1).OnUploadCompletedAsync(ctx, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_WhenHandlerThrows_LogsError()
    {
        var throwingHandler = Substitute.For<IStorageEventHandler>();
        throwingHandler.OnUploadCompletedAsync(Arg.Any<StorageEventContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Boom"));

        var logger = Substitute.For<ILogger<StorageEventDispatcher>>();
        var dispatcher = CreateDispatcher([throwingHandler], logger);

        await dispatcher.DispatchUploadCompletedAsync(CreateContext());

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<InvalidOperationException>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}

// ─── StorageEventContext ──────────────────────────────────────────────────────

public sealed class StorageEventContextTests
{
    [Fact]
    public void Context_WithRequiredProperties_IsCreated()
    {
        var ctx = new StorageEventContext
        {
            ProviderName = "AWS",
            OperationType = "Upload"
        };

        ctx.ProviderName.Should().Be("AWS");
        ctx.OperationType.Should().Be("Upload");
    }

    [Fact]
    public void Context_DefaultIsSuccess_IsFalse()
    {
        var ctx = new StorageEventContext { ProviderName = "Local", OperationType = "Delete" };
        ctx.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Context_DefaultDuration_IsZero()
    {
        var ctx = new StorageEventContext { ProviderName = "Local", OperationType = "Upload" };
        ctx.Duration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Context_DefaultExtra_IsEmptyDictionary()
    {
        var ctx = new StorageEventContext { ProviderName = "Local", OperationType = "Upload" };
        ctx.Extra.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Context_DefaultPath_IsNull()
    {
        var ctx = new StorageEventContext { ProviderName = "Local", OperationType = "Upload" };
        ctx.Path.Should().BeNull();
    }

    [Fact]
    public void Context_DefaultErrorMessage_IsNull()
    {
        var ctx = new StorageEventContext { ProviderName = "Local", OperationType = "Upload" };
        ctx.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Context_DefaultFileSizeBytes_IsNull()
    {
        var ctx = new StorageEventContext { ProviderName = "Local", OperationType = "Upload" };
        ctx.FileSizeBytes.Should().BeNull();
    }

    [Fact]
    public void Context_WithAllProperties_StoresCorrectly()
    {
        var extra = new Dictionary<string, object> { ["key"] = "value" };
        var ctx = new StorageEventContext
        {
            ProviderName = "Azure",
            OperationType = "Download",
            Path = "folder/file.txt",
            IsSuccess = true,
            ErrorMessage = null,
            Duration = TimeSpan.FromMilliseconds(120),
            FileSizeBytes = 4096L,
            Extra = extra
        };

        ctx.ProviderName.Should().Be("Azure");
        ctx.OperationType.Should().Be("Download");
        ctx.Path.Should().Be("folder/file.txt");
        ctx.IsSuccess.Should().BeTrue();
        ctx.Duration.Should().Be(TimeSpan.FromMilliseconds(120));
        ctx.FileSizeBytes.Should().Be(4096L);
        ctx.Extra.Should().ContainKey("key");
    }
}

// ─── StorageEventHandlerBase ──────────────────────────────────────────────────

public sealed class StorageEventHandlerBaseTests
{
    private sealed class ConcreteHandler : StorageEventHandlerBase { }

    private sealed class OverridingHandler : StorageEventHandlerBase
    {
        public bool UploadCompletedCalled { get; private set; }

        public override Task OnUploadCompletedAsync(StorageEventContext context, CancellationToken cancellationToken = default)
        {
            UploadCompletedCalled = true;
            return Task.CompletedTask;
        }
    }

    private static StorageEventContext MakeContext() => new()
    {
        ProviderName = "InMemory",
        OperationType = "Upload"
    };

    [Fact]
    public void ConcreteHandler_CanBeInstantiated()
    {
        var handler = new ConcreteHandler();
        handler.Should().NotBeNull();
    }

    [Fact]
    public async Task OnUploadCompletedAsync_DefaultImplementation_DoesNotThrow()
    {
        var handler = new ConcreteHandler();
        var act = async () => await handler.OnUploadCompletedAsync(MakeContext());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OnUploadFailedAsync_DefaultImplementation_DoesNotThrow()
    {
        var handler = new ConcreteHandler();
        var act = async () => await handler.OnUploadFailedAsync(MakeContext());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OnDownloadCompletedAsync_DefaultImplementation_DoesNotThrow()
    {
        var handler = new ConcreteHandler();
        var act = async () => await handler.OnDownloadCompletedAsync(MakeContext());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OnDeleteCompletedAsync_DefaultImplementation_DoesNotThrow()
    {
        var handler = new ConcreteHandler();
        var act = async () => await handler.OnDeleteCompletedAsync(MakeContext());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OverriddenMethod_IsCalledInsteadOfBase()
    {
        var handler = new OverridingHandler();
        await handler.OnUploadCompletedAsync(MakeContext());
        handler.UploadCompletedCalled.Should().BeTrue();
    }

    [Fact]
    public void ConcreteHandler_ImplementsIStorageEventHandler()
    {
        var handler = new ConcreteHandler();
        handler.Should().BeAssignableTo<IStorageEventHandler>();
    }
}
