using ValiBlob.Core.Pipeline;

namespace ValiBlob.Core.Abstractions;

public interface IStorageMiddleware
{
    Task InvokeAsync(StoragePipelineContext context, StorageMiddlewareDelegate next);
}

public delegate Task StorageMiddlewareDelegate(StoragePipelineContext context);
