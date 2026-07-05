using OpenGisDAF.Core;

namespace OpenGisDAF.Core;

public interface ISchedulingEngine
{
    Task<PlanExecutionStatistics> ExecuteAsync(
        AnalysisPlan plan,
        CancellationToken cancellationToken = default);
}
