using OpenGisDAF.Core;

namespace OpenGisDAF.Scheduling;

/// <summary>
/// 将调度层捕获的底层异常映射为框架错误码（<see cref="ErrorCode"/>）。
/// </summary>
/// <remarks>
/// 调度器在解析输入源、统计要素数量与写出结果时会直接触发适配器/数据源层异常，
/// 这些异常发生在算子执行阶段（<c>ERR_RT_*</c>）之外。此分类器按根因将其归类到
/// <c>SCH_*</c> 家族，或复用既有的 <c>ERR_DS_*</c> 数据源错误码，避免不同根因
/// （文件缺失、连接/密文非法、数据源写出失败等）全部塌缩为单一错误码。
/// 当前异常无特定信号时沿内部异常链继续查找，最终兜底为 <see cref="ErrorCode.SchUnexpected"/>。
/// 分类语义参见 docs/plan-config-guide.md。
/// </remarks>
public static class SchedulerErrorClassifier
{
    /// <summary>沿内部异常链查找的最大深度，防御异常的环形引用。</summary>
    private const int MaxInnerDepth = 10;

    /// <summary>
    /// 返回与 <paramref name="exception"/> 最贴切的错误码字符串。
    /// </summary>
    /// <param name="exception">调度层捕获的异常，允许为 <see langword="null"/>。</param>
    /// <returns>匹配到的 <c>SCH_*</c> / <c>ERR_DS_*</c> / <c>ERR_RT_*</c> 错误码；无匹配时为 <see cref="ErrorCode.SchUnexpected"/>。</returns>
    public static string Classify(Exception? exception)
    {
        var current = exception;
        for (var depth = 0; current is not null && depth < MaxInnerDepth; depth++)
        {
            var code = ClassifySingle(current);
            if (code is not null)
                return code;

            current = current.InnerException;
        }

        return ErrorCode.SchUnexpected;
    }

    // 返回 null 表示当前异常无特定信号，调用方应继续沿内部异常链查找。
    // 匹配顺序遵循从具体到宽泛：先 BCL 具体类型，再 extern 数据源基类，最后兜底。
    private static string? ClassifySingle(Exception exception) => exception switch
    {
        OperationCanceledException => ErrorCode.RtCancelled,
        FileNotFoundException => ErrorCode.SchInputNotFound,
        DirectoryNotFoundException => ErrorCode.SchInputNotFound,
        UnauthorizedAccessException => ErrorCode.DsPermissionDenied,
        NotSupportedException => ErrorCode.SchUnsupportedBinding,
        ArgumentException => ErrorCode.DsConnectionFailed,
        OpenGIS.Utils.Exception.OguException => ErrorCode.SchDataSourceError,
        _ => null
    };
}
