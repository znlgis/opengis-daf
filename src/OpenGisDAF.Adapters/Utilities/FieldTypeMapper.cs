using OpenGIS.Utils.Engine.Enums;
using OpenGisDAF.Core;

namespace OpenGisDAF.Adapters.Utilities;

public static class FieldTypeMapper
{
    public static FieldDataType ToFieldDataType(FieldType type) => type switch
    {
        FieldType.String => FieldDataType.STRING,
        FieldType.Integer => FieldDataType.INTEGER,
        FieldType.Double => FieldDataType.DOUBLE,
        FieldType.DateTime => FieldDataType.DATETIME,
        FieldType.Boolean => FieldDataType.BOOLEAN,
        _ => FieldDataType.STRING
    };

    public static FieldType FromFieldDataType(FieldDataType type) => type switch
    {
        FieldDataType.STRING => FieldType.String,
        FieldDataType.INTEGER or FieldDataType.LONG => FieldType.Integer,
        FieldDataType.DOUBLE or FieldDataType.FLOAT => FieldType.Double,
        FieldDataType.DATE or FieldDataType.DATETIME => FieldType.DateTime,
        FieldDataType.BOOLEAN => FieldType.Boolean,
        _ => FieldType.String
    };
}
