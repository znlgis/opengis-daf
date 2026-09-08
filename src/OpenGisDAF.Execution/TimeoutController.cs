using System.Diagnostics;
using OpenGisDAF.Core;

namespace OpenGisDAF.Execution;

public static class TimeoutController
{
    public static async Task<ExecutionResult> ExecuteWithTimeoutAsync(
        Func<CancellationToken, Task<ExecutionResult>> action,
        TimeSpan timeout,
        CancellationToken externalCancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be a positive duration.");

        var sw = Stopwatch.StartNew();
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCts.Token, externalCancellationToken);

        try
        {
            var result = await action(linkedCts.Token);

            // 算子可能自行捕获 OperationCanceledException 并返回 Canceled/RtCancelled，
            // 使下方 catch 无法区分超时与用户取消；此处按令牌归属重新分类。
            if (result.ErrorCode == ErrorCode.RtCancelled
                && timeoutCts.IsCancellationRequested
                && !externalCancellationToken.IsCancellationRequested)
            {
                return result with
                {
                    Status = ExecutionStatus.Failed,
                    ErrorCode = ErrorCode.RtTimeout,
                    ErrorMessage = $"执行超时（{timeout.TotalSeconds:0.###}s）"
                };
            }

            return result;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !externalCancellationToken.IsCancellationRequested)
        {
            return new ExecutionResult
            {
                Status = ExecutionStatus.Failed,
                ErrorCode = ErrorCode.RtTimeout,
                ErrorMessage = $"执行超时（{timeout.TotalSeconds:0.###}s）",
                Elapsed = sw.Elapsed
            };
        }
        catch (OperationCanceledException) when (externalCancellationToken.IsCancellationRequested)
        {
            return new ExecutionResult
            {
                Status = ExecutionStatus.Canceled,
                ErrorCode = ErrorCode.RtCancelled,
                ErrorMessage = "执行已取消（外部取消信号）",
                Elapsed = sw.Elapsed
            };
        }
    }
}
