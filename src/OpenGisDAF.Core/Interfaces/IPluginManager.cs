namespace OpenGisDAF.Core;

public interface IPluginManager
{
    IReadOnlyList<IOperator> ImportPlugin(string dllPath);
    void UnloadPlugin(string pluginId);
    IReadOnlyList<PluginInfo> GetLoadedPlugins();
}

public sealed record PluginInfo
{
    public string PluginId { get; init; } = null!;
    public string DllPath { get; init; } = null!;
    public string Version { get; init; } = null!;
    public IReadOnlyList<string> OperatorIds { get; init; } = [];
}
