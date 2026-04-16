namespace ValiBlob.Core.Models;

public readonly struct StorageResult
{
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }
    public StorageErrorCode ErrorCode { get; }
    public Exception? Exception { get; }

    private StorageResult(bool isSuccess, string? errorMessage, StorageErrorCode errorCode, Exception? exception)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
        Exception = exception;
    }

    public static StorageResult Success() => new(true, null, StorageErrorCode.None, null);

    public static StorageResult Failure(string message, StorageErrorCode code = StorageErrorCode.ProviderError, Exception? ex = null)
        => new(false, message, code, ex);

    public static implicit operator bool(StorageResult result) => result.IsSuccess;

    public override string ToString() =>
        IsSuccess ? "Success" : $"Failure({ErrorCode}): {ErrorMessage}";
}

public readonly struct StorageResult<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? ErrorMessage { get; }
    public StorageErrorCode ErrorCode { get; }
    public Exception? Exception { get; }

    private StorageResult(bool isSuccess, T? value, string? errorMessage, StorageErrorCode errorCode, Exception? exception)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
        Exception = exception;
    }

    public static StorageResult<T> Success(T value) => new(true, value, null, StorageErrorCode.None, null);

    public static StorageResult<T> Failure(string message, StorageErrorCode code = StorageErrorCode.ProviderError, Exception? ex = null)
        => new(false, default, message, code, ex);

    public static implicit operator bool(StorageResult<T> result) => result.IsSuccess;

    public StorageResult<TResult> Map<TResult>(Func<T, TResult> mapper) =>
        IsSuccess && Value is not null
            ? StorageResult<TResult>.Success(mapper(Value))
            : StorageResult<TResult>.Failure(ErrorMessage!, ErrorCode, Exception);

    public override string ToString() =>
        IsSuccess ? $"Success({Value})" : $"Failure({ErrorCode}): {ErrorMessage}";
}
