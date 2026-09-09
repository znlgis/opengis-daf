using NetTopologySuite.Geometries;
using OpenGisDAF.Core;

namespace OpenGisDAF.SamplePlugin;

/// <summary>
/// 插件自带的不可变要素实现，仅依赖 <c>OpenGisDAF.Core.IFeature</c> 契约。
/// </summary>
public sealed record PluginFeature(
    string Id,
    Geometry Geometry,
    IReadOnlyDictionary<string, object?> Attributes
) : IFeature;
