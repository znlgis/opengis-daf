namespace OpenGisDAF.Core;

public interface IPlanValidator
{
    ValidationResult Validate(AnalysisPlan plan, IOperatorPool? operatorPool = null);
}
