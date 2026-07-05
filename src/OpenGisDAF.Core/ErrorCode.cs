namespace OpenGisDAF.Core;

public static class ErrorCode
{
    public const string CfgSchemaInvalid = "ERR_CFG_SCHEMA_INVALID";
    public const string CfgOperatorNotFound = "ERR_CFG_OPERATOR_NOT_FOUND";
    public const string CfgParamOutOfRange = "ERR_CFG_PARAM_OUT_OF_RANGE";
    public const string CfgDagCycle = "ERR_CFG_DAG_CYCLE";
    public const string CfgBindingIncomplete = "ERR_CFG_BINDING_INCOMPLETE";
    public const string CfgOperatorVersionMismatch = "ERR_CFG_OPERATOR_VERSION_MISMATCH";
    public const string CfgOutputTargetInvalid = "ERR_CFG_OUTPUT_TARGET_INVALID";
    public const string PlanNotFound = "ERR_PLAN_NOT_FOUND";
    public const string PlanVersionConflict = "ERR_PLAN_VERSION_CONFLICT";

    public const string DsConnectionFailed = "ERR_DS_CONNECTION_FAILED";
    public const string DsPermissionDenied = "ERR_DS_PERMISSION_DENIED";
    public const string DsFormatInvalid = "ERR_DS_FORMAT_INVALID";
    public const string DsCrsNotDeclared = "ERR_DS_CRS_NOT_DECLARED";
    public const string DsCrsMismatch = "ERR_DS_CRS_MISMATCH";

    public const string RtTimeout = "ERR_RT_TIMEOUT";
    public const string RtOutOfMemory = "ERR_RT_OUT_OF_MEMORY";
    public const string RtUnexpected = "ERR_RT_UNEXPECTED";
    public const string RtCancelled = "ERR_RT_CANCELLED";

    public const string DataGeometryInvalid = "ERR_DATA_GEOMETRY_INVALID";
    public const string DataFieldMissing = "ERR_DATA_FIELD_MISSING";
    public const string DataValueOutOfRange = "ERR_DATA_VALUE_OUT_OF_RANGE";
}
