namespace OpenGisDAF.Core;

public interface IPlanValidator
{
    Task<ValidationResult> ValidateAsync(AnalysisPlan plan, IOperatorPool? operatorPool = null, CancellationToken cancellationToken = default);
}
