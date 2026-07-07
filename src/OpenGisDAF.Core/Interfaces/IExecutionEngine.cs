namespace OpenGisDAF.Core;

public interface IExecutionEngine
{
    Task<ExecutionResult> ExecuteItemAsync(
        AnalysisItem item,
        IReadOnlyDictionary<string, IFeatureSource> resolvedInputs,
        ExecutionContext context,
        CancellationToken cancellationToken = default);
}
