namespace OpenGisDAF.Core;

public enum ExecutionStatus { Pending, Queued, Executing, Success, Failed, Canceled, Retrying }

public enum BindingType { External, Upstream, SubPlan }

public enum FieldType { String, Integer, Double, DateTime, Boolean, Geometry }

public enum GeometryType { Point, MultiPoint, LineString, MultiLineString, Polygon, MultiPolygon, GeometryCollection }

public enum ValidationSeverity { Error, Warning }

public enum IssueSeverity { Error, Warning, Info }

public enum LogGranularity { Item }
