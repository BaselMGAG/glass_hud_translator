using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GamingTranslatorGlassHUD.Core.Storage;

namespace GamingTranslatorGlassHUD.Windows;

/// <summary>
/// API keys encrypted with DPAPI, scoped to the current Windows user.
///
/// <para>
/// The file is useless if copied to another machine or opened by another account, which is the
/// property worth having: keys are bring-your-own, so the only person who can read them is the
/// person who typed them in.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GamingTranslatorGlassHUD.v1");

    private readonly string _path;
    private Dictionary<string, string> _protectedValues;

    public DpapiSecretStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppPaths.DataDirectory, "secrets.dat");
        _protectedValues = Load(_path);
    }

    public string? Get(string name)
    {
        if (!_protectedValues.TryGetValue(name, out var encoded)) return null;

        try
        {
            var plain = ProtectedData.Unprotect(
                Convert.FromBase64String(encoded), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            // Copied from another machine or another user account. Treat as absent rather than
            // failing - the user is asked for the key again, which is the right recovery.
            return null;
        }
    }

    public void Set(string name, string value)
    {
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);

        _protectedValues[name] = Convert.ToBase64String(encrypted);
        Save();
    }

    public void Delete(string name)
    {
        if (_protectedValues.Remove(name)) Save();
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
        File.WriteAllText(_path, JsonSerializer.Serialize(_protectedValues,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
