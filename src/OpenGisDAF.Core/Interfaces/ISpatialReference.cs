namespace OpenGisDAF.Core;

public interface ISpatialReference
{
    string Authority { get; }
    int Code { get; }
    string Wkt { get; }
    bool IsEquivalentTo(ISpatialReference other);
}
