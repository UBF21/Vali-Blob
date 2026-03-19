namespace ValiBlob.Core.Models;

public enum StorageErrorCode
{
    None,
    FileNotFound,
    AccessDenied,
    QuotaExceeded,
    NetworkError,
    ValidationFailed,
    ProviderError,
    Timeout,
    NotSupported,
    Conflict
}
