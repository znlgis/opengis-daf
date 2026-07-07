namespace OpenGisDAF.Core;

public enum ExecutionStatus { Success, Failed, Canceled }

public enum BindingType { External, Upstream, SubPlan }

public enum FieldType { String, Integer, Double, DateTime, Boolean, Geometry }

public enum GeometryType { Point, MultiPoint, LineString, MultiLineString, Polygon, MultiPolygon, GeometryCollection }

public enum ValidationSeverity { Error, Warning }

public enum IssueSeverity { Error, Warning, Info }

public enum LogGranularity { Plan, Item, Feature }

public enum FailurePolicy { StopOnAny, ContinueIndependent }

public enum OutputAdapterType { ConsoleWriter, GeoJsonWriter, ShapefileWriter, PostGISWriter }
