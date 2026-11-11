using System.Security.Cryptography;
using System.Text;

namespace CarArchive.Services;

public sealed class CredentialService(string directory)
{
    private string FilePath => Path.Combine(directory, "gemini-key.bin");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CarArchive.Gemini.v1");
    public string Read()
    {
        if (!File.Exists(FilePath)) return "";
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser));
    }
    public void Save(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) { if (File.Exists(FilePath)) File.Delete(FilePath); return; }
        Directory.CreateDirectory(directory);
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), Entropy, DataProtectionScope.CurrentUser);
        var temp = FilePath + ".tmp"; File.WriteAllBytes(temp, bytes); File.Move(temp, FilePath, true);
    }
}
