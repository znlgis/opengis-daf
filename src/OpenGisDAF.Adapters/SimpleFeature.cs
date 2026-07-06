using NetTopologySuite.Geometries;
using OpenGisDAF.Core;

namespace OpenGisDAF.Adapters;

public sealed record SimpleFeature(
    string Id,
    Geometry Geometry,
    IReadOnlyDictionary<string, object?> Attributes
) : IFeature;
