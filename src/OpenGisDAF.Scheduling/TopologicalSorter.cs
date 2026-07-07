using OpenGisDAF.Core;

namespace OpenGisDAF.Scheduling;

public sealed class TopologicalSorter
{
    public static SortResult Sort(IReadOnlyList<AnalysisItem> items, Dictionary<string, List<string>> adj)
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
            if (itemMap.TryGetValue(node, out var item))
                ordered.Add(item);

            foreach (var neighbor in adj.GetValueOrDefault(node, []))
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
