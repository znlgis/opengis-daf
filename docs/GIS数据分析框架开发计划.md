# GIS 数据分析与质检框架 — 开发计划

> 基于需求文档 (`docs/GIS数据分析框架需求文档.md`) 与设计文档 (`docs/GIS数据分析框架设计文档.md`) 综合制定。
> 遵循原则：**先核心后周边 · 先简单后复杂 · 每个里程碑产出可验证交付物**。

---

## 1. 总体目标与原则

### 1.1 总体目标

构建统一的 GIS 数据分析与数据质检框架，以"方案驱动"为核心，将分析与质检统一在一套执行引擎之上。项目分两阶段递进：Phase 1 打通端到端核心链路，Phase 2 扩产增强至生产可用并完善生态。

### 1.2 设计决策约束（来自设计文档）

以下关键决策在制定计划时必须遵守，违反即为规划错误：

| # | 决策点 | 约束 | 影响范围 |
|---|--------|------|----------|
| 1 | Phase 1 仅串联执行 | 不启用并行调度，`MaxParallelism` 等字段保留设计模型但忽略 | 调度引擎 M6 |
| 2 | 版本管理用文件副本 | P1 不建版本数据库，通过 `.bak` 文件实现回退 | 方案管理 M4 |
| 3 | 子方案嵌套是 P2 功能 | `SubPlans` 字段保留，P1 引擎不处理 | 方案模型 M4, 调度 M6 |
| 4 | 算子发现用主动导入 | 不自动扫描插件目录，用户显式指定 DLL | 算子池 M3 |
| 5 | 质检是分析的特化模式 | 通过 `QcMode` 标志切换，不独立建设 QC 引擎 | 执行引擎 M5/M7 |
| 6 | GDAL/OGR 为主 GIS 内核 | 通过 OpenGIS Utils for .NET 封装层接入；NTS 为补充 | 适配器层 M2 |
| 7 | 方案定义为 JSON 文件 | 持久化以文件系统为主，不依赖数据库 | 方案管理 M4 |
| 8 | 算子无状态 | 算子自身不处理重试/跳过，由框架统一裁决 | 算子设计 M3 |

### 1.3 开发原则

- **可交付优先**：每个里程碑结束时必须有可运行、可演示的产出物
- **垂直切片**：M2-M8 在 Phase 1 中形成从"数据源→算子→执行→输出"的完整端到端链路
- **接口先行**：每个层级的接口先定义、先评审，实现后行
- **DI 驱动**：所有组件通过 `Microsoft.Extensions.DependencyInjection` 组装，新增能力 = 新增注册
- **纯配置优先于代码**：行为差异尽量通过 JSON 配置表达，配置表达不了的才写新算子

---

## 2. 阶段总览

```
Phase 1 (MVP)              Phase 2 (增强)
══════════════             ══════════════
M1  项目工程搭建
M2  适配器层                     ┐
M3  算子池+核心算子    ──────────┤
M4  方案管理层                    ├─ M9  并行调度
M5  执行引擎层                    ├─ M10 算子扩展
M6  调度引擎层(串联)   ──────────┤  M11 数据源扩展
M7  质检能力层         ──────────┤  M12 输出扩展
M8  CLI+集成验收       ──────────┤  M13 质检增强
                                  ├─ M14 安全+审计
                                  ├─ M15 性能优化
                                  ├─ M16 模板管理
                                  └─ M17 子方案嵌套
```

**依赖关系**：
- M2→M3：适配器是算子的数据入口，先有数据读取再写算子
- M3→M4：方案引用算子，算子池必须先就绪
- M4→M6：调度引擎需要解析方案，方案管理器先就绪
- M2+M3→M5：执行引擎同时依赖适配器和算子池
- M5+M6→M7：QC 能力叠加在执行和调度之上
- M2-M7→M8：CLI 是各层集成入口
- M6+M9→M17：子方案嵌套依赖调度引擎和并行调度

**可并行的路径**：
- M2（适配器）和 M3（算子池）在接口定义后可以并行开发
- M5（执行引擎）组件 `ExecutionContext`、状态机、超时等可以提前实现
- M9-M17 中无强依赖关系的可以并行推进

---

## 3. Phase 1 — MVP 核心框架 详细计划

> **总工期预估**：14-17 周（3.5-4 个月）
> **目标**：实现可运行的端到端链路，验证七层架构可行性
> **交付物**：可执行的 CLI 工具，能加载方案、读取数据、执行算子、产出结果

### M1：项目工程搭建与基础设施

**工期**：1 周
**目标**：建立解决方案结构、项目文件、核心接口和基础设施层，提前验证外部关键依赖
**前置依赖**：无

#### 产出物

| 产出 | 说明 |
|------|------|
| 解决方案结构 | `.sln` + 各层 `csproj`，清晰分层 |
| 核心接口程序集 | `IOperator` / `IFeatureSource` / `IFeatureSink` / `IFeature` 等基础接口 |
| 领域模型程序集 | `AnalysisPlan` / `AnalysisItem` / `ExecutionResult` 等 record 定义 |
| 基础设施配置 | DI 容器配置模板、结构化日志（`Microsoft.Extensions.Logging`）、JSON 序列化配置 |
| 项目配置 | `.editorconfig`、`Directory.Build.props`、`global.json`（锁定 .NET 10 SDK） |
| 构建脚本 | `build.ps1` / `build.sh`，支持 CI 集成 |
| 依赖评估报告 | OpenGIS Utils for .NET API 可用性评估与 Plan B 预案 |

#### 任务分解

1. **OpenGIS Utils for .NET 预评估**（0.5 天）⚠️ **关键路径任务**
   - 获取 `opengis-utils-for-net` NuGet 包或 GitHub 源码
   - 验证核心 API：Shapefile 读写、PostGIS 读写、GeoJSON 读写、CRS 转换
   - 验证 Windows x64 和 Linux x64（WSL2/Docker）下的兼容性
   - 如关键 API 缺失，确定 Plan B：通过 `MaxRev.Gdal.Core` 或直接 P/Invoke 封装 GDAL C API
   - 产出依赖评估报告，决定 M2 的技术路线

2. **项目结构设计**（0.5 天）
   - 确定程序集划分：`OpenGisDAF.Core`（接口+模型）、`OpenGisDAF.Infrastructure`、`OpenGisDAF.Adapters`、`OpenGisDAF.Operators`、`OpenGisDAF.Execution`、`OpenGisDAF.Scheduling`、`OpenGisDAF.PlanManagement`、`OpenGisDAF.Cli`
   - 确定 NuGet 依赖：`Microsoft.Extensions.DependencyInjection`、`Microsoft.Extensions.Logging`、`System.Text.Json`、`System.Threading.Channels`
   - 创建解决方案文件 `OpenGisDAF.sln`

3. **核心接口定义**（1 天）
   - `IOperator` 接口（含 `Metadata`、`Validate`、`ExecuteAsync`）
   - `IFeature` / `IFeatureSource` / `IFeatureSink` 接口
   - `ISpatialReference` 接口
   - 所有枚举类型（`ExecutionStatus`、`BindingType`、`GeometryType` 等）

4. **领域模型定义**（1 天）
   - `AnalysisPlan` / `AnalysisItem` / `SubPlan`（P1 仅定义不处理）
   - `InputBinding` / `OutputBinding`
   - `ExecutionResult` / `ValidationResult` / `ValidationError`
   - `ExecutionLogEntry` / `IssueRecord`
   - 注意：`System.Text.Json` 序列化枚举需配置 `JsonStringEnumConverter`

5. **基础设施搭建**（1 天）
   - DI host 构建器封装（`HostBuilder` 模式）
   - 结构化日志配置（Console provider + File provider）
   - `System.Text.Json` 全局配置（驼峰命名、枚举字符串化、ISO 8601 时间）
   - 全局异常处理与错误码注册

6. **项目文件配置**（0.5 天）
   - `.editorconfig`（代码风格规范）
   - `Directory.Build.props`（公共属性：Nullable=enable、ImplicitUsings=enable、语言版本=C#14）
   - `global.json`（锁定 .NET 10.0.x SDK）
   - `.gitignore` 更新（排除 `bin/`、`obj/`、`*.user` 等）
   - 核心接口程序集配置 NuGet 包打包（`GeneratePackageOnBuild`），便于第三方开发算子插件

7. **构建验证**（0.5 天）
   - `dotnet build` 全部项目通过
   - 验证分层引用方向正确（上层引用下层，不可反向）

#### 验收标准
- [ ] 解决方案包含 8+ 个项目，层级清晰
- [ ] `dotnet build` 全量编译通过，0 Warning
- [ ] 所有 Core 接口和模型与设计文档 §3、§6 一致
- [ ] DI 容器可成功构建，日志输出到 Console
- [ ] OpenGIS Utils for .NET 评估报告完成，M2 技术路线明确
- [ ] 核心接口程序集 NuGet 包打包配置就绪（`dotnet pack` 产出 `.nupkg`）

---

### M2：数据源/输出适配器层

**工期**：4 周
**目标**：完成统一数据适配层，支持 PostGIS + Shapefile + GeoJSON 读写，Console 输出，CRS 一致性检查与几何降级处理
**前置依赖**：M1（核心接口 + OpenGIS Utils 评估报告）

#### 产出物

| 产出 | 说明 |
|------|------|
| PostGIS 适配器 | `PostGISFeatureSource`，支持空间查询和属性过滤下推 |
| Shapefile 适配器 | `ShapefileFeatureSource`，处理字段名长度限制、`.qix` 索引 |
| GeoJSON 适配器 | `GeoJsonFeatureSource`，支持 FeatureCollection 解析 |
| 内存适配器 | `InMemoryFeatureSource`，用于中间数据传递 |
| PostGIS 输出 | `PostGISFeatureSink`，支持表创建和批量写入 |
| Shapefile 输出 | `ShapefileFeatureSink`，处理字段名截断、多文件打包（.shp+.shx+.dbf+.prj） |
| GeoJSON 输出 | `GeoJsonFeatureSink`，FeatureCollection 序列化输出 |
| Console 输出 | `ConsoleFeatureSink`，开发调试用 |
| 连接配置管理 | `ConnectionConfig` 模型 + 加密接口 `IConnectionEncryption` |
| 字段映射机制 | `FieldMapping` 适配器内部字段映射 |
| CRS 一致性检查 | 框架层面 CRS 收集和一致性校验 |
| WKT 工具类 | `WktConverter` 几何对象与 WKT 互转（算子间几何传递中间格式） |
| 几何降级策略 | 不支持几何类型的自动降级转换（Curve→LineString 等） |

#### 任务分解

> **说明**：OpenGIS Utils for .NET 的 API 评估已前置到 M1。M2 假设评估通过（使用该 NuGet 包），如 M1 确定需要自行封装 GDAL，以下各任务工期需上浮 20%-30%。

1. **连接配置与加密**（0.5 天）
   - 实现 `ConnectionConfig` record
   - 实现基于 .NET DPAPI 的 `ConnectionEncryption`
   - 连接字符串反序列化时自动检测明文并拒绝

2. **PostGIS 适配器（读）**（2 天）
   - `PostGISFeatureSource` 实现 `IFeatureSource`
   - 通过 Npgsql + GDAL OGR PostgreSQL driver 读取
   - `GetFeaturesAsync` 支持 `boundingBox` 下推（`ST_Intersects`）
   - `filterExpression` 解析并转换为 SQL WHERE
   - 几何字段识别（`geometry_columns` 元数据表）
   - CRS 信息从 `geometry_columns` 或 `Find_SRID` 获取

3. **PostGIS 输出适配器（写）**（1.5 天）
   - `PostGISFeatureSink` 实现 `IFeatureSink`
   - 自动创建目标表（基于 `OutputSchema` 推断 DDL）
   - 批量写入优化（`COPY` 或批量 INSERT）
   - 支持追加模式（Append）和覆盖模式（Overwrite）

4. **Shapefile 适配器（读）**（2 天）
   - `ShapefileFeatureSource` 实现 `IFeatureSource`
   - 通过 GDAL OGR Shapefile driver 读取
   - 字段名自动截断处理（Shapefile 10 字符限制）
   - `.qix` 空间索引优先使用
   - CRS 从 `.prj` 文件读取
   - 多几何类型混合的 Shapefile 警告处理

5. **Shapefile 输出适配器（写）**（1 天）
   - `ShapefileFeatureSink` 实现 `IFeatureSink`
   - 字段名自动截断（10 字符限制），中文/超长字段名警告
   - 多文件正确打包（`.shp` + `.shx` + `.dbf` + `.prj`）
   - 单 Shapefile 仅允许一种几何类型，混合类型时拒绝写入并报错

6. **GeoJSON 适配器（读）**（1 天）
   - `GeoJsonFeatureSource` 实现 `IFeatureSource`
   - 支持 `FeatureCollection` 和单 `Feature` 两种结构
   - CRS 从 `crs` 属性读取（如有）
   - P1 阶段先用 `JsonDocument` 全量加载（适用于中小文件），流式解析（`Utf8JsonReader`）推迟到 P2

7. **GeoJSON 输出适配器（写）**（0.5 天）
   - `GeoJsonFeatureSink` 实现 `IFeatureSink`
   - 输出符合 RFC 7946 的 FeatureCollection
   - 支持格式化输出（缩进美化）和紧凑输出（单行）

8. **内存适配器**（0.5 天）
   - `InMemoryFeatureSource`，用于算子间数据传递
   - 支持从 `IList<IFeature>` 构造
   - 可选 NTS STRtree 空间索引加速查询

9. **Console 输出适配器**（0.5 天）
   - `ConsoleFeatureSink` 实现 `IFeatureSink`
   - 支持格式化的表格输出和统计摘要
   - 开发调试用，非生产输出

10. **字段映射机制**（1 天）
    - `FieldMapping` 模型实现
    - 各适配器内部建立源字段→统一字段映射
    - 类型转换器（`Func<object?, object?>`）支持

11. **WKT 工具类**（0.5 天）
    - 实现 `WktConverter`：`ToWkt(Geometry)` / `FromWkt(string)` / `TryParse`
    - 作为算子间几何信息传递的轻量中间格式
    - 集成到 GDAL 和 NTS 两种几何模型

12. **几何降级策略**（0.5 天）
    - 实现设计文档 §5.3.4 的几何类型降级表
    - Curve/CircularString → LineString（线性插值逼近）
    - Surface/CurvePolygon → Polygon（提取外边界）
    - TIN → GeometryCollection（拆分子三角形）
    - 降级时日志输出 Warning（精度损失提示）

13. **CRS 一致性框架**（1 天）
    - 实现 `ISpatialReference`（基于 EPSG Code + WKT）
    - `IsEquivalentTo` 等价判断
    - 框架层面 CRS 收集和一致性校验（集成到方案校验 M4）

14. **单元测试与集成测试**（4 天）
    - 每个适配器的单元测试（Mock 数据）
    - PostGIS 集成测试（Docker 容器：`postgis/postgis:16-3.4`）
    - Shapefile 集成测试（使用标准测试数据）
    - GeoJSON 读写往返测试
    - 分页/流式读取正确性、字段映射正确性
    - 几何降级正确性测试
    - WKT 序列化/反序列化往返测试

#### 验收标准
- [ ] 3 个外部数据源读适配器全部通过集成测试
- [ ] 4 个输出适配器（PostGIS/Shapefile/GeoJSON/Console）全部通过集成测试
- [ ] GeoJSON 读→写往返数据一致
- [ ] 每个适配器正确声明 CRS
- [ ] 分页读取返回正确数量和内容
- [ ] `IAsyncDisposable` 正确释放资源（连接、文件句柄）
- [ ] WKT 互转正确（POINT/LINESTRING/POLYGON/MULTI* 等类型往返无差）
- [ ] 几何降级转换正确，日志输出 Warning

---

### M3：算子池与核心算子

**工期**：3 周
**目标**：完成算子注册/发现/版本管理基础设施，实现 7 个核心算子
**前置依赖**：M1（核心接口）、M2（适配器，仅接口依赖）

#### 产出物

| 产出 | 说明 |
|------|------|
| 算子池 | `IOperatorPool` 实现，含注册、检索、元数据管理 |
| 插件管理 | `IPluginManager` 实现，主动导入 + `AssemblyLoadContext` 隔离 |
| 7 个核心算子 | 覆盖空间关系、空间运算、属性操作、格式转换、质检规则五大类 |
| 算子参数校验 | 基于 `ParameterConstraint` 的自动校验 |
| 算子元数据 | 每个算子的 `OperatorMetadata` 完整定义 |

#### 任务分解

1. **算子池基础设施**（2 天）
   - `OperatorPool` 实现 `IOperatorPool`：注册表、分类索引、标签索引
   - `GetByCategory` / `Search` / `GetById` 查询方法
   - 参数自动校验框架（基于 `ParameterConstraint`）

2. **插件加载器**（2 天）
   - `AssemblyLoadContext` 实现独立加载
   - `IPluginManager` 接口实现：`ImportPlugin`、`SearchPlugins`、`GetPluginVersions`
   - 反射发现 `IOperator` 实现类型
   - 版本识别（文件名后缀或 Assembly 版本）
   - 兼容性校验（`MinFrameworkVersion` 检查）

3. **空间关系算子**（1.5 天）
   - `IntersectCheckOperator`：判断两要素是否相交，输出 `true/false`（分析模式）或问题记录（QC 模式）
   - `ContainmentCheckOperator`：判断要素 A 是否包含要素 B

4. **空间运算算子**（1.5 天）
   - `BufferOperator`：缓冲区分析，参数化缓冲距离
   - `ClipOperator`：裁剪分析，用裁剪面裁剪输入要素

5. **属性操作算子**（1 天）
   - `FieldCalculator`：字段计算（支持简单表达式如 `area * 2`、字符串拼接）
   - `NullValueFiller`：空值填充（默认值或基于规则的填充）

6. **格式转换算子**（1 天）
   - `CoordinateTransformOperator`：坐标系转换（通过 GDAL OSR）
   - （暂不实现格式互转，作为 M2 适配器读→M2 适配器写的能力组合）

7. **质检规则算子**（1.5 天）
   - `AttributeCompletenessChecker`：必填字段非空检查
   - `GeometryValidityChecker`：几何有效性检查（自相交、环方向等）

8. **算子单元测试**（2 天）
   - 每个算子的参数校验测试（合法/非法参数）
   - 每个算子的正常执行测试（使用 InMemoryAdapter mock 数据）
   - QC 模式下的输出格式验证
   - `Validate` 方法返回正确的 `ValidationResult`

#### 验收标准
- [ ] 算子池可正确注册和检索所有 7 个算子
- [ ] 每个算子 `Validate` 对合法/非法参数返回正确结果
- [ ] 每个算子 `ExecuteAsync` 对 mock 数据产出正确结果
- [ ] QC 模式下算子产出 `IssueRecord` 格式正确
- [ ] 插件加载器可正确加载外部 DLL 并隔离

---

### M4：方案管理层

**工期**：2.5 周
**目标**：完成方案 JSON Schema 定义、CRUD 操作、版本管理（文件副本）、Schema 校验和业务规则校验
**前置依赖**：M1（领域模型）、M3（算子池，用于校验算子引用）

#### 产出物

| 产出 | 说明 |
|------|------|
| JSON Schema 定义 | `plan-schema.json`，规范方案配置文件结构 |
| 方案管理器 | `IPlanManager` 实现，CRUD + 导入导出 |
| 方案校验器 | Schema 校验 + 业务规则校验（8 项检查清单） |
| 版本管理器 | 文件副本方式实现版本回退和差异对比 |
| 方案存储 | 文件夹层级组织，JSON 文件持久化 |

#### 任务分解

1. **JSON Schema 定义**（1.5 天）
   - 根据设计文档 §8 的配置模型编写 JSON Schema
   - 定义 `AnalysisPlan` / `AnalysisItem` / `InputBinding`（External/Upstream/SubPlan 三种类型）/ `OutputBinding` / 执行策略的 Schema
   - 必填字段、类型约束、枚举值范围、`additionalProperties: false`
   - 子方案字段 `SubPlans` 定义但标记为可选（P1 不校验内容）
   - P1 阶段优先覆盖核心字段，复杂嵌套校验（如参数约束的 `oneOf` 多态）可在 P2 增强

2. **方案加载与序列化**（1 天）
   - JSON → `AnalysisPlan` 反序列化
   - `AnalysisPlan` → JSON 序列化
   - `System.Text.Json` 自定义转换器：`TimeSpan`（ISO 8601 格式）、枚举（字符串）、敏感字段处理

3. **方案 CRUD**（1 天）
   - 实现 `IPlanManager`：`CreateAsync` / `LoadAsync` / `SaveAsync` / `UpdateAsync` / `CopyAsync` / `ImportAsync` / `ExportAsync`
   - 文件系统存储：按路径组织 `plans/{group}/{name}.json`
   - 方案 ID 自动生成（GUID 或用户指定）

4. **Schema 校验**（1 天）
   - 集成 `JsonSchema.Net` 或 `Newtonsoft.Json.Schema`（选轻量方案）
   - 加载时自动执行 Schema 校验
   - 校验失败返回详细错误位置和原因

5. **业务规则校验**（2 天）
   - 实现 8 项检查清单（设计文档 §5.2.5）：
     - 算子存在性检查：引用 `OperatorId` 必须在算子池中存在
     - 输入绑定完整性：`Inputs` 无空引用或悬挂引用
     - DAG 无环检查：构建依赖图检测循环
     - 参数边界校验：参数值在 `ParameterConstraint` 范围内
     - 输出绑定完整性（Warning）
     - 子方案引用校验（P1 仅检查存在性，不展开）
     - CRS 一致性预检（有/无转换策略）
   - 校验结果返回 `ValidationResult`（Errors + Warnings）

6. **方案版本管理**（1 天）
   - 实现 `IPlanVersionManager`：`GetVersionHistoryAsync` / `RollbackAsync` / `DiffAsync`
   - 保存时自动创建 `.V{n}.bak` 备份文件
   - 回退时复制备份覆盖当前文件
   - 差异对比：JSON diff 算法比较两个版本

7. **方案组织管理**（0.5 天）
   - 支持文件夹层级结构（建议 ≤3 层）
   - 分组信息存储于方案元数据
   - 方案列表支持按名称/分组搜索

8. **测试**（2 天）
   - 合法 JSON 加载测试
   - Schema 校验拒绝非法 JSON 测试（缺字段、类型错误、多余字段）
   - 业务规则校验测试（引用不存在算子、循环依赖、参数越界）
   - 版本回退和差异对比测试

#### 验收标准
- [ ] JSON Schema 覆盖所有必填字段和类型约束
- [ ] 8 项业务规则校验全部实现并通过测试
- [ ] 方案 CRUD 7 个操作全部可用
- [ ] 版本回退可恢复到之前任意版本
- [ ] 方案文件目录结构清晰（`plans/项目名/分类/方案名.json`）

---

### M5：执行引擎层

**工期**：2 周
**目标**：完成算子执行驱动、ExecutionContext、状态机、超时控制、错误处理、结构化日志
**前置依赖**：M1（接口+模型）、M2（IFeatureSource）、M3（算子池）

#### 产出物

| 产出 | 说明 |
|------|------|
| ExecutionContext | 执行上下文，承载中间数据和状态 |
| 状态机 | `Pending → Queued → Executing → Success/Failed/Canceled` |
| 超时控制 | `TimeoutController` 基于 `CancellationTokenSource` |
| 失败重试 | 重试策略（指数退避），仅 `ERR_RT_*` 触发 |
| 结构化日志 | 方案级 + 分析项级日志（可通过配置升级到要素级） |
| 执行统计 | `PlanExecutionStatistics` 收集与输出 |

#### 任务分解

1. **ExecutionContext 实现**（1 天）
   - `ExecutionContext` sealed class 实现
   - `IResultCache` 接口和基础内存实现（P1 用 `ConcurrentDictionary`）
   - 与 DI 容器/日志集成

2. **状态机实现**（1 天）
   - `ExecutionStatus` 枚举的状态流转控制
   - 状态转换合法性校验（如不能从 Success 回到 Executing）
   - 状态变更事件通知（可选，P1 以日志代替）

3. **超时控制**（0.5 天）
   - `TimeoutController.ExecuteWithTimeoutAsync` 实现
   - 超时后返回 `ERR_RT_TIMEOUT` 的 `ExecutionResult`
   - 区分超时取消和外部手动取消

4. **失败重试机制**（1 天）
   - 重试策略：读取 `ItemExecutionPolicy.RetryInterval` 和 `MaxRetries`
   - 指数退避：每次重试间隔翻倍
   - 仅 `ERR_RT_*` 错误触发重试，`ERR_CFG_*`/`ERR_DS_*` 直接失败
   - 状态流转：`Failed → Retrying → Executing`

5. **错误码体系实现**（0.5 天）
   - `ErrorCode` 静态类（设计文档 §6.8）
   - 错误分类与传播策略（配置→拒绝、数据源→终止、运行时→重试、数据→跳过）

6. **执行日志**（1 天）
   - `ExecutionLogEntry` 生成与收集
   - 按 `LogGranularity` 控制日志粒度
   - 日志携带 PlanId / ExecutionId / ItemId 标签
   - 日志持久化（文件输出，通过 `ILoggerProvider` 配置）

7. **执行统计收集**（0.5 天）
   - `PlanExecutionStatistics` 与 `PerItemStats` 自动收集
   - 耗时、要素处理数、成功/失败/跳过计数
   - 执行结束后自动输出统计摘要

8. **算子执行编排**（1 天）
   - `IExecutionEngine` 接口：`ExecuteItemAsync(AnalysisItem, ExecutionContext)`
   - 输入绑定解析：`External` → 创建适配器，`Upstream` → 从缓存取
   - 输出绑定处理：调用对应 `IFeatureSink`
   - 异常捕获与错误码映射

9. **单元测试**（2 天）
   - 状态机流转正确性测试（含非法转换拒绝）
   - 超时控制测试（模拟长时间执行）
   - 重试策略测试（指数退避、超阈值终止）
   - 错误码传播测试

#### 验收标准
- [ ] 算子执行正确：输入→处理→输出完整链路
- [ ] 超时控制正确触发并返回 `ERR_RT_TIMEOUT`
- [ ] 重试策略按预期执行（次数、间隔、退避）
- [ ] 结构化日志包含正确的标签层级
- [ ] 执行统计自动收集并输出

---

### M6：调度引擎层（串联模式）

**工期**：1.5 周
**目标**：完成 DAG 构建、拓扑排序、串联执行编排、优雅关闭
**前置依赖**：M4（方案管理器）、M5（执行引擎）

#### 产出物

| 产出 | 说明 |
|------|------|
| DAG 构建器 | 基于 InputBinding 构建有向无环图 |
| 拓扑排序器 | 计算执行层级和执行顺序 |
| 串联调度器 | 按拓扑顺序依次执行分析项 |
| 优雅关闭 | 取消令牌传播 + 完成当前分析项 |
| 全局并发控制 | `GlobalConcurrencyController`（P1 实现接口，并发数固定为 1） |

#### 任务分解

1. **DAG 构建器**（1.5 天）
   - 解析方案中所有分析项的 `InputBinding`
   - `BindingType.Upstream` → 建立依赖边
   - `BindingType.SubPlan` → P1 标记为不支持并给出明确错误
   - 循环依赖检测（DFS 或 Kahn 算法）
   - 检测到循环返回 `ERR_CFG_DAG_CYCLE`

2. **拓扑排序器**（0.5 天）
   - 基于 Kahn 算法实现拓扑排序
   - 输出执行层级列表（同层分析项无依赖关系）
   - 处理孤立节点（无依赖也无被依赖）

3. **串联调度器**（1.5 天）
   - `ISchedulingEngine` 接口：`ExecuteAsync(AnalysisPlan, CancellationToken)`
   - 按拓扑顺序依次调用执行引擎
   - 上游输出自动注入下游输入绑定（通过 `ExecutionContext.ResultCache`）
   - 失败传播：按 `FailurePolicy` 决策（P1 默认 `StopOnAny`，后续支持 `ContinueIndependent`）
   - `CancellationToken` 向所有执行项传播取消

4. **全局并发控制**（0.5 天）
   - `GlobalConcurrencyController` 实现（P1 并发度为 1）
   - `SemaphoreSlim` 槽位机制
   - 接口保留为 P2 并行调度做准备

5. **优雅关闭**（0.5 天）
   - 接收关闭信号 → 停止接受新任务
   - 等待当前执行中的分析项完成或超时
   - 持久化中断状态（P1 仅日志记录，不实现断点恢复）

6. **集成测试**（1.5 天）
   - 简单串联方案（2-3 个分析项）端到端测试
   - DAG 循环依赖检测测试
   - 取消信号传播测试
   - 上游输出→下游输入的数据流测试

#### 验收标准
- [ ] DAG 正确检测循环依赖并拒绝
- [ ] 串联执行按拓扑顺序完成
- [ ] 上游分析项输出正确传递给下游
- [ ] 取消信号正确传播到所有正在执行的分析项
- [ ] 端到端：JSON 方案文件 → 执行 → 输出结果

---

### M7：质检能力层

**工期**：1.5 周
**目标**：在现有执行引擎上叠加 QC 模式，实现问题清单、质量评分、中间结果保留
**前置依赖**：M5（执行引擎）、M6（调度引擎）

#### 产出物

| 产出 | 说明 |
|------|------|
| QcMode 集成 | `ItemExecutionPolicy.QcMode` 检测与行为切换 |
| 问题清单收集 | `IssueRecord` 收集、聚合与输出 |
| 质量评分 | 加权通过率算法 |
| 质量报告生成 | `QualityReport` 结构化输出 |
| 中间结果保留 | QC 模式下自动保留中间结果 |

#### 任务分解

1. **QcMode 行为切换**（0.5 天）
   - 执行引擎检测 `QcMode = true` 时：
     - 自动设置 `LogGranularity = Feature`
     - 自动设置 `RetainIntermediateResults = true`
   - 中间结果输出到 `intermediate/{planId}/{itemId}/` 路径

2. **问题清单收集**（1 天）
   - 算子输出 `IssueRecord` 的收集管道
   - 执行引擎收集各分析项的问题记录
   - 按分析项/问题类型/严重级别分类聚合

3. **质量评分算法实现**（0.5 天）
   - 加权通过率模型：`Score = Σ(Weight_i × PassRate_i) × 100`
   - `QualityReportConfig` 权重配置读取
   - 除零保护：`max(TotalChecked_i, 1)`

4. **质量报告生成**（1 天）
   - `QualityReport` 组装：TotalScore + RuleStats + Issues + ExecutionMetadata
   - JSON 格式序列化输出
   - 支持按规则/严重级别/要素分组的视图

5. **统计增强**（0.5 天）
   - `QcStatistics` 收集（问题总数、按严重级别分布、按类别分布）
   - `ExecutionMetadata` 写入（PlanVersion + OperatorVersion + DataSourceVersion + ExecutionTime）

6. **测试**（1.5 天）
   - QC 模式端到端测试（方案标记 QcMode → 执行 → 产出 QualityReport）
   - 质量评分计算正确性测试
   - 中间结果保留验证
   - 问题清单完整性验证

#### 验收标准
- [ ] QcMode 标志正确触发细粒度日志和中间结果保留
- [ ] 问题清单包含所有 `IssueRecord`，格式符合设计文档 §9.4
- [ ] 质量评分算法正确计算 0-100 分数
- [ ] `QualityReport` 结构化输出可被工具解析

---

### M8：CLI 工具与集成验收

**工期**：2 周
**目标**：完成 CLI 工具，实现端到端集成验收（含自动化测试），输出 MVP Demo
**前置依赖**：M2-M7 全部完成

#### 产出物

| 产出 | 说明 |
|------|------|
| CLI 工具 | `daf` 命令行（`System.CommandLine` 或手工解析） |
| 基础命令 | `run`、`validate`、`operator list`、`operator import`、`plan list`、`plan create`、`plan copy`、`plan export` |
| 自动化集成测试 | 3 个验收场景的自动化测试脚本，纳入 CI |
| 集成验收方案 | 端到端验收场景与测试数据 |
| 使用文档 | 快速开始指南 + 方案配置指南 + 算子参考 + CLI 命令参考 |
| 平台验证 | Windows x64 + Linux x64 编译运行验证 |

#### 任务分解

1. **CLI 框架搭建**（0.5 天）
   - `System.CommandLine` 集成（.NET 自带）
   - DI 容器初始化（Host 构建）
   - 全局错误处理和退出码

2. **命令实现**（2.5 天）
   - `daf run --plan <path>`：加载方案 → 校验 → 执行 → 输出结果
   - `daf validate --plan <path>`：仅校验方案，不执行（Dry-Run）
   - `daf operator list [--category <name>]`：列出已注册算子（支持 `--category` 筛选）
   - `daf operator import --dll <path>`：导入算子 DLL
   - `daf plan list [--group <name>]`：列出方案（支持分组筛选）
   - `daf plan create --name <name> [--template <id>]`：创建新方案或从模板创建
   - `daf plan copy --source <id> --target <name>`：复制已有方案
   - `daf plan export --plan <id> --output <path>`：导出方案配置
   - 进度输出：执行过程中实时显示状态

3. **端到端验收场景设计**（1 天）
   - **场景 1：简单分析**：Shapefile 读入 → 缓冲区分析 → GeoJSON 输出
   - **场景 2：串联分析**：PostGIS 读入 → 空间过滤 → 字段计算 → 控制台输出
   - **场景 3：质检**：GeoJSON 读入 → 属性完整性检查 → 质量报告（JSON）
   - 每个场景准备测试数据和期望结果

4. **自动化集成测试**（1 天）
   - 将 3 个验收场景编写为自动化集成测试（xUnit）
   - 测试覆盖：输入→执行→输出结果的断言验证
   - 集成到 CI 流水线（`dotnet test`），确保回归保护
   - 测试数据使用项目内置的小样本数据集，不依赖外部数据库

5. **集成验收执行**（1.5 天）
   - 按场景顺序执行验收，记录结果
   - 修复发现的问题
   - 性能基线测试：单算子处理速率是否 ≥ 1000 要素/秒
   - 可靠性测试：连续 10 次执行无异常崩溃

6. **平台验证**（0.5 天）
   - Windows x64：编译 + 运行全部验收场景
   - Linux x64（WSL2 或 Docker）：编译 + 运行全部验收场景
   - Docker 镜像构建脚本

7. **文档**（1.5 天）
   - README 补充：快速开始、环境要求、安装步骤
   - 方案配置指南：JSON 结构说明、各字段含义与示例
   - 算子参考：P1 阶段 7 个算子的参数说明和用法示例
   - CLI 命令参考：所有命令的完整帮助信息
   - 不建独立文档网站，追加到现有 README 和 `docs/` 目录中

#### 验收标准
- [ ] 8 个 CLI 命令全部可用
- [ ] 3 个验收场景全部通过
- [ ] 自动化集成测试全部通过（3 个场景 × 断言验证）
- [ ] Windows x64 和 Linux x64 双平台验证通过
- [ ] 单算子处理速率 ≥ 1000 要素/秒（简单属性操作）
- [ ] 连续 10 次执行无崩溃
- [ ] 文档覆盖快速开始、方案配置、算子参考、CLI 参考四大模块

---

## 4. Phase 2 — 功能增强 详细计划

> **总工期预估**：13-17 周（3.25-4.25 个月）
> **目标**：多核算力利用、算子体系扩展、生产级适配器、安全与审计、子方案嵌套
> **前置条件**：Phase 1 全部里程碑完成且验证通过

### M9：并行调度引擎

**工期**：2 周 · **前置**：M6

| 任务 | 工期 |
|------|------|
| 拓扑层级识别：识别可并行的同层分析项 | 0.5 天 |
| `SemaphoreSlim` 并发控制（`PlanExecutionPolicy.MaxParallelism`） | 0.5 天 |
| 汇合点等待：等待同层全部完成才进入下一层 | 1 天 |
| `FailurePolicy.ContinueIndependent` 实现 | 0.5 天 |
| 全局并发控制（`GlobalConcurrencyController` 多方案并行） | 0.5 天 |
| `Channel<T>` 有界管道实现流水线传递 | 0.5 天 |
| 背压控制（Channel 容量达到上限阻塞生产端） | 0.5 天 |
| 并行执行统计（CPU 利用率、并发度） | 0.5 天 |
| 集成测试（多种 DAG 拓扑的并行执行） | 2 天 |

**验收**：同拓扑层分析项真正并行执行，`MaxParallelism=4` 时 4 个无依赖项同时执行，汇合点正确等待

### M10：算子扩展（20+）

**工期**：3 周 · **前置**：M3

新增算子覆盖：

| 类别 | 新增算子 | 工期 |
|------|---------|------|
| 空间关系 | 相邻判断 (`AdjacencyCheck`)、距离判断 (`DistanceCheck`) | 1.5 天 |
| 空间运算 | 叠加分析 (`OverlayAnalysis`)、合并 (`Union`)、差集 (`Difference`) | 2 天 |
| 空间连接 | 空间关联 (`SpatialJoin`)、最近邻连接 (`NearestNeighbor`) | 2 天 |
| 统计分析 | 分组统计 (`GroupStatistics`)、密度分析 (`DensityAnalysis`)、热点分析 (`HotSpotAnalysis`) | 2 天 |
| 质检规则 | 拓扑检查 (`TopologyChecker`：面重叠、线闭合)、重复检查 (`DuplicateChecker`)、精度检查 (`PrecisionChecker`) | 2.5 天 |
| 属性操作 | 条件赋值 (`ConditionalAssignment`)、属性映射 (`AttributeMapper`) | 1 天 |
| 测试 | 每个新算子的单元测试和集成测试 | 2 天 |

**验收**：算子池注册算子 ≥ 20 个，覆盖全部 7 大类，每个算子通过测试

### M11：数据源扩展

**工期**：2.5 周 · **前置**：M2

| 新增适配器 | 工期 |
|-----------|------|
| Oracle Spatial 适配器（通过 Oracle.ManagedDataAccess + GDAL OGR OCI driver） | 2 天 |
| SQL Server 适配器（通过 Microsoft.Data.SqlClient + geometry/geography 类型） | 2 天 |
| MySQL 8.0+ 适配器（通过 MySqlConnector + InnoDB R-tree） | 2 天 |
| WFS 适配器（HTTP 客户端 + GML 解析，仅读取） | 2 天 |
| InMemory 适配器增强（支持溢出到临时文件、LRU 淘汰） | 1 天 |
| 集成测试（Docker 容器化数据库 + WFS 测试服务） | 2 天 |

**验收**：5 个新适配器全部通过集成测试，CRS 正确声明

### M12：输出适配器扩展

**工期**：1.5 周 · **前置**：M2

| 新增适配器 | 工期 |
|-----------|------|
| GeoPackage 输出（GDAL OGR GPKG driver，含空间索引写入） | 1.5 天 |
| CSV 输出（属性数据导出，含表头、编码选择、经纬度列） | 1 天 |
| Shapefile 输出（字段名自动截断、多文件打包 `.shp+.shx+.dbf+.prj`） | 1 天 |
| HTML 报告输出（`HtmlReportWriter`，质检报告可视化） | 1.5 天 |
| 输出字段选择增强（白名单/黑名单、字段重命名） | 0.5 天 |
| 集成测试 | 1 天 |

**验收**：每个新增输出适配器通过集成测试，输出文件可被对应工具正确打开

### M13：质检增强

**工期**：2 周 · **前置**：M7、M4

| 任务 | 工期 |
|------|------|
| 版本追溯对比（`IVersionDiffService` 实现，跨执行结果的质量趋势分析） | 2 天 |
| `QualityReportDiff` 生成：问题新增/消除/变化统计 | 1 天 |
| 差异对比增强：原始数据 vs 质检后数据的差异对比 | 1.5 天 |
| 质量趋势分析：多次执行的评分趋势图和变化检测 | 1 天 |
| 质检报告增强（图表嵌入：通过纯文本或链接方式，不依赖图形库） | 1 天 |
| 测试 | 1.5 天 |

**验收**：跨版本质检结果对比可用，质量趋势分析可检测评分变化

### M14：安全与审计

**工期**：2 周 · **前置**：M1（基础设施）

| 任务 | 工期 |
|------|------|
| `ISecretManager` 实现（DPAPI 加密 + 脱敏展示 `MaskForDisplay`） | 1 天 |
| 连接字符串加密（运行时解密到内存，持久化保持密文） | 1 天 |
| 方案配置中敏感字段脱敏展示 | 0.5 天 |
| API Key 认证（`IAuthService` 最小实现，配置文件管理 Key） | 1.5 天 |
| 审计日志（`IAuditService`：方案创建/修改/删除/执行，追加写入不可篡改） | 1.5 天 |
| 安全错误码补充（`ERR_SEC_*`） | 0.5 天 |
| 测试 | 1 天 |

**验收**：敏感信息在日志和存储中保持加密/脱敏，审计日志记录关键操作

### M15：性能优化

**工期**：2 周 · **前置**：M9（并行调度）、M10（算子）

| 任务 | 工期 |
|------|------|
| 结果缓存增强（`IResultCache` 基于输入哈希的缓存键，支持 TTL 和 LRU 淘汰） | 1.5 天 |
| 数据分区策略（`IPartitionStrategy`：按空间范围网格分区 + 按属性值分区） | 1.5 天 |
| 分区并行执行（调度引擎集成：自动分区→并行执行→合并结果） | 1.5 天 |
| 空间索引增强（NTS STRtree 集成到内存操作中） | 1 天 |
| 性能基准测试（100 万要素空间连接 ≤ 60s，内存峰值 ≤ 2GB） | 1.5 天 |

**验收**：关键性能指标达到或接近需求文档 §8.1 的基线

### M16：模板管理

**工期**：1.5 周 · **前置**：M4（方案管理器）

| 任务 | 工期 |
|------|------|
| `IPlanTemplateManager` 实现（`CreateFromTemplateAsync`、`SaveAsTemplateAsync`） | 1.5 天 |
| 模板占位符系统（`"type": "placeholder", "key": "target_dataset"`） | 1 天 |
| 模板与方案文件格式统一（仅存储位置不同：`templates/` vs `plans/`） | 0.5 天 |
| 模板导入导出 | 0.5 天 |
| CLI 命令扩展（`daf template list`、`daf template create-from`） | 1 天 |
| 测试 | 1 天 |

**验收**：模板 → 指定数据源 → 生成可执行方案 的完整链路可用

---

### M17：子方案嵌套

**工期**：3 周 · **前置**：M6、M9

| 任务 | 工期 |
|------|------|
| 递归方案解析（深度限制，防止无限嵌套） | 1.5 天 |
| 子方案上下文隔离（独立 ExecutionContext 或子上下文） | 1 天 |
| 子方案结果回传到父方案（通过 `BindingType.SubPlan`） | 1 天 |
| 循环嵌套检测（方案 A 引用方案 B，方案 B 引用方案 A） | 0.5 天 |
| 集成测试 | 1.5 天 |

**验收**：方案 A 引用方案 B，B 完成后结果正确注入 A；循环嵌套被正确检测并拒绝

---

## 5. 风险与缓解

### 5.1 技术风险

| # | 风险 | 概率 | 影响 | 缓解措施 |
|---|------|------|------|----------|
| T1 | OpenGIS Utils for .NET API 不完整，需自行封装 GDAL C API | 高 | 高 | M1 提前评估，预留自行封装的时间和人力；评估结果确定 M2 技术路线；如不可用则通过 `MaxRev.Gdal.Core` 或直接 P/Invoke 封装替代 |
| T2 | GDAL 在 Linux ARM64 环境下编译/运行问题 | 中 | 中 | P1 目标平台仅 x64，ARM64 在 P2 验证；容器化环境先行测试 |
| T3 | NTS 与 GDAL 几何模型互转性能瓶颈 | 低 | 中 | 优先在同一 GIS 内核内部完成计算，减少跨内核转换；转换路径性能测试纳入 M8 |
| T4 | `IAsyncEnumerable` + Channel 背压在大数据量下表现不佳 | 低 | 中 | Channel 容量可配置，P1 以串联执行压力低；P2 并行时做压测调优 |
| T5 | `System.Text.Json` 对复杂多态类型（如 `object?` 参数值）序列化支持不足 | 中 | 低 | 使用自定义 `JsonConverter`，参考 .NET 9+ 的多态序列化增强 |

### 5.2 进度风险

| # | 风险 | 概率 | 影响 | 缓解措施 |
|---|------|------|------|----------|
| P1 | Phase 1 算子开发工作量被低估（7 个算子需要大量 GIS 算法实现） | 中 | 高 | 优先保证 5 个最核心算子（Buffer、IntersectCheck、FieldCalculator、AttributeCompleteness、CoordinateTransform），其余 2 个作为 P1 延后项 |
| P2 | 数据源适配器测试环境搭建耗时（Docker + 测试数据准备） | 高 | 中 | 提前准备 Docker Compose 文件和标准化测试数据集；CI 中自动化部署测试环境 |
| P3 | 跨平台验证问题（Linux 环境 GDAL 路径、权限等差异） | 中 | 中 | M8.5 尽早启动 Linux 验证，不等到最后；使用 Docker 保证环境一致性 |
| P4 | Phase 2 并行调度引入的并发 Bug（死锁、竞态条件） | 中 | 高 | M9 阶段严格 TDD，状态机用不可变模型；`Channel<T>` 和 `SemaphoreSlim` 的正确使用已有成熟模式 |

### 5.3 设计风险

| # | 风险 | 概率 | 影响 | 缓解措施 |
|---|------|------|------|----------|
| D1 | 设计文档接口与实际实现有偏差，导致返工 | 中 | 中 | 每个 Milestone 开始前评审相关接口定义；M1 定义的接口在 M2-M7 中使用中发现问题的及时反馈修正 |
| D2 | 方案 JSON Schema 频繁变更导致已有方案文件不兼容 | 低 | 中 | Schema 版本号机制（设计文档 §8.4）；P1 期间冻结 Schema 主版本 |
| D3 | QC 模式与分析模式共享引擎但行为差异多，代码分支复杂 | 低 | 中 | 通过策略模式（`ItemExecutionPolicy`）而非 if-else 控制差异；算子层面通过 `ExecutionContext` 信息自行适配输出 |

---

## 6. 资源与人力估算

### 6.1 人力配置建议

| 阶段 | 工期 | 建议人数 | 角色 |
|------|------|----------|------|
| Phase 1 | 14-17 周 | 2-3 人 | 1 名架构/全栈 + 1 名 GIS 后端 + 可选 1 名测试/工具 |
| Phase 2 | 13-17 周 | 3-4 人 | +1 名后端（算子扩展并行） |

### 6.2 关键依赖外部资源

| 依赖 | 用途 | 获取方式 |
|------|------|----------|
| OpenGIS Utils for .NET | GDAL/OGR 封装层 | NuGet (`opengis-utils-for-net`) 或 GitHub |
| Npgsql | PostGIS 数据库连接 | NuGet |
| NetTopologySuite | 轻量空间计算补充 | NuGet |
| JsonSchema.Net | JSON Schema 校验 | NuGet |
| System.CommandLine | CLI 框架 | .NET 自带 |
| Docker | 集成测试环境（PostGIS、各数据库） | 公共镜像 |

### 6.3 里程碑资源分配总览

```
Phase 1 工时分饼（按 17 周上限估算）：
  M1  项目搭建      ███░░░░░░░  6%
  M2  适配器层      ████████░░ 24%  ← 最大单块（含读写适配器+WKT+几何降级）
  M3  算子池+算子    ███████░░░ 18%
  M4  方案管理      █████░░░░░ 15%
  M5  执行引擎      ████░░░░░░ 12%
  M6  调度引擎      ███░░░░░░░  9%
  M7  质检能力      ███░░░░░░░  9%
  M8  CLI+验收      ██░░░░░░░░  7%
```

---

## 7. 附录：Phase 1 验收检查清单

### 7.1 功能验收

- [ ] 支持 PostGIS、Shapefile、GeoJSON 三种数据源读取
- [ ] 支持 Console、PostGIS、Shapefile、GeoJSON 四种输出，读写往返数据一致
- [ ] WKT 几何对象互转正确（POINT/LINESTRING/POLYGON/MULTI* 等类型）
- [ ] 几何降级策略可用（Curve→LineString 等），日志输出 Warning
- [ ] 算子池注册 7 个核心算子，按分类可检索
- [ ] 方案 JSON Schema 校验 + 8 项业务规则校验
- [ ] 方案 CRUD（创建/加载/保存/更新/复制/导入/导出）全部可用
- [ ] 方案版本回退可用（通过文件副本）
- [ ] 串联执行：按 DAG 拓扑顺序依次执行分析项
- [ ] 超时控制：超时自动终止并返回错误
- [ ] 失败重试：运行时错误按策略重试
- [ ] QC 模式：产出结构化 `QualityReport`
- [ ] 质量评分 0-100 可用
- [ ] 结构化日志（方案级 + 分析项级 + QC 要素级）
- [ ] CLI 命令全部可用（`run`、`validate`、`operator list`、`operator import`、`plan list`、`plan create`、`plan copy`、`plan export`）
- [ ] 自动化集成测试 3 个场景全部通过，纳入 CI

### 7.2 非功能验收

- [ ] Windows x64 编译运行通过
- [ ] Linux x64 编译运行通过
- [ ] 单算子处理速率 ≥ 1000 要素/秒（简单属性操作）
- [ ] 框架冷启动时间 ≤ 3 秒
- [ ] 百万级要素场景内存 ≤ 2GB

### 7.3 质量验收

- [ ] 所有公共接口/方法有单元测试覆盖（≥ 70% 行覆盖率）
- [ ] 集成测试覆盖 3 个验收场景，自动化集成测试纳入 CI 流水线
- [ ] 无已知内存泄漏（通过 `IAsyncDisposable` 正确释放资源）
- [ ] 无未处理的异常导致进程崩溃
- [ ] 日志输出包含正确的上下文标签
- [ ] 文档覆盖快速开始、方案配置指南、算子参考、CLI 命令参考
- [ ] 核心接口程序集 NuGet 包可正常打包（`dotnet pack`）

---

*文档版本：V1.1 · 修订日期：2026-07-02 · 修订内容：补齐输出适配器、前置依赖评估、增强测试与文档*
