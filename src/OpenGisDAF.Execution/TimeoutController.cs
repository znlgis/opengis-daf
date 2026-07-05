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
        var sw = Stopwatch.StartNew();
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCts.Token, externalCancellationToken);

        try
        {
            var result = await action(linkedCts.Token);
            return result;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !externalCancellationToken.IsCancellationRequested)
        {
            return new ExecutionResult
            {
                Status = ExecutionStatus.Failed,
                ErrorCode = ErrorCode.RtTimeout,
                ErrorMessage = $"执行超时（{timeout.TotalSeconds:F0}s）",
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
