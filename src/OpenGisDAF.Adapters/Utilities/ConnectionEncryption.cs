using System.Security.Cryptography;
using System.Text;

namespace OpenGisDAF.Adapters.Utilities;

public interface IConnectionEncryption
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}

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
