using ValiBlob.Core.Models;

namespace ValiBlob.Core.Abstractions;

/// <summary>Determines the quota scope (tenant, user, etc.) for a given upload request.</summary>
public interface IQuotaScopeResolver
{
    /// <summary>Returns the scope identifier for the upload. Examples: "user:123", "tenant:acme", "org:default".</summary>
    string Resolve(UploadRequest request);
}
