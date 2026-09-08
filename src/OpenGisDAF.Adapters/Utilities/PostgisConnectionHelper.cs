using Npgsql;
using OpenGisDAF.Core;
using System.Security.Cryptography;

namespace OpenGisDAF.Adapters.Utilities;

public static class PostgisConnectionHelper
{
    /// <summary>
    /// 为 GDAL PG: 连接字符串转义值中的单引号和反斜杠。
    /// 先转义反斜杠再转义单引号，避免对已转义序列的二次转义。
    /// </summary>
    public static string EscapePgValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Replace("\\", "\\\\").Replace("'", "\\'");
    }

    public static string BuildConnectionString(ConnectionConfig config, IConnectionEncryption encryption)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(encryption);

        if (string.IsNullOrWhiteSpace(config.Host))
            throw new ArgumentException("PostGIS Host 不能为空。", nameof(config));
        if (config.Port is < 1 or > 65535)
            throw new ArgumentException("PostGIS Port 必须在 1 到 65535 之间。", nameof(config));
        if (string.IsNullOrWhiteSpace(config.Database))
            throw new ArgumentException("PostGIS Database 不能为空。", nameof(config));
        if (string.IsNullOrWhiteSpace(config.UserName))
            throw new ArgumentException("PostGIS UserName 不能为空。", nameof(config));
        if (string.IsNullOrWhiteSpace(config.EncryptedPassword))
            throw new ArgumentException("PostGIS EncryptedPassword 不能为空且必须是有效密文。", nameof(config));

        string password;
        try
        {
            password = encryption.Decrypt(config.EncryptedPassword);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            throw new ArgumentException("PostGIS EncryptedPassword 不是有效密文。", nameof(config), ex);
        }

        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("PostGIS EncryptedPassword 解密结果为空。", nameof(config));

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = config.Host,
            Port = config.Port,
            Database = config.Database,
            Username = config.UserName,
            Password = password
        };

        return $"PG:host='{EscapePgValue(builder.Host!)}' port={builder.Port} dbname='{EscapePgValue(builder.Database!)}' user='{EscapePgValue(builder.Username!)}' password='{EscapePgValue(password)}'";
    }
}
