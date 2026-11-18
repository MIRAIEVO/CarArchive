using System.Security.Cryptography;
using System.Text;
using CarArchive.Core;

namespace CarArchive.Services;

public sealed class CredentialService(string directory)
{
    public string Read() => Read(RecognitionProvider.Gemini);
    public string Read(RecognitionProvider provider)
    {
        var file=Path.Combine(directory,ProviderCatalog.KeyFile(provider));
        if (!File.Exists(file)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(file), Entropy(provider), DataProtectionScope.CurrentUser)); }
        catch(CryptographicException) { return ""; }
    }
    private static byte[] Entropy(RecognitionProvider provider) => Encoding.UTF8.GetBytes($"CarArchive.{provider}.v1");
    public void Save(string key) => Save(RecognitionProvider.Gemini,key);
    public void Save(RecognitionProvider provider,string key)
    {
        var FilePath=Path.Combine(directory,ProviderCatalog.KeyFile(provider));
        if (string.IsNullOrWhiteSpace(key)) { if (File.Exists(FilePath)) File.Delete(FilePath); return; }
        Directory.CreateDirectory(directory);
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), Entropy(provider), DataProtectionScope.CurrentUser);
        var temp = FilePath + ".tmp"; File.WriteAllBytes(temp, bytes); File.Move(temp, FilePath, true);
    }
}
