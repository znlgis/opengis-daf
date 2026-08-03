using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace OpenGisDAF.Adapters.Utilities;

public interface IConnectionEncryption
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}

/// <summary>
/// 根据运行平台选择合适的连接字符串加密实现。
/// Windows 使用 DPAPI（与当前用户账户绑定）；其它平台使用 AES-GCM，
/// 密钥保存在当前用户配置目录下、仅所有者可读写的密钥文件中。
/// </summary>
public static class ConnectionEncryptionFactory
{
    public static IConnectionEncryption Create() =>
        OperatingSystem.IsWindows()
            ? new DpapiConnectionEncryption()
            : new AesConnectionEncryption();
}

/// <summary>
/// 基于 Windows DPAPI 的加密实现，仅在 Windows 上可用。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiConnectionEncryption : IConnectionEncryption
{
    public string Encrypt(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(cipherBytes);
    }

    public string Decrypt(string cipherText)
    {
        ArgumentNullException.ThrowIfNull(cipherText);

        var cipherBytes = Convert.FromBase64String(cipherText);
        var plainBytes = ProtectedData.Unprotect(cipherBytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }
}

/// <summary>
/// 跨平台的 AES-256-GCM 加密实现。密钥在首次使用时随机生成，
/// 持久化到当前用户配置目录下的密钥文件（在类 Unix 系统上权限设置为 0600）。
/// 密文格式为 Base64(nonce ‖ tag ‖ ciphertext)。
/// </summary>
public sealed class AesConnectionEncryption : IConnectionEncryption
{
    private const int KeySizeBytes = 32;   // AES-256
    private const int NonceSizeBytes = 12; // GCM 推荐值
    private const int TagSizeBytes = 16;   // GCM 标签

    private readonly Lazy<byte[]> _key;

    public AesConnectionEncryption(string? keyFilePath = null)
    {
        var path = keyFilePath ?? DefaultKeyFilePath();
        _key = new Lazy<byte[]>(() => LoadOrCreateKey(path));
    }

    public string Encrypt(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        if (plainText.Length == 0)
            return string.Empty;

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSizeBytes];

        using (var aes = new AesGcm(_key.Value, TagSizeBytes))
            aes.Encrypt(nonce, plainBytes, cipher, tag);

        var output = new byte[NonceSizeBytes + TagSizeBytes + cipher.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSizeBytes);
        Buffer.BlockCopy(tag, 0, output, NonceSizeBytes, TagSizeBytes);
        Buffer.BlockCopy(cipher, 0, output, NonceSizeBytes + TagSizeBytes, cipher.Length);
        return Convert.ToBase64String(output);
    }

    public string Decrypt(string cipherText)
    {
        ArgumentNullException.ThrowIfNull(cipherText);
        if (cipherText.Length == 0)
            return string.Empty;

        var data = Convert.FromBase64String(cipherText);
        if (data.Length < NonceSizeBytes + TagSizeBytes)
            throw new CryptographicException("密文长度无效。");

        var nonce = new byte[NonceSizeBytes];
        var tag = new byte[TagSizeBytes];
        var cipherLen = data.Length - NonceSizeBytes - TagSizeBytes;
        var cipher = new byte[cipherLen];
        Buffer.BlockCopy(data, 0, nonce, 0, NonceSizeBytes);
        Buffer.BlockCopy(data, NonceSizeBytes, tag, 0, TagSizeBytes);
        Buffer.BlockCopy(data, NonceSizeBytes + TagSizeBytes, cipher, 0, cipherLen);

        var plain = new byte[cipherLen];
        using (var aes = new AesGcm(_key.Value, TagSizeBytes))
            aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }

    private static string DefaultKeyFilePath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Path.GetTempPath();

        return Path.Combine(baseDir, "opengis-daf", "connection.key");
    }

    private static byte[] LoadOrCreateKey(string keyFilePath)
    {
        if (File.Exists(keyFilePath))
            return Convert.FromBase64String(File.ReadAllText(keyFilePath).Trim());

        var dir = Path.GetDirectoryName(keyFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var key = RandomNumberGenerator.GetBytes(KeySizeBytes);
        var tmpPath = keyFilePath + ".tmp";
        File.WriteAllText(tmpPath, Convert.ToBase64String(key));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(tmpPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        try
        {
            // Move without overwrite: if another process created the key first, keep theirs.
            File.Move(tmpPath, keyFilePath);
            return key;
        }
        catch (IOException)
        {
            TryDelete(tmpPath);
            return Convert.FromBase64String(File.ReadAllText(keyFilePath).Trim());
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }
}
