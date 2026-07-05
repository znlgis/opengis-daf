namespace OpenGisDAF.Core;

public interface IOperatorPool
{
    void Register(IOperator op);
    bool Unregister(string operatorId);
    IOperator? GetById(string operatorId);
    IReadOnlyList<IOperator> GetByCategory(string category);
    IReadOnlyList<IOperator> Search(string keyword);
    IReadOnlyList<IOperator> GetAll();
    IReadOnlyDictionary<string, IReadOnlyList<IOperator>> GetAllGroupedByCategory();
}
