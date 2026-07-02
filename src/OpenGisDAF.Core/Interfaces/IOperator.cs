namespace OpenGisDAF.Core;

public interface IOperator
{
    OperatorMetadata Metadata { get; }
    ValidationResult Validate(AnalysisItem config);
    Task<ExecutionResult> ExecuteAsync(
        IReadOnlyDictionary<string, IFeatureSource> inputs,
        IReadOnlyDictionary<string, object?> parameters,
        ExecutionContext context,
        CancellationToken cancellationToken);
}
