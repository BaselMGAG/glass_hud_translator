using System.Text.Json;

namespace GamingTranslatorGlassHUD.Core.Storage;

/// <summary>
/// API key storage. Bring-your-own-key: the developer's key is never embedded, because a key in a
/// distributed binary is extractable in seconds, and because Google's additional terms require
/// paid services when making an API client available to users in the EEA/Switzerland/UK - which
/// both parties here are (brief 11).
/// </summary>
public interface ISecretStore
{
    string? Get(string name);
    void Set(string name, string value);
    void Delete(string name);
}

public static class SecretStoreExtensions
{
    public static bool Has(this ISecretStore store, string name) =>
        !string.IsNullOrWhiteSpace(store.Get(name));
}

public static class SecretNames
{
    public const string GeminiApiKey = "GeminiApiKey";
    public const string GroqApiKey = "GroqApiKey";
}

/// <summary>
/// Plaintext secrets on disk, for development only.
///
/// <para>
/// Exists because the shipping store is DPAPI, and <c>ProtectedData</c> throws
/// <see cref="PlatformNotSupportedException"/> off Windows - so without this seam the settings
/// screen could not be run or debugged on the development machine at all (PROJECT_PLAN.md 1.3).
/// It announces itself loudly on construction so it can never be shipped by accident, and the file
/// it writes is gitignored.
/// </para>
/// </summary>
public sealed class DevPlainFileSecretStore : ISecretStore
{
    private readonly string _path;
    private Dictionary<string, string> _values;

    public DevPlainFileSecretStore(string? path = null)
    {
        _path = path ?? AppPaths.DevSecrets;
        _values = Load(_path);

        Console.Error.WriteLine(
            $"WARNING: DevPlainFileSecretStore is active. API keys are stored UNENCRYPTED at {_path}. " +
            "This must never run in a shipped build - Windows uses DpapiSecretStore.");
    }

    public string? Get(string name) => _values.GetValueOrDefault(name);

    public void Set(string name, string value)
    {
        _values[name] = value;
        Save();
    }

    public void Delete(string name)
    {
        if (_values.Remove(name)) Save();
    }

    private static Dictionary<string, string> Load(string path)
    {
        if (!File.Exists(path)) return [];

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true }));

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}

/// <summary>In-memory store for tests and for --provider stub runs that need no key at all.</summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _values = [];

    public string? Get(string name) => _values.GetValueOrDefault(name);
    public void Set(string name, string value) => _values[name] = value;
    public void Delete(string name) => _values.Remove(name);
}
