# OpenGisDAF 全量缺陷修复实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复两轮真实 SHP/PostGIS/CLI 测试确认的 D1–D27 问题，使方案 JSON、九个内置算子、三类文件输出、PostGIS、质检报告、CLI 退出码和 CI 测试链路可实际使用。

**Architecture:** 保留 `ExecutionResult` 公共协议和现有调度结构，在 JSON 反序列化、source 元数据、sink schema、QC issue 和运行状态边界增加集中式适配层。算子输出继续使用 `IFeatureSource` 或 `IEnumerable<IssueRecord>`，由 scheduler 负责把 source 元数据组装成真实 `OutputSchema`；OGU 子模块负责把底层写入错误完整传播给 DAF。

**Tech Stack:** .NET 10、C# 14、NetTopologySuite 2.6.0、System.Text.Json 10.0.11、MaxRev.Gdal 3.13.3.557、xunit.v3 4.0.0、Microsoft Testing Platform、PostGIS 3.5.2。

**Spec:** `docs/superpowers/specs/2026-09-08-all-issues-remediation-design.md`

## Global Constraints

- 同时修改 DAF 主仓库与 `extern/opengis-utils-for-net` 子模块；OGU 改动先在子模块独立提交，父仓库随后只更新 gitlink。
- 不扩展 `ExecutionResult` 公共协议，不新增 3D `GeometryType` 枚举成员，不把 `FeatureId` 改成可配置业务主键。
- `IssueRecord.FeatureId` 继续表示驱动 FID，并在文档和测试中明确当前驱动可为 0-based 且跨版本不稳定。
- DAF 侧不新增第三方依赖；优先使用已有 NetTopologySuite、System.Text.Json、OGU 和 Microsoft Testing Platform。
- 所有实现步骤先写失败测试，再写最小实现；每个任务完成后运行该任务列出的最窄验证命令。
- 不使用含糊的 `utils`、`helpers`、`service` catch-all 新文件；新增文件必须有单一职责名称。
- 真实 SHP 数据根目录为 `D:\work\resource\其他项目\渠道数据迁移项目\原始数据分析\原始数据`；测试输出写入 `.artifacts\daf-test`，该目录已被 `.gitignore` 忽略。
- PostGIS 测试表使用 `daf_test_` 前缀；验证结束后删除临时表，只保留约定的最后一轮人工查看结果。

---

## 文件与职责地图

### DAF Core

- Modify `src/OpenGisDAF.Core/Models/OutputSchema.cs`: 增加可选 WKID/空间参考元数据，并保持旧构造方式可用。
- Modify `src/OpenGisDAF.Core/Models/IssueRecord.cs`: 为 `ViolationGeometry` 提供专用 JSON 转换边界，避免反射 NTS 内部对象。
- Add `src/OpenGisDAF.Core/Interfaces/IFeatureSchemaProvider.cs`: 可选 source schema 能力，避免修改 `IFeatureSource` 现有接口。
- Add `src/OpenGisDAF.Core/Models/FeatureSchemaMetadata.cs`: 字段定义、几何类型和空间参考的集中模型。

### DAF infrastructure and plan input

- Modify `src/OpenGisDAF.Infrastructure/JsonConfiguration.cs`: 参数归一化、枚举别名、非 ASCII 输出和浮点 JSON 安全配置。
- Add `src/OpenGisDAF.Infrastructure/JsonConverters/AnalysisParametersJsonConverter.cs`: 将简单 `JsonElement` 转换成 CLR 基元。
- Add `src/OpenGisDAF.Infrastructure/JsonConverters/OutputAdapterTypeJsonConverter.cs`: 接受短名和枚举成员名，序列化保持现有 camelCase 成员名。
- Modify `src/OpenGisDAF.PlanManagement/PlanSerializer.cs`: 注册 converter 并保持 plan 往返。
- Modify `src/OpenGisDAF.PlanManagement/PlanValidator.cs`: 空草稿可保存但不可运行；保留正式方案规则；补全输出和连接配置校验。
- Modify `src/OpenGisDAF.PlanManagement/ServiceCollectionExtensions.cs` and `PlanRepository.cs`: 默认仓库使用稳定用户数据目录，同时允许显式 root path。

### DAF adapters and operators

- Modify `src/OpenGisDAF.Adapters/Utilities/WktConverter.cs`: 使用三维 writer，二维 geometry 不补 Z。
- Modify `src/OpenGisDAF.Adapters/InMemoryFeatureSource.cs`: 保存字段、几何类型和真实 CRS，Dispose 只清理本 source 的可控资源。
- Modify `src/OpenGisDAF.Adapters/GeoJsonFeatureSink.cs`, `ShapefileFeatureSink.cs`, `PostgisFeatureSink.cs`: 使用真实 schema/WKID，禁止硬编码 4326 和空 schema 降级。
- Modify `src/OpenGisDAF.Adapters/PostgisFeatureSource.cs` and `src/OpenGisDAF.Execution/SourceFactory.cs`: 支持结构化 PostGIS 输入配置并统一脱敏错误。
- Modify `src/OpenGisDAF.Adapters/Utilities/ConnectionEncryption.cs` and `PostgisConnectionHelper.cs`: 拒绝无效密文，错误不泄露口令和完整连接串。
- Modify `src/OpenGisDAF.Operators/OperatorHelper.cs` and all parameterized operators: 统一读取 JSON/CLR 参数。
- Modify the four QC operators: 统一 `IssueRecord` 输出，并保留空间违规 feature 的独立 output key。

### DAF execution, scheduling, CLI, tests and docs

- Modify `src/OpenGisDAF.Scheduling/SerialScheduler.cs`: 生成真实 OutputSchema、记录 QC 实际检查数量、统一 source 生命周期、修复 DAG 环消息和运行状态统计。
- Modify `src/OpenGisDAF.Execution/QualityCalculator.cs` and `QualityReportGenerator.cs`: 按实际检查数评分并可靠生成 JSON 报告。
- Modify `src/OpenGisDAF.Execution/TimeoutController.cs` only if its regression test fails.
- Modify `src/OpenGisDAF.Cli/DafApplication.cs`: 失败/跳过/取消/超时返回非零，删除重复 Console sink，修复 plan create/run 工作流。
- Modify `.github/workflows/ci.yml` and `tests/OpenGisDAF.IntegrationTests/OpenGisDAF.IntegrationTests.csproj`: 恢复标准 `dotnet test`。
- Modify existing tests and add focused regression tests under `tests/OpenGisDAF.IntegrationTests`: 覆盖升级后 0-based FID、参数 JSON、真实 schema/CRS/Z、QC、CLI 和 PostGIS。
- Modify `docs/plan-config-guide.md`, `docs/cli-reference.md`, `docs/operator-reference.md`: 更新 adapterType、FeatureId、PostGIS、QC 和 plan create 契约。

### OGU submodule

- Modify `extern/opengis-utils-for-net/src/OpenGIS.Utils/Engine/GdalWriter.cs`: 创建图层失败、单 feature 失败和驱动异常必须向调用方抛出带 FID 的聚合异常；不把失败写成 Console warning 后继续。
- Modify/add OGU writer and CRS tests in the submodule’s existing test project: 验证 GeoJSON、Shapefile、PostGIS 的字段、CRS、Z 和错误传播。

---

## Task 1: 建立升级后红色回归测试

**Files:**
- Modify: `tests/OpenGisDAF.IntegrationTests/Scenario3_QualityCheck.cs`
- Add: `tests/OpenGisDAF.IntegrationTests/RemediationRegressionTests.cs`
- Add: `tests/OpenGisDAF.IntegrationTests/TestData/invalid-geometry.geojson` only if the existing test data does not cover all three geometry issue types
- Test: existing integration test project

**Interfaces:**
- Consumes: `DafTestHost`, `ISchedulingEngine`, `AnalysisPlan`, `OutputSchema`, existing xunit v4 APIs.
- Produces: executable red tests for JSON parameters, output metadata, QC reports, failure exit status, FID semantics, and real output formats.

- [ ] **Step 1: Update the D27 assertion to the selected compatibility contract.**

Change the Scenario3 assertions so they expect the upgraded driver behavior: `FeatureId == "3" && IssueType == "ATTR_EMPTY"` for the explicit empty description, and assert null fields produce `ATTR_MISSING`. Do not use `properties.fid` as `IssueRecord.FeatureId`.

- [ ] **Step 2: Add red tests for the required JSON parameter forms.**

Create plans through `PlanSerializer.Deserialize` rather than constructing C# dictionaries directly. Assert that these values reach their operators: `source_epsg=2326`, `target_epsg=4326`, `relationship="within"`, `use_second_input=false`, `target_field="TEST_FIELD"`, `field_type="Double"`, and `required_fields="FACILITY_I,NAME"`. Assert malformed values return a configuration failure without `InvalidCastException` and without silently applying defaults.

- [ ] **Step 3: Add red tests for real output metadata.**

Use the existing EPSG:2326 SHP fixtures and assert that GeoJSON, Shapefile and PostGIS preserve source field names/types, output CRS, and Z dimension. Assert Shapefile has `.prj`; assert PostGIS has the expected columns, `ST_SRID=2326`, and `ST_NDims=3`; assert GeoJSON has CRS metadata consistent with metre coordinates.

- [ ] **Step 4: Add red tests for QC contracts and report boundaries.**

Cover geometry empty/invalid/non-simple, attribute missing/empty, intersect and containment issues. Assert all issues are `IssueRecord`, `TotalChecked` equals the number of primary input features, zero-issue QC still writes a report, and `ViolationGeometry` serializes as WKT or null.

- [ ] **Step 5: Add red tests for CLI and lifecycle behavior.**

Cover failed item → `run` returns 1, upstream-skipped item → 1, cancellation/timeout codes, one Console sink registration, complete DAG cycle text, stable plan repository root, and external sources reused/disposed by stable source identity.

- [ ] **Step 6: Run the focused tests and record expected failures.**

Run from repository root:

```powershell
dotnet run --project tests/OpenGisDAF.IntegrationTests
```

Expected: the test process is runnable, existing Scenario3 and the new remediation tests are red for the known defects. This establishes the implementation feedback loop before production changes.

---

## Task 2: Repair plan JSON input and connection validation

**Files:**
- Add: `src/OpenGisDAF.Infrastructure/JsonConverters/AnalysisParametersJsonConverter.cs`
- Add: `src/OpenGisDAF.Infrastructure/JsonConverters/OutputAdapterTypeJsonConverter.cs`
- Modify: `src/OpenGisDAF.Infrastructure/JsonConfiguration.cs`
- Modify: `src/OpenGisDAF.PlanManagement/PlanSerializer.cs`
- Modify: `src/OpenGisDAF.Operators/OperatorHelper.cs`
- Modify: `src/OpenGisDAF.Operators/CoordinateTransformOperator.cs`
- Modify: `src/OpenGisDAF.Operators/FieldCalculator.cs`
- Modify: `src/OpenGisDAF.Operators/NullValueFiller.cs`
- Modify: `src/OpenGisDAF.Operators/AttributeCompletenessChecker.cs`
- Modify: `src/OpenGisDAF.Operators/ContainmentCheckOperator.cs`
- Modify: `src/OpenGisDAF.Operators/IntersectCheckOperator.cs`
- Modify: `src/OpenGisDAF.Adapters/Utilities/ConnectionEncryption.cs`
- Modify: `src/OpenGisDAF.Adapters/Utilities/PostgisConnectionHelper.cs`

**Interfaces:**
- Consumes: `AnalysisItem.Parameters: IReadOnlyDictionary<string, object?>`, `OutputAdapterType`, `IConnectionEncryption`.
- Produces: `GetStringParam`, `GetIntParam`, `GetDoubleParam`, `GetBoolParam` that accept CLR values, simple `JsonElement` values and valid numeric strings; converters registered by `JsonConfiguration`.

- [ ] **Step 1: Implement parameter normalization at deserialization.**

The converter must recursively map only JSON scalar values:

```csharp
JsonValueKind.String  -> string
JsonValueKind.True/False -> bool
JsonValueKind.Number  -> long when integral and representable, otherwise double
JsonValueKind.Null    -> null
JsonValueKind.Array/Object -> original JsonElement
```

Keep arrays and objects as `JsonElement` because no existing operator consumes them.

- [ ] **Step 2: Implement shared typed parameter readers.**

Each reader must inspect CLR primitives first, then `JsonElement`, then invariant-culture strings where applicable. Return a typed result or a descriptive `ValidationError`/operator failure containing the parameter name and expected type. Replace direct casts and default fallbacks in all six affected operators.

- [ ] **Step 3: Add adapter type aliases.**

Map case-insensitively:

```text
console | ConsoleWriter       -> ConsoleWriter
geojson | GeoJsonWriter       -> GeoJsonWriter
shapefile | ShapefileWriter   -> ShapefileWriter
postgis | PostGISWriter       -> PostGISWriter
```

Serialization must remain the existing camelCase enum-member spelling, such as `geoJsonWriter` and `postGISWriter`.

- [ ] **Step 4: Make encrypted password failures explicit and safe.**

Validate null/empty and decryption exceptions before building the GDAL connection string. Throw a configuration-specific exception whose message identifies the data source and says the password is invalid or cannot be decrypted; never include password text or the full connection string. Verify the supplied DPAPI ciphertext still decrypts to `postgres`.

- [ ] **Step 5: Run input-focused tests.**

Run:

```powershell
dotnet run --project tests/OpenGisDAF.IntegrationTests
```

Expected: JSON parameter tests, documented adapterType plans, and malformed-password tests pass; metadata/QC/OGU tests remain red.

---

## Task 3: Repair source metadata and output schema propagation

**Files:**
- Add: `src/OpenGisDAF.Core/Interfaces/IFeatureSchemaProvider.cs`
- Add: `src/OpenGisDAF.Core/Models/FeatureSchemaMetadata.cs`
- Modify: `src/OpenGisDAF.Core/Models/OutputSchema.cs`
- Modify: `src/OpenGisDAF.Adapters/InMemoryFeatureSource.cs`
- Modify: all operators creating `InMemoryFeatureSource`
- Modify: `src/OpenGisDAF.Scheduling/SerialScheduler.cs`
- Modify: `src/OpenGisDAF.Adapters/GeoJsonFeatureSink.cs`
- Modify: `src/OpenGisDAF.Adapters/ShapefileFeatureSink.cs`
- Modify: `src/OpenGisDAF.Adapters/PostgisFeatureSink.cs`

**Interfaces:**
- Consumes: `IFeatureSource.Metadata`, `IFeatureSource.SpatialReference`, optional `IFeatureSchemaProvider`, `IFeature` attributes and geometry.
- Produces: `OutputSchema` with `ProducedFields`, `ProducedGeometryType`, and optional `Wkid`; `InMemoryFeatureSource` outputs that retain source metadata.

- [ ] **Step 1: Define the optional schema capability without changing IFeatureSource.**

Use an interface with a single read-only metadata property, for example:

```csharp
public interface IFeatureSchemaProvider
{
    FeatureSchemaMetadata Schema { get; }
}
```

`FeatureSchemaMetadata` must contain `IReadOnlyList<FieldDefinition> Fields`, nullable `GeometryType GeometryType`, and nullable `ISpatialReference SpatialReference`.

- [ ] **Step 2: Make Shapefile and PostGIS sources expose schema metadata.**

Build fields from the OGU layer definitions, preserve DBF types including Date/Integer64/Real, use the layer geometry type mapped to the existing enum, and expose the parsed `.prj`/PostGIS spatial reference. Do not add 3D enum members.

- [ ] **Step 3: Extend InMemoryFeatureSource with optional metadata.**

Add an optional metadata argument while preserving the current constructor call sites. Store fields, geometry type and spatial reference. When no metadata exists, use an explicit unknown CRS rather than pretending EPSG:4326; retain the source CRS when an operator passes through or transforms features.

- [ ] **Step 4: Update operator output construction.**

For each operator, inherit fields and primary-input CRS. Buffer changes only geometry type to Polygon; coordinate transform changes CRS to the target EPSG; clip/intersect/containment and attribute operators preserve the primary input geometry type and CRS. Preserve attributes unless the operator intentionally adds or changes a field.

- [ ] **Step 5: Build scheduler OutputSchema from output sources.**

For an `IFeatureSchemaProvider`, copy its schema. For a third-party source without the capability, materialize one feature enumeration once, infer the attribute union and compatible field types, infer a homogeneous geometry type when possible, and obtain CRS from `IFeatureSource.SpatialReference`. Reuse the materialized/replayable source for both sink writing and downstream caching. Do not pass an empty shell schema.

- [ ] **Step 6: Make all sinks consume actual metadata.**

Remove `GeoJsonFeatureSink`’s `Wkid = 4326`. Set OGU layer WKID from `OutputSchema.Wkid`/spatial reference only when known. Shapefile and PostGIS must use the same schema fields and geometry type; unknown CRS remains unknown and does not become 4326.

- [ ] **Step 7: Run metadata tests against the five real SHP datasets.**

Run the focused integration process and inspect:

```powershell
C:\OSGeo4W\bin\ogrinfo.exe ".artifacts\daf-test\out\p3_tower.shp" -so -al
$env:PGPASSWORD='postgres'; C:\OSGeo4W\bin\psql.exe -h 127.0.0.1 -U postgres -d postgres -A -t -c "select count(*) from public.daf_test_shaft_tower"
```

Expected: source field counts 62/31/63/31/32, Shapefile attributes and `.prj` present, GeoJSON CRS matches EPSG:2326, and PostGIS schema contains attributes.

---

## Task 4: Preserve Z and repair all QC issue paths

**Files:**
- Modify: `src/OpenGisDAF.Adapters/Utilities/WktConverter.cs`
- Modify: `src/OpenGisDAF.Operators/GeometryValidityChecker.cs`
- Modify: `src/OpenGisDAF.Operators/AttributeCompletenessChecker.cs`
- Modify: `src/OpenGisDAF.Operators/IntersectCheckOperator.cs`
- Modify: `src/OpenGisDAF.Operators/ContainmentCheckOperator.cs`
- Modify: `src/OpenGisDAF.Execution/QualityCalculator.cs`
- Modify: `src/OpenGisDAF.Execution/QualityReportGenerator.cs`
- Modify: `src/OpenGisDAF.Infrastructure/JsonConfiguration.cs`
- Modify: `src/OpenGisDAF.Scheduling/SerialScheduler.cs`

**Interfaces:**
- Consumes: `IssueRecord`, `ViolationGeometry`, QC operator outputs, scheduler item statistics.
- Produces: one `IEnumerable<IssueRecord>` issue stream for every QC operator and a report that is valid for empty, null-geometry, invalid-geometry, non-simple, attribute, intersect and containment cases.

- [ ] **Step 1: Configure three-dimensional WKT output.**

Set the existing NTS `WKTWriter.OutputDimension` to 3. Keep two-dimensional geometries two-dimensional according to NTS writer behavior. Add assertions for source `POINT Z`/`LINESTRING Z`, buffer output, every file sink and PostGIS.

- [ ] **Step 2: Convert intersect and containment QC violations to IssueRecord.**

Emit `issues: List<IssueRecord>` in QC mode. If spatial violation features remain useful, emit them under a separate `violations` feature output. Populate `ViolationGeometry` only where appropriate and preserve source/target IDs in `ContextData`. Keep non-QC output behavior compatible.

- [ ] **Step 3: Record actual QC checked counts.**

Have the scheduler record the primary source count before/while executing each QC item. Pass that count to the quality calculation path without changing `ExecutionResult`. Define unique failed features by Error issue and compute `Passed`, `Failed`, and `PassRate` from `TotalChecked`.

- [ ] **Step 4: Make report geometry serialization explicit.**

Add a converter that writes `ViolationGeometry` as WKT and reads it only if report deserialization needs it. Register it for `Geometry` properties. Set UTF-8 relaxed JSON encoding and finite-number handling in report options; do not serialize NTS factory/precision internals.

- [ ] **Step 5: Generate reports for zero issues.**

When a plan contains QC items, create `QcStatistics` even when the issue list is empty, with zero issue counts and checked counts from the scheduler. Generate `{plan}.qc-report.json` in both zero-issue and nonzero-issue cases. For zero checked features, write `TotalChecked=0`, `Passed=0`, `Failed=0`, `PassRate=1.0` and an explicit no-checkable-features condition in report metadata.

- [ ] **Step 6: Run QC focused tests.**

Run:

```powershell
dotnet run --project tests/OpenGisDAF.IntegrationTests
```

Expected: all QC issue types produce valid statistics and reports; no `NaN`, `Infinity`, NTS reflection exception, or silently missing issue stream remains.

---

## Task 5: Repair runtime status, retry, lifecycle and plan management

**Files:**
- Modify: `src/OpenGisDAF.Scheduling/SerialScheduler.cs`
- Modify: `src/OpenGisDAF.Execution/ExecutionEngine.cs` only where cancellation/retry classification requires it
- Modify: `src/OpenGisDAF.Execution/TimeoutController.cs` only if its regression test fails
- Modify: `src/OpenGisDAF.Scheduling/TopologicalSorter.cs` or cycle reporting helper
- Modify: `src/OpenGisDAF.PlanManagement/ServiceCollectionExtensions.cs`
- Modify: `src/OpenGisDAF.PlanManagement/PlanRepository.cs`
- Modify: `src/OpenGisDAF.PlanManagement/PlanValidator.cs`
- Modify: `src/OpenGisDAF.Cli/DafApplication.cs`
- Modify: `src/OpenGisDAF.Infrastructure/HostBuilder.cs`

**Interfaces:**
- Consumes: `PlanExecutionStatistics`, `PerItemStats`, `ExecutionStatus`, `FailurePolicy`, `TimeoutController` result codes.
- Produces: deterministic status accounting, nonzero CLI failure status, one Console sink, stable repository path, safe source disposal and complete DAG cycle diagnostics.

- [ ] **Step 1: Preserve timeout controller behavior with a regression test.**

Assert a timeout returns `Failed + ERR_RT_TIMEOUT` and a caller cancellation returns `Canceled + ERR_RT_CANCELLED`. Assert timeout text includes the configured threshold. Do not modify `TimeoutController` if the test passes.

- [ ] **Step 2: Make retry classification explicit.**

Retry only failed runtime results explicitly marked retryable. Do not retry caller cancellation. Keep constant and exponential delay semantics and include item ID, operator ID, code, attempt and delay in logs.

- [ ] **Step 3: Return failure from CLI run.**

After scheduler completion, return 1 if any item failed or was skipped due to a failed upstream, and return a nonzero result for cancellation, timeout or report generation failure. Return 0 only when all required items completed successfully and report generation succeeded.

- [ ] **Step 4: Fix source identity and disposal.**

Key external source cache entries by stable source ID/path rather than input name. Reuse one source for repeated references and dispose each unique source once in the outer `finally`. Ensure cached/replayable output sources remain valid until downstream consumers finish.

- [ ] **Step 5: Improve DAG cycle diagnostics.**

When DFS finds a back edge, append the repeated starting node to the displayed path, for example `v_cycle_a → v_cycle_b → v_cycle_a`.

- [ ] **Step 6: Stabilize plan repository and draft semantics.**

Resolve the default root path under the application’s user data directory; retain an explicit `AddPlanManagement(rootPath)` override. Let `plan create` produce a named draft with a warning or draft state, make `run` reject empty items clearly, and make `validate` behavior match the documented draft contract.

- [ ] **Step 7: Remove duplicate logging.**

Register the Console sink in exactly one layer. `HostBuilder` should establish base logging and caller configuration should change level/options without attaching a second Console sink. Add a test or captured-log assertion that one event is emitted once.

- [ ] **Step 8: Run orchestration tests.**

Run:

```powershell
dotnet run --project tests/OpenGisDAF.IntegrationTests
```

Expected: StopOnAny and ContinueIndependent semantics remain unchanged, failure/skip exit codes are nonzero, timeout/cancel codes are distinct, source cleanup is once-only, cycle paths close, and logs are not duplicated.

---

## Task 6: Add PostGIS input and complete OGU error propagation

**Files:**
- Modify: `src/OpenGisDAF.Execution/SourceFactory.cs`
- Modify: `src/OpenGisDAF.Adapters/PostgisFeatureSource.cs`
- Modify: `extern/opengis-utils-for-net/src/OpenGIS.Utils/Engine/GdalWriter.cs`
- Add/modify: existing OGU writer test files under `extern/opengis-utils-for-net/tests`

**Interfaces:**
- Consumes: structured `ConnectionConfig`, `DataFormatType.POSTGIS`, OGU writer return/error behavior.
- Produces: plan-driven PostGIS source creation and exceptions for data source, layer and feature write failures.

- [ ] **Step 1: Add a red PostGIS input plan test.**

Construct a plan input with a structured connection binding, not a fake filename extension. Assert `SourceFactory` creates `PostgisFeatureSource`, reads the `daf_test_shaft_tower` table, and reports fields, SRID and geometry dimension.

- [ ] **Step 2: Implement structured source dispatch.**

Dispatch to PostGIS when the binding contains a valid `ConnectionConfig`/PostGIS adapter marker; retain extension dispatch for SHP and GeoJSON. Validate required host, port, database, user and encrypted password before constructing the GDAL `PG:` string.

- [ ] **Step 3: Add OGU writer failure tests.**

Cover failed data source creation, failed layer creation, nonzero `CreateFeature`, and a driver exception. Each test must assert the caller receives an exception containing a safe FID/error summary rather than a Console warning.

- [ ] **Step 4: Implement aggregated write failure propagation in OGU.**

Keep successful feature writes flowing, collect each failed FID and sanitized driver message, and throw after the write operation if any feature failed. Throw immediately for data source or layer creation failure. Do not include passwords or unrestricted connection strings in exception text.

- [ ] **Step 5: Build and test the OGU submodule independently.**

From the submodule root, use its existing project/test commands and require a clean build plus passing writer/CRS tests. Commit the submodule change independently before updating the parent gitlink.

- [ ] **Step 6: Run DAF PostGIS integration tests.**

Before each run:

```powershell
$env:PGPASSWORD='postgres'
C:\OSGeo4W\bin\psql.exe -h 127.0.0.1 -U postgres -d postgres -c "drop table if exists public.daf_test_shaft_tower cascade"
```

After the run, assert row count, source field columns, `ST_SRID=2326`, `ST_NDims=3`, and geometry type. Drop temporary tables after the final inspection snapshot.

---

## Task 7: Restore standard test platform, docs and CI

**Files:**
- Modify: `tests/OpenGisDAF.IntegrationTests/OpenGisDAF.IntegrationTests.csproj`
- Modify: `.github/workflows/ci.yml`
- Modify: `docs/plan-config-guide.md`
- Modify: `docs/cli-reference.md`
- Modify: `docs/operator-reference.md`
- Modify: `README.md` only where the public command/format contract is documented

**Interfaces:**
- Consumes: xunit.v3 4.0 and Microsoft Testing Platform 2.3.3 configuration, corrected CLI behavior and JSON aliases.
- Produces: standard `dotnet test` support and documentation that is executable against the current serializer.

- [ ] **Step 1: Enable Microsoft Testing Platform dotnet-test support.**

Add the project property required by the installed MTP version:

```xml
<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
```

Keep the existing xunit v4 package versions and do not add a second test runner.

- [ ] **Step 2: Update CI to restore, build and test consistently.**

Keep the existing Release sequence, but verify the test target is compatible with the property above:

```yaml
- run: dotnet restore OpenGisDAF.slnx
- run: dotnet build OpenGisDAF.slnx --configuration Release --no-restore
- run: dotnet test OpenGisDAF.slnx --configuration Release --no-build --verbosity normal
```

- [ ] **Step 3: Correct plan JSON documentation.**

Document both accepted adapter forms, with short names as the recommended human-facing form and enum-member camelCase as the serializer output. Document `type` values, encrypted password requirements, PostGIS input structure, 0-based driver FID semantics, QC report location and empty-plan behavior.

- [ ] **Step 4: Run the standard test command.**

Run:

```powershell
dotnet restore OpenGisDAF.slnx
dotnet build OpenGisDAF.slnx --configuration Release --no-restore
dotnet test OpenGisDAF.slnx --configuration Release --no-build
```

Expected: no MTP VSTest error and all existing plus new tests pass.

---

## Task 8: Full real-data verification and final integration

**Files:**
- Modify: only files required by failing verification evidence
- Verify: `.artifacts/daf-test/plans/*.json`, `.artifacts/daf-test/out/*`, `.artifacts/daf-test/reports/*`

**Interfaces:**
- Consumes: all completed DAF and OGU changes, five SHP datasets, local PostGIS credentials and existing plan matrix.
- Produces: final test report, clean Git state except intended source/docs commits, and evidence for every D1–D27 disposition.

- [ ] **Step 1: Run build and standard tests in Release.**

```powershell
dotnet restore OpenGisDAF.slnx
dotnet build OpenGisDAF.slnx --configuration Release --no-restore
dotnet test OpenGisDAF.slnx --configuration Release --no-build
```

Require 0 warnings, 0 errors and all tests passing.

- [ ] **Step 2: Run the existing CLI matrix.**

Run the plans covering documented adapter aliases, all nine operators, DAG chain, StopOnAny, ContinueIndependent, retry, timeout, QC zero/nonzero issues, validator rules, and plan commands. Capture stdout, stderr and exit code separately for each plan.

- [ ] **Step 3: Verify five real SHP inputs.**

For `raw_tunnel`, `raw_portal`, `fresh_tunnel`, `raw_shaft`, and `raw_shaft_tower`, compare counts 181/149/38/37/8, source field counts 62/31/63/31/32, EPSG:2326, Chinese values, null/empty distinction, Date values and 3D coordinates.

- [ ] **Step 4: Verify all sinks.**

Use `ogrinfo` and `psql` to check complete attributes, field types, `.prj`/CRS metadata, row counts, SRID and `ST_NDims=3`. Confirm any write failure causes a failed item and nonzero CLI exit.

- [ ] **Step 5: Verify QC reports.**

Confirm reports exist for zero issues and every issue type, contain finite numbers, readable Chinese, actual `TotalChecked`, and WKT/null `ViolationGeometry`. Confirm intersect and containment issues appear in the aggregate report.

- [ ] **Step 6: Update the final report.**

Write `D:\self\code\opengis-daf\.artifacts\daf-test\reports\TEST-REPORT-FINAL.md` with baseline, changed dependency/submodule commits, task matrix, D1–D27 status, exact reproduction evidence, residual risks and commands used.

- [ ] **Step 7: Perform final repository checks.**

Run:

```powershell
git status --short
git diff --check
git diff --stat
git diff --submodule=log
```

Require `.artifacts` to remain ignored, no unintended files staged, the OGU submodule commit to be recorded by the parent gitlink, and no debug output or unfinished markers in modified source files.

---

## Verification Budget and Completion Criteria

Use one verification pass per implementation phase rather than repeating the full suite after every edit:

1. Focused red/green integration tests after each task.
2. One Debug build plus focused test process after input and metadata phases.
3. One independent OGU build/test pass before updating the parent gitlink.
4. One Release restore/build/standard `dotnet test` pass after CI changes.
5. One complete real-data CLI/PostGIS/OGR pass and one final Git/report inspection.

The work is complete only when all of the following are observable:

- Standard `dotnet test OpenGisDAF.slnx --configuration Release --no-build` runs successfully and all tests pass.
- Documented plans parse and all nine operator parameter contracts work through JSON.
- Five real EPSG:2326 PointZ/LineStringZ SHP datasets preserve counts, attributes, null/empty values, dates, CRS and Z where the target format supports it.
- Shapefile has attributes and `.prj`; GeoJSON CRS agrees with coordinates; PostGIS has attributes, SRID 2326 and three dimensions.
- QC issue types aggregate as `IssueRecord`, quality reports exist for zero and nonzero issues, numbers are finite, Chinese is readable, and checked counts are actual.
- Failed, skipped, canceled, timed-out and report-failed runs return nonzero; successful runs return zero.
- OGU write errors reach DAF and cannot be reported as successful partial writes.
- `git status` shows only intended implementation/design history, `.artifacts` remains ignored, and the final report is present.
