namespace OpenGisDAF.Core;

public interface IFeature
{
    string Id { get; }
    NetTopologySuite.Geometries.Geometry Geometry { get; }
    IReadOnlyDictionary<string, object?> Attributes { get; }
}
