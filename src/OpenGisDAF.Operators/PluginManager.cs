using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using OpenGisDAF.Core;

namespace OpenGisDAF.Operators;

public sealed class PluginManager : IPluginManager
{
    private readonly IOperatorPool _pool;
    private readonly ILogger<PluginManager> _logger;
    private readonly List<PluginLoadContext> _contexts = [];
    private readonly object _contextsLock = new();
    private readonly ConcurrentDictionary<string, List<string>> _pluginOperators = new();

    public PluginManager(IOperatorPool pool, ILogger<PluginManager> logger)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<IOperator> ImportPlugin(string dllPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dllPath);

        var fullPath = Path.GetFullPath(dllPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Plugin DLL not found: {fullPath}");

        _logger.LogInformation("Loading plugin from: {Path}", fullPath);

        var context = new PluginLoadContext(fullPath);
        var assembly = context.LoadFromAssemblyPath(fullPath);
        var operators = new List<IOperator>();

        var operatorIds = new List<string>();

        foreach (var type in assembly.GetExportedTypes())
        {
            if (!typeof(IOperator).IsAssignableFrom(type) || type.IsAbstract)
                continue;

            try
            {
                if (Activator.CreateInstance(type) is IOperator op)
                {
                    _pool.Register(op);
                    operators.Add(op);
                    operatorIds.Add(op.Metadata.Id);
                    _logger.LogInformation("Registered operator: {OperatorId} ({Name}) from plugin {Plugin}",
                        op.Metadata.Id, op.Metadata.Name, Path.GetFileName(fullPath));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to instantiate operator type {TypeName} from plugin {Plugin}",
                    type.FullName, Path.GetFileName(fullPath));
            }
        }

        lock (_contextsLock)
        {
            _contexts.Add(context);
        }

        var pluginId = Path.GetFileNameWithoutExtension(fullPath);
        _pluginOperators[pluginId] = operatorIds;

        if (operators.Count == 0)
            _logger.LogWarning("No IOperator implementations found in: {Path}", fullPath);

        return operators;
    }

    public void UnloadPlugin(string pluginId)
    {
        PluginLoadContext? ctx;
        lock (_contextsLock)
        {
            ctx = _contexts.FirstOrDefault(c =>
                string.Equals(Path.GetFileNameWithoutExtension(c.DllPath), pluginId, StringComparison.OrdinalIgnoreCase));

            if (ctx is not null)
            {
                _contexts.Remove(ctx);
            }
        }

        if (ctx is not null)
        {
            if (_pluginOperators.TryRemove(pluginId, out var operatorIds))
            {
                foreach (var opId in operatorIds)
                {
                    _pool.Unregister(opId);
                }
            }

            ctx.Unload();
            _logger.LogInformation("Unloaded plugin: {PluginId}", pluginId);
        }
    }

    public IReadOnlyList<PluginInfo> GetLoadedPlugins()
    {
        lock (_contextsLock)
        {
            return _contexts.Select(c => new PluginInfo
            {
                PluginId = Path.GetFileNameWithoutExtension(c.DllPath),
                DllPath = c.DllPath,
                Version = c.Assembly.GetName().Version?.ToString() ?? "0.0.0",
                OperatorIds = _pool.GetAll()
                    .Where(op => op.GetType().Assembly == c.Assembly)
                    .Select(op => op.Metadata.Id)
                    .ToList()
            }).ToList();
        }
    }

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        public string DllPath { get; }
        public Assembly Assembly { get; private set; } = null!;

        public PluginLoadContext(string dllPath) : base(isCollectible: true)
        {
            DllPath = dllPath;
            _resolver = new AssemblyDependencyResolver(dllPath);
        }

        protected override Assembly? Load(AssemblyName name)
        {
            var path = _resolver.ResolveAssemblyToPath(name);
            return path is not null ? LoadFromAssemblyPath(path) : null;
        }

        public new Assembly LoadFromAssemblyPath(string path)
        {
            Assembly = base.LoadFromAssemblyPath(path);
            return Assembly;
        }
    }
}
