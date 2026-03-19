using ValiBlob.Core.Models;

namespace ValiBlob.Core.Exceptions;

public class StorageException : Exception
{
    public StorageErrorCode ErrorCode { get; }

    public StorageException(string message, StorageErrorCode errorCode = StorageErrorCode.ProviderError)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public StorageException(string message, Exception innerException, StorageErrorCode errorCode = StorageErrorCode.ProviderError)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

public sealed class StorageValidationException : StorageException
{
    public IReadOnlyList<string> Errors { get; }

    public StorageValidationException(IEnumerable<string> errors)
        : base("File validation failed.", StorageErrorCode.ValidationFailed)
    {
        Errors = errors.ToList().AsReadOnly();
    }

    public override string Message =>
        $"File validation failed: {string.Join("; ", Errors)}";
}

public sealed class StorageFileNotFoundException : StorageException
{
    public string FilePath { get; }

    public StorageFileNotFoundException(string path)
        : base($"File not found: {path}", StorageErrorCode.FileNotFound)
    {
        FilePath = path;
    }
}
