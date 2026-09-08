namespace OpenGisDAF.Core;

public interface IFeatureSchemaProvider
{
    FeatureSchemaMetadata Schema { get; }
}
