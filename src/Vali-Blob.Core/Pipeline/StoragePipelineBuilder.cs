using ValiBlob.Core.Abstractions;

namespace ValiBlob.Core.Pipeline;

public sealed class StoragePipelineBuilder
{
    private readonly List<IStorageMiddleware> _middlewares = new();

    public StoragePipelineBuilder Use(IStorageMiddleware middleware)
    {
        _middlewares.Add(middleware);
        return this;
    }

    public StorageMiddlewareDelegate Build()
    {
        StorageMiddlewareDelegate pipeline = _ => Task.CompletedTask;

        for (var i = _middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = _middlewares[i];
            var next = pipeline;
            pipeline = context => middleware.InvokeAsync(context, next);
        }

        return pipeline;
    }

    public async Task ExecuteAsync(StoragePipelineContext context, CancellationToken cancellationToken = default)
    {
        context.CancellationToken = cancellationToken;
        var pipeline = Build();
        await pipeline(context);
    }
}
