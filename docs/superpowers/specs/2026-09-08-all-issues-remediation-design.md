# OpenGisDAF 全量缺陷修复设计

## 1. 背景与目标

两轮真实 SHP、PostGIS、CLI 和回归测试共确认 D1–D27。升级
OpenGIS.Utils 至 v1.0.6 后，PostGIS 写入和 GeoJSON 属性完整性已有改善，
但 DAF 内部仍存在参数反序列化、输出元数据传播、质检报告、退出码、
测试平台和生命周期问题；OGU 写入器仍需确保底层失败能够传播给调用方。

本次修复覆盖 DAF 主仓库和 `extern/opengis-utils-for-net` 子模块。完成标准是：

- 文档中的 plan JSON 能被 CLI 直接使用，九个内置算子参数均生效。
- Shapefile、GeoJSON、PostGIS 保留属性、真实 CRS 和 Z 值。
- 四类质检算子统一进入统计和报告链路。
- 运行失败不再以成功退出码或虚假成功统计结束。
- 标准 `dotnet test` 和 CI 恢复可用，全部回归测试通过。

## 2. 已确认的范围决策

- 同时修改 DAF 与 OGU；OGU 改动在子模块内独立提交，父仓库只更新 gitlink。
- 不重做 `ExecutionResult` 公共协议，以可选元数据接口完成兼容扩展。
- CLI 方案存在失败或因上游失败而跳过的 item 时返回 1。
- `IssueRecord.FeatureId` 继续表示驱动 FID；接受当前 0-based 行为并注明其跨驱动版本不稳定。
- WKT 输出维度改为 3 以保留 Z，但不扩展 `GeometryType` 枚举。
- 不引入与缺陷修复无关的新功能或重构。

## 3. 总体架构

修复集中在五条边界契约上：

1. **方案输入契约**：JSON 参数归一化、枚举别名、连接配置和 PostGIS 输入。
2. **要素元数据契约**：字段、几何类型和 CRS 从 source 经算子传播至 sink。
3. **质检契约**：所有 QC 算子输出统一的 `IssueRecord` 和实际检查数量。
4. **运行边界契约**：失败、跳过、超时、取消和 CLI 退出码一致。
5. **底层写入契约**：OGU 将数据源、图层和单要素写入失败抛给 DAF。

`ExecutionResult.Outputs` 保持不变。新的元数据通过 source 的可选能力暴露，
避免要求第三方 `IFeatureSource` 实现立刻修改。

## 4. 方案输入契约

### 4.1 参数归一化

`PlanSerializer` 在反序列化边界把 `AnalysisItem.Parameters` 中的简单
`JsonElement` 归一化为 CLR 基元：

| JSON | CLR |
|---|---|
| string | `string` |
| boolean | `bool` |
| integer | 可安全表示的整数类型 |
| number | `double` |
| null | `null` |
| array/object | 保留 `JsonElement` |

`OperatorHelper` 提供统一的 `GetStringParam`、`GetIntParam`、
`GetDoubleParam` 和 `GetBoolParam`。这些读取器同时兼容 JSON 反序列化值、
C# 直接构造值和合法数字字符串。转换失败返回明确配置错误，不抛
`InvalidCastException`，也不静默套用默认值。

### 4.2 枚举兼容

`OutputAdapterType` 的 JSON 转换器接受以下形式：

- `console` / `ConsoleWriter`
- `geojson` / `GeoJsonWriter`
- `shapefile` / `ShapefileWriter`
- `postgis` / `PostGISWriter`

匹配不区分大小写；序列化继续使用当前 camelCase 枚举成员形式，保证 DAF
自产 JSON 可往返。

### 4.3 连接与 PostGIS 输入

- 密文不是有效 DPAPI/AES 数据时返回明确的连接配置错误，不把解密垃圾传给数据库。
- 错误和日志不得包含密码；连接串只允许显示脱敏后的 host、database 和 user。
- `SourceFactory` 支持通过结构化连接配置构造 `PostgisFeatureSource`，不再只按文件扩展名分派。

## 5. 字段、几何和 CRS 数据流

数据流为：

```text
source(features, fields, geometry type, CRS)
  -> operator output source(inherited metadata)
  -> scheduler OutputSchema
  -> GeoJSON / Shapefile / PostGIS sink
```

### 5.1 元数据能力

- 新增小型可选 schema provider，提供字段定义和几何类型。
- `OutputSchema` 增加可选 WKID 或等价空间参考信息。
- `InMemoryFeatureSource` 保存字段、几何类型和 CRS，不再固定返回 SRID 0。
- 算子输出继承主输入的字段和 CRS；改变几何的算子同步更新几何类型。

### 5.2 Scheduler 与 sink

`SerialScheduler.HandleOutputsAsync` 从输出 source 组装真实 `OutputSchema`。
若第三方 source 不提供 schema，则在写出前单次物化并推断属性并集、字段类型、
几何类型和 CRS。sink 不再收到空壳 schema。

- `GeoJsonFeatureSink` 删除 `Wkid = 4326` 硬编码。
- Shapefile 和 PostGIS sink 使用 schema 中的实际 WKID。
- 未知 CRS 保持未知，不按坐标范围猜测，也不伪造 EPSG:4326。
- OGU 将 WKID 分别写入 GeoJSON CRS、Shapefile `.prj` 和 PostGIS SRID。

### 5.3 三维几何

- `WktConverter` 使用三维 WKT writer，二维 geometry 不补 Z。
- 拓扑类型仍用现有 `Point`、`LineString` 等枚举表达。
- 可保留 Z 的驱动必须写出 Z；无法写入时返回明确失败，禁止静默降维。

## 6. 质检契约

### 6.1 统一问题输出

- `geometry_validity_checker` 和 `attribute_completeness_checker` 保持输出 `IssueRecord`。
- `intersect_check` 和 `containment_check` 的 QC 模式改为输出 `IssueRecord`。
- 如仍需空间问题要素，使用独立的 `violations` feature output，不代替 `issues`。
- `SerialScheduler.CollectIssues` 聚合统一的 `IEnumerable<IssueRecord>`。

### 6.2 检查数量与评分

Scheduler 为每个 QC item 记录实际检查的主输入要素数。质量统计定义为：

- `TotalChecked`：实际检查要素数。
- `Failed`：至少有一个 Error 的唯一要素数。
- `Passed = TotalChecked - Failed`。
- `PassRate = Passed / TotalChecked`。
- `TotalChecked = 0` 时三项计数均为 0，`PassRate = 1.0`，报告注明无可检查要素。

### 6.3 报告

- `ViolationGeometry` 用专用 JSON 转换器序列化为 WKT，不反射序列化 NTS 内部对象。
- 方案包含 QC item 时始终生成报告，包括零问题场景。
- 报告使用可读 UTF-8 中文。
- 普通 issue、空几何、非法几何和非简单线均不得导致序列化异常。

## 7. 运行、错误和 CLI 行为

| 场景 | 结果 | CLI `run` |
|---|---|---:|
| 全部 item 成功 | Success | 0 |
| 任一 item 失败 | Failed | 1 |
| 因失败上游而跳过 | 方案失败 | 1 |
| 用户取消 | `ERR_RT_CANCELLED` | 非 0 |
| 超时 | `ERR_RT_TIMEOUT` | 1 |
| 配置、JSON、文件或连接错误 | 不执行或失败 | 1 |
| QC 报告生成失败 | 失败并显示明确错误 | 1 |

- `StopOnAny` 和 `ContinueIndependent` 的调度语义不变。
- 外部取消不进入普通失败重试；只有明确标记为可重试的运行时失败才重试。
- 当前 `TimeoutController` 已区分超时和外部取消，先用回归测试确认；测试通过则不修改实现。
- 重试日志包含 item、算子、错误码、尝试次数和等待时间。
- source 生命周期按稳定绑定标识管理，并统一在方案结束时释放。
- DAG 环错误路径回显闭环起点。

## 8. Plan、日志和测试平台

- `plan create` 生成草稿。`validate` 对空 items 给出警告，`run` 明确拒绝执行空方案。
- 默认方案仓库使用稳定的用户数据目录，同时保留显式 root path 配置。
- `HostBuilder` 和 CLI 合计只注册一个 Console sink。
- 测试项目启用 Microsoft Testing Platform 的 `dotnet test` 支持。
- CI 继续以标准 `dotnet test` 为入口，避免依赖本地规避命令。
- FeatureId 文档明确说明它是驱动 FID，不是业务主键，且不保证跨驱动版本稳定。

## 9. OGU 写入契约

`GdalWriter` 对以下失败均向调用方报告：

1. 数据源创建失败。
2. 图层创建失败。
3. 单个 feature 创建失败。
4. 底层驱动抛出的写入异常。

单 feature 失败记录 FID、错误码和脱敏后的驱动错误；写入结束后有任何失败即抛
聚合异常。DAF sink 因此不能把部分写入报告为成功。PostGIS、Shapefile 和
GeoJSON 使用同一失败判定原则。

子模块先独立构建、测试和提交，父仓库随后更新 gitlink。后续需通过 fork 或维护
补丁确保远端子模块更新不会覆盖该提交。

## 10. 文件级改动

主要改动位置：

- Core：`OutputSchema.cs`、`IssueRecord.cs`、可选 schema provider 接口。
- Adapters：`WktConverter.cs`、`InMemoryFeatureSource.cs`、三个 sink、
  `PostgisFeatureSource.cs`、连接加密实现。
- Operators：`OperatorHelper.cs`、六个有参数算子、四个 QC 算子和改变几何的算子。
- Execution/Scheduling：`SourceFactory.cs`、`QualityCalculator.cs`、
  `QualityReportGenerator.cs`、`SerialScheduler.cs`、DAG 环检测。
- Infrastructure/PlanManagement/CLI：`JsonConfiguration.cs`、`HostBuilder.cs`、
  `PlanSerializer.cs`、方案仓库注册、`DafApplication.cs`。
- Tests/CI/docs：集成测试、测试项目配置、`.github/workflows/ci.yml`、plan/CLI 文档。
- OGU：`GdalWriter.cs`、相关 CRS/PostGIS 工具和回归测试。

禁止创建含糊的 `utils`、`helpers` 或 `service` catch-all 文件；新增文件按单一职责命名。

## 11. 实施顺序

1. **红色回归测试**：参数、四 sink、CRS、Z、PostGIS 输入、QC、退出码、超时、
   方案仓库、OGU 写入失败和标准 `dotnet test`。
2. **输入契约**：参数归一化、统一参数读取、adapterType 别名、连接错误、PostGIS 输入。
3. **元数据链**：source 元数据、算子继承、scheduler schema、sink CRS、Z 和 `.prj`。
4. **QC/运行状态**：统一 issues、实际检查数、报告、退出码、取消/重试和生命周期。
5. **OGU 写入错误**：创建与单 feature 失败传播，DAF 观察到真实失败。
6. **文档和完整回归**：CI、文档、D1–D27 真实数据矩阵和 Git 检查。

## 12. 验证标准

### 12.1 构建和测试

```powershell
dotnet restore OpenGisDAF.slnx
dotnet build OpenGisDAF.slnx --configuration Release --no-restore
dotnet test OpenGisDAF.slnx --configuration Release --no-build
```

要求构建 0 警告、0 错误，标准 `dotnet test` 可执行，既有和新增回归测试全部通过。
OGU 使用其项目规定的独立构建与测试命令。

### 12.2 真实数据

对五个 EPSG:2326、PointZ/LineStringZ SHP 核对：

- 要素数 181、149、38、37、8。
- 属性列与源一致，null、空字符串、中文和 Date 保真。
- Shapefile 生成正确 `.prj`。
- GeoJSON CRS 与坐标一致。
- PostGIS 字段、SRID 和维度正确。
- PointZ/LineStringZ 输出保持三维。

### 12.3 QC 和 CLI

- 零问题、`GEOM_EMPTY`、`GEOM_INVALID`、`GEOM_NOT_SIMPLE`、属性、相交和包含问题均生成报告。
- `TotalChecked` 等于实际检查要素数，中文可读，违规几何可序列化。
- 正常方案返回 0；失败、跳过、取消、超时和报告错误均返回非 0。
- 文档 plan 可被 `validate` 和 `run` 接受。

### 12.4 Git

- `.artifacts` 不进入提交。
- OGU 子模块先有独立提交，父仓库只提交 gitlink 与本仓库预期文件。
- 提交前检查 `git status`、`git diff`、`git diff --submodule` 和子模块状态。

## 13. 非目标

- 不重做 `ExecutionResult`。
- 不新增 3D `GeometryType` 枚举成员。
- 不把 FeatureId 改成可配置业务主键。
- 不引入与缺陷修复无关的新功能或依赖。
