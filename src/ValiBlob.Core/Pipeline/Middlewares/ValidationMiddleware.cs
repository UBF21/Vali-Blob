using Microsoft.Extensions.Options;
using ValiBlob.Core.Abstractions;
using ValiBlob.Core.Exceptions;
using ValiBlob.Core.Options;

namespace ValiBlob.Core.Pipeline.Middlewares;

public sealed class ValidationMiddleware : IStorageMiddleware
{
    private readonly ValidationOptions _options;
    private readonly IEnumerable<IFileValidator> _validators;

    public ValidationMiddleware(IOptions<ValidationOptions> options, IEnumerable<IFileValidator> validators)
    {
        _options = options.Value;
        _validators = validators;
    }

    public async Task InvokeAsync(StoragePipelineContext context, StorageMiddlewareDelegate next)
    {
        var request = context.Request;
        var errors = new List<string>();

        // Path traversal protection
        var pathString = request.Path.ToString();
        if (pathString.Contains(".."))
            errors.Add("Path contains invalid traversal sequences ('..').");

        // Size validation
        if (request.ContentLength.HasValue && request.ContentLength.Value > _options.MaxFileSizeBytes)
            errors.Add($"File size {request.ContentLength.Value:N0} bytes exceeds maximum allowed {_options.MaxFileSizeBytes:N0} bytes.");

        // Extension validation
        if (_options.AllowedExtensions.Count > 0)
        {
            var ext = Path.GetExtension(request.Path)?.ToLowerInvariant();
            if (ext is not null && !_options.AllowedExtensions.Contains(ext))
                errors.Add($"File extension '{ext}' is not allowed.");
        }

        var blockedExt = Path.GetExtension(request.Path)?.ToLowerInvariant();
        if (blockedExt is not null && _options.BlockedExtensions.Contains(blockedExt))
            errors.Add($"File extension '{blockedExt}' is blocked.");

        // Content type validation
        if (_options.AllowedContentTypes.Count > 0 && request.ContentType is not null)
        {
            if (!_options.AllowedContentTypes.Contains(request.ContentType))
                errors.Add($"Content type '{request.ContentType}' is not allowed.");
        }

        // Custom validators
        foreach (var validator in _validators)
        {
            var result = await validator.ValidateAsync(request);
            if (!result.IsValid)
                errors.AddRange(result.Errors);
        }

        if (errors.Count > 0)
        {
            context.IsCancelled = true;
            throw new StorageValidationException(errors);
        }

        await next(context);
    }
}
