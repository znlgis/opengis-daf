using OpenGisDAF.Core;

namespace OpenGisDAF.Execution;

public static class TimeoutController
{
    public static async Task<ExecutionResult> ExecuteWithTimeoutAsync(
        Func<CancellationToken, Task<ExecutionResult>> action,
        TimeSpan timeout,
        CancellationToken externalCancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCts.Token, externalCancellationToken);

        try
        {
            return await action(linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !externalCancellationToken.IsCancellationRequested)
        {
            return new ExecutionResult
            {
                Status = ExecutionStatus.Failed,
                ErrorCode = ErrorCode.RtTimeout,
                ErrorMessage = $"执行超时（{timeout.TotalSeconds:F0}s）",
                Elapsed = timeout
            };
        }
        catch (OperationCanceledException) when (externalCancellationToken.IsCancellationRequested)
        {
            return new ExecutionResult
            {
                Status = ExecutionStatus.Canceled,
                ErrorCode = ErrorCode.RtCancelled,
                ErrorMessage = "执行已取消（外部取消信号）",
                Elapsed = TimeSpan.Zero
            };
        }
    }
}
