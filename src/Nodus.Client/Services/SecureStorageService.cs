using Nodus.Shared.Services;

namespace Nodus.Client.Services;

public class SecureStorageService : ISecureStorageService
{
    public Task<string?> GetAsync(string key) => SecureStorage.Default.GetAsync(key);

    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);

    public bool Remove(string key) => SecureStorage.Default.Remove(key);

    public void RemoveAll() => SecureStorage.Default.RemoveAll();
}
