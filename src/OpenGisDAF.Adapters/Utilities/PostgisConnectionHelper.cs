using Npgsql;
using OpenGisDAF.Core;

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

        var password = encryption.Decrypt(config.EncryptedPassword ?? string.Empty);
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
