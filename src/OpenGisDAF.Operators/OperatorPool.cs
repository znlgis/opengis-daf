using System.Collections.Concurrent;
using OpenGisDAF.Core;

namespace OpenGisDAF.Operators;

public sealed class OperatorPool : IOperatorPool
{
    private readonly ConcurrentDictionary<string, IOperator> _operators = new();
    private readonly ConcurrentDictionary<string, List<string>> _categoryIndex = new();

    public void Register(IOperator op)
    {
        ArgumentNullException.ThrowIfNull(op);
        _operators[op.Metadata.Id] = op;

        var category = op.Metadata.Category;
        _categoryIndex.AddOrUpdate(
            category,
            _ => [op.Metadata.Id],
            (_, list) => { list.Add(op.Metadata.Id); return list; });
    }

    public IOperator? GetById(string operatorId) =>
        _operators.TryGetValue(operatorId, out var op) ? op : null;

    public IReadOnlyList<IOperator> GetByCategory(string category) =>
        _categoryIndex.TryGetValue(category, out var ids)
            ? ids.Select(id => _operators[id]).ToList()
            : [];

    public IReadOnlyList<IOperator> Search(string keyword)
    {
        var lower = keyword.ToLowerInvariant();
        return _operators.Values
            .Where(op =>
                op.Metadata.Id.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
                op.Metadata.Name.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
                op.Metadata.Category.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
                op.Metadata.Tags.Any(t => t.Contains(lower, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public IReadOnlyList<IOperator> GetAll() => _operators.Values.ToList();

    public IReadOnlyDictionary<string, IReadOnlyList<IOperator>> GetByCategory()
        => _categoryIndex.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<IOperator>)kvp.Value.Select(id => _operators[id]).ToList());
}
