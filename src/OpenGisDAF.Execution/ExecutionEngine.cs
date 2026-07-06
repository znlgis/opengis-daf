using Microsoft.Extensions.Logging;
using OpenGisDAF.Core;
using ExecutionContext = OpenGisDAF.Core.ExecutionContext;

namespace OpenGisDAF.Execution;

public sealed partial class ExecutionEngine : IExecutionEngine
{
    private readonly IOperatorPool _operatorPool;
    private readonly IResultCache _resultCache;
    private readonly ILogger<ExecutionEngine> _logger;

    public ExecutionEngine(
        IOperatorPool operatorPool,
        IResultCache resultCache,
        ILogger<ExecutionEngine> logger)
    {
        _operatorPool = operatorPool;
        _resultCache = resultCache;
        _logger = logger;
    }

    public async Task<ExecutionResult> ExecuteItemAsync(
        AnalysisItem item,
        IReadOnlyDictionary<string, IFeatureSource> resolvedInputs,
        ExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var op = _operatorPool.GetById(item.OperatorId);
        if (op is null)
        {
            return new ExecutionResult
            {
                Status = ExecutionStatus.Failed,
                ErrorCode = ErrorCode.CfgOperatorNotFound,
                ErrorMessage = $"算子 '{item.OperatorId}' 未在算子池中注册",
                Elapsed = TimeSpan.Zero
            };
        }

        var policy = item.ExecutionPolicy;

        ExecutionResult result;
        if (policy.MaxRetries > 0)
        {
            result = await ExecuteWithRetryAsync(op, item, resolvedInputs, context, policy, cancellationToken);
        }
        else
        {
            result = await TimeoutController.ExecuteWithTimeoutAsync(
                async ct =>
                {
                    var parameters = new Dictionary<string, object?>(item.Parameters);
                    if (policy.QcMode)
                        parameters["_qc_mode"] = true;

                    return await op.ExecuteAsync(resolvedInputs, parameters, context, ct);
                },
                policy.Timeout,
                cancellationToken);
        }

        return result;
    }

    private async Task<ExecutionResult> ExecuteWithRetryAsync(
        IOperator op,
        AnalysisItem item,
        IReadOnlyDictionary<string, IFeatureSource> resolvedInputs,
        ExecutionContext context,
        ItemExecutionPolicy policy,
        CancellationToken cancellationToken)
    {
        var totalElapsed = TimeSpan.Zero;
        var lastResult = (ExecutionResult?)null;

        try
        {
            for (var attempt = 0; attempt <= policy.MaxRetries; attempt++)
            {
                if (attempt > 0)
                {
                    var delay = policy.ExponentialBackoff
                        ? TimeSpan.FromMilliseconds(policy.RetryInterval.TotalMilliseconds * Math.Pow(2, attempt - 1))
                        : policy.RetryInterval;

                    Log.RetryAttempt(_logger, attempt, policy.MaxRetries, (long)delay.TotalMilliseconds);

                    await Task.Delay(delay, cancellationToken);
                }

                var parameters = new Dictionary<string, object?>(item.Parameters);
                if (policy.QcMode)
                    parameters["_qc_mode"] = true;

                var result = await TimeoutController.ExecuteWithTimeoutAsync(
                    ct => op.ExecuteAsync(resolvedInputs, parameters, context, ct),
                    policy.Timeout,
                    cancellationToken);

                totalElapsed += result.Elapsed;

                if (result.Status == ExecutionStatus.Success)
                {
                    result = result with { Elapsed = totalElapsed };
                    return result;
                }

                if (result.ErrorCode is not null && !result.ErrorCode.StartsWith("ERR_RT_", StringComparison.Ordinal))
                {
                    Log.NonRuntimeErrorStopRetry(_logger, result.ErrorCode);
                    result = result with { Elapsed = totalElapsed };
                    return result;
                }

                lastResult = result;
                Log.ExecutionFailedRetrying(_logger, result.ErrorCode);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException ex)
        {
            Log.OperatorExecutionError(_logger, ex);
            return new ExecutionResult
            {
                Status = ExecutionStatus.Failed,
                ErrorCode = ErrorCode.RtUnexpected,
                ErrorMessage = $"算子参数异常: {ex.Message}",
                Elapsed = totalElapsed
            };
        }
        catch (InvalidOperationException ex)
        {
            Log.OperatorExecutionError(_logger, ex);
            return new ExecutionResult
            {
                Status = ExecutionStatus.Failed,
                ErrorCode = ErrorCode.RtUnexpected,
                ErrorMessage = $"算子执行异常: {ex.Message}",
                Elapsed = totalElapsed
            };
        }

        return lastResult is not null
            ? lastResult with { Elapsed = totalElapsed }
            : new ExecutionResult
            {
                Status = ExecutionStatus.Failed,
                ErrorCode = ErrorCode.RtUnexpected,
                ErrorMessage = "算子执行未返回任何结果",
                Elapsed = totalElapsed
            };
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information,
            Message = "重试第 {Attempt}/{MaxRetries} 次，等待 {DelayMs}ms")]
        public static partial void RetryAttempt(ILogger logger, int attempt, int maxRetries, long delayMs);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "非运行时错误，停止重试: {ErrorCode}")]
        public static partial void NonRuntimeErrorStopRetry(ILogger logger, string errorCode);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "执行失败（{ErrorCode}），准备重试")]
        public static partial void ExecutionFailedRetrying(ILogger logger, string? errorCode);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "算子执行异常")]
        public static partial void OperatorExecutionError(ILogger logger, Exception ex);
    }
}
