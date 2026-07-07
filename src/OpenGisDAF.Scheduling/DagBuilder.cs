using OpenGisDAF.Core;

namespace OpenGisDAF.Scheduling;

public sealed class DagBuilder
{
    public static DagResult Build(IReadOnlyList<AnalysisItem> items)
    {
        var adj = new Dictionary<string, List<string>>();
        var inDegree = new Dictionary<string, int>();

        foreach (var item in items)
        {
            adj[item.Id] = [];
            inDegree[item.Id] = 0;
        }

        foreach (var item in items)
        {
            foreach (var (_, binding) in item.Inputs)
            {
                if (binding.Type == BindingType.Upstream)
                {
                    if (!adj.TryGetValue(binding.SourceId, out var upstreamNeighbors))
                    {
                        return DagResult.Failure(
                            $"分析项 '{item.Id}' 的上游依赖 '{binding.SourceId}' 不在方案中");
                    }
                    upstreamNeighbors.Add(item.Id);
                    inDegree[item.Id]++;
                }

                if (binding.Type == BindingType.SubPlan)
                {
                    return DagResult.Failure(
                        $"P1 不支持子方案引用: '{item.Id}' 引用了 SubPlan '{binding.SourceId}'");
                }
            }
        }

        // Cycle detection via Kahn's algorithm
        var queue = new Queue<string>();
        foreach (var (id, degree) in inDegree)
        {
            if (degree == 0)
                queue.Enqueue(id);
        }

        var visited = 0;
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            visited++;

            foreach (var neighbor in adj[node])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        if (visited != items.Count)
        {
            return DagResult.Failure("方案中存在循环依赖");
        }

        return DagResult.Success(adj);
    }
}

public sealed record DagResult
{
    public bool IsValid { get; private init; }
    public string? ErrorMessage { get; private init; }
    public Dictionary<string, List<string>>? Adjacency { get; private init; }

    public static DagResult Success(Dictionary<string, List<string>> adj) => new()
    {
        IsValid = true,
        Adjacency = adj
    };

    public static DagResult Failure(string message) => new()
    {
        IsValid = false,
        ErrorMessage = message
    };
}
