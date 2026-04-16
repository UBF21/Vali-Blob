namespace ValiBlob.Core.Abstractions;

public interface IStorageFactory
{
    IStorageProvider Create(string? providerName = null);
    IStorageProvider Create<TProvider>() where TProvider : IStorageProvider;
    IEnumerable<IStorageProvider> GetAll();
}
