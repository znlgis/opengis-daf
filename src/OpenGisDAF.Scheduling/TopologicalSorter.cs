using OpenGisDAF.Core;

namespace OpenGisDAF.Scheduling;

public sealed class TopologicalSorter
{
    public static SortResult Sort(IReadOnlyList<AnalysisItem> items)
    {
        var adj = new Dictionary<string, List<string>>();
        var inDegree = new Dictionary<string, int>();
        var itemMap = items.ToDictionary(i => i.Id);

        foreach (var item in items)
        {
            adj[item.Id] = [];
            inDegree[item.Id] = 0;
        }

        foreach (var item in items)
        {
            foreach (var (_, binding) in item.Inputs)
            {
                if (binding.Type == BindingType.Upstream && adj.TryGetValue(binding.SourceId, out var upstreamNeighbors))
                {
                    upstreamNeighbors.Add(item.Id);
                    inDegree[item.Id] = inDegree.GetValueOrDefault(item.Id, 0) + 1;
                }
            }
        }

        var queue = new Queue<string>();
        foreach (var (id, degree) in inDegree)
        {
            if (degree == 0)
                queue.Enqueue(id);
        }

        var ordered = new List<AnalysisItem>();
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            ordered.Add(itemMap[node]);

            foreach (var neighbor in adj[node])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        return new SortResult
        {
            OrderedItems = ordered,
            IsComplete = ordered.Count == items.Count
        };
    }

    public static SortResult SortFromDag(IReadOnlyList<AnalysisItem> items, Dictionary<string, List<string>> adj)
    {
        var inDegree = new Dictionary<string, int>();
        var itemMap = items.ToDictionary(i => i.Id);

        foreach (var item in items)
        {
            inDegree[item.Id] = 0;
        }

        // Build in-degrees from adj
        foreach (var (_, neighbors) in adj)
        {
            foreach (var neighbor in neighbors)
            {
                inDegree[neighbor] = inDegree.GetValueOrDefault(neighbor, 0) + 1;
            }
        }

        var queue = new Queue<string>();
        foreach (var (id, degree) in inDegree)
        {
            if (degree == 0)
                queue.Enqueue(id);
        }

        var ordered = new List<AnalysisItem>();
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            ordered.Add(itemMap[node]);

            foreach (var neighbor in adj[node])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        return new SortResult
        {
            OrderedItems = ordered,
            IsComplete = ordered.Count == items.Count
        };
    }
}

public sealed record SortResult
{
    public IReadOnlyList<AnalysisItem> OrderedItems { get; init; } = [];
    public bool IsComplete { get; init; }
}
