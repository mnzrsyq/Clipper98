using System.Security.Cryptography;
using System.Text;
using Google.Apis.Util.Store;
using Newtonsoft.Json;

namespace Clipper98;

public sealed class ProtectedYouTubeTokenStore : IDataStore
{
    private static readonly string StoreFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clipper98", "YouTubeAuth");

    public async Task StoreAsync<T>(string key, T value)
    {
        Directory.CreateDirectory(StoreFolder);
        var json = JsonConvert.SerializeObject(value);
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(GetPath<T>(key), encrypted);
    }

    public async Task<T> GetAsync<T>(string key)
    {
        var path = GetPath<T>(key);
        if (!File.Exists(path)) return default!;
        var encrypted = await File.ReadAllBytesAsync(path);
        var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));
        return JsonConvert.DeserializeObject<T>(json)!;
    }

    public Task DeleteAsync<T>(string key)
    {
        var path = GetPath<T>(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        if (Directory.Exists(StoreFolder))
            Directory.Delete(StoreFolder, true);
        return Task.CompletedTask;
    }

    private static string GetPath<T>(string key)
    {
        var identity = $"{typeof(T).AssemblyQualifiedName}|{key}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return Path.Combine(StoreFolder, hash + ".bin");
    }
}
