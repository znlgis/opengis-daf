# GIS 数据分析与质检框架设计文档

> 对应需求文档：[/docs/GIS数据分析框架需求文档.md](./GIS数据分析框架需求文档.md)

## 1. 文档目标

本文档聚焦框架的技术设计，说明总体架构、核心模型、接口边界、执行机制和关键技术决策。  
业务目标、功能范围、非功能要求和实施优先级见需求文档。

## 2. 技术设计原则

### 2.1 技术栈

| 技术要素 | 选型 | 说明 |
|----------|------|------|
| 开发语言 | C# 12 | 利用现代语言特性提升可维护性 |
| 运行框架 | .NET 8 | 跨平台、高性能、长期支持 |
| GIS 内核 | NetTopologySuite (NTS) | .NET 生态成熟的空间计算库 |
| 依赖注入 | Microsoft.Extensions.DependencyInjection | 与 .NET 生态一致 |
| 序列化 | System.Text.Json | 方案配置与元数据处理 |
| 异步编程 | async/await + Channel + IAsyncEnumerable | 支持流式和并行处理 |
| 日志 | Microsoft.Extensions.Logging | 统一结构化日志 |

### 2.2 关键设计决策

| 决策点 | 选择 | 原因 |
|--------|------|------|
| 执行模型 | DAG + 拓扑排序 | 轻量、易推导并行度、适合数据流场景 |
| 处理方式 | 异步流式处理 | 控制内存占用，适应大数据集 |
| 方案定义 | JSON Schema | 便于校验、生成工具支持好 |
| 扩展方式 | 配置驱动为主，代码扩展为辅 | 兼顾可视化配置和开发扩展 |
| 插件隔离 | AssemblyLoadContext | 支持插件加载和隔离 |

### 2.3 设计约束

- 算子必须保持无状态，执行状态统一通过 `ExecutionContext` 传递。
- `IFeatureSource` 与 `IFeatureSink` 生命周期由 DI 容器管理。
- 方案配置在执行期间保持不可变，保证可追溯性。
- 数据流依赖必须可构建为无环图，否则方案校验失败。

## 3. 领域模型设计

### 3.1 核心对象

| 对象 | 作用 |
|------|------|
| Operator | 可复用分析能力的最小单元 |
| Analysis Item | 算子在具体场景下的配置实例 |
| Analysis Plan | 由多个分析项和子方案构成的执行单元 |
| FeatureSource | 统一的数据读取抽象 |
| FeatureSink | 统一的数据输出抽象 |
| ExecutionContext | 单次执行的上下文与共享状态 |

### 3.2 关系说明

- `AnalysisPlan` 包含多个 `AnalysisItem`，也可包含子方案。
- `AnalysisItem` 通过 `OperatorId` 绑定具体算子。
- 分析项输入可来自外部数据源、上游分析项结果或子方案输出。
- 调度引擎基于输入绑定关系构建 DAG，并据此执行。

## 4. 总体架构

```text
┌─────────────────────────────────────────────────────┐
│                    API / CLI 层                     │
├─────────────────────────────────────────────────────┤
│               方案管理层 (Plan Manager)              │
├─────────────────────────────────────────────────────┤
│             调度引擎层 (Scheduling Engine)           │
├─────────────────────────────────────────────────────┤
│               执行引擎层 (Execution Engine)          │
├─────────────────────────────────────────────────────┤
│                 算子池 (Operator Pool)               │
├─────────────────────────────────────────────────────┤
│        数据源/输出适配层 (Source & Sink Adapter)     │
├─────────────────────────────────────────────────────┤
│         基础设施层 (日志、缓存、配置、监控、安全)     │
└─────────────────────────────────────────────────────┘
```

### 4.1 方案管理层

负责方案解析、Schema 校验、业务规则校验、模板管理、版本管理和持久化。

### 4.2 调度引擎

负责根据数据依赖构建执行图、检测循环依赖、计算拓扑层级、控制并发和状态流转。

### 4.3 执行引擎

负责驱动算子执行、传递上下文、管理结果缓存、收集日志和执行结果。

### 4.4 适配器层

通过统一接口屏蔽不同数据源和输出目标差异，支持后续持续扩展。

## 5. 组件设计

### 5.1 算子池

职责：

- 注册与发现算子
- 维护算子元数据
- 按分类和能力检索算子
- 管理算子版本

注册方式：

```csharp
services.AddOperatorsFromAssembly(typeof(BufferOperator).Assembly);
services.AddOperator<BufferOperator>("spatial.buffer");
services.AddOperator<IntersectOperator>("spatial.intersect");
```

### 5.2 方案管理器

职责：

- 解析 JSON 方案配置
- 执行 Schema 与业务规则校验
- 管理方案模板和版本
- 将方案转换为运行时模型

### 5.3 数据源适配器

已规划适配器：

| 适配器 | 数据源 |
|--------|--------|
| PostGISAdapter | PostgreSQL + PostGIS |
| ShapefileAdapter | Shapefile |
| GdbAdapter | File / Enterprise GDB |
| GeoJsonAdapter | GeoJSON |
| WfsAdapter | OGC WFS |
| InMemoryAdapter | 中间结果缓存 |

统一要求：

- 所有适配器实现 `IFeatureSource`
- 必须提供空间参考信息
- 支持分页或流式读取
- 允许将过滤条件尽可能下推到数据源

### 5.4 输出适配器

| 适配器 | 输出目标 |
|--------|----------|
| PostGISWriter | PostgreSQL 表 |
| ShapefileWriter | Shapefile 文件 |
| GeoJsonWriter | GeoJSON 文件或 HTTP 响应 |
| CsvWriter | CSV 报表 |
| GeoPackageWriter | GeoPackage |
| ConsoleWriter | 调试输出 |

### 5.5 插件扩展

- 算子插件实现 `IOperator`
- 数据源插件实现 `IFeatureSource`
- 输出插件实现 `IFeatureSink`
- 通过 `AssemblyLoadContext` 实现插件发现、隔离和卸载

## 6. 核心接口设计

### 6.1 算子接口

```csharp
public sealed record OperatorMetadata
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string Category { get; init; }
    public string Description { get; init; }
    public IReadOnlyList<ParameterDefinition> Parameters { get; init; }
    public InputSchema InputSchema { get; init; }
    public OutputSchema OutputSchema { get; init; }
}

public interface IOperator
{
    OperatorMetadata Metadata { get; }
    ValidationResult Validate(AnalysisItemConfig config);
    Task<ExecutionResult> ExecuteAsync(
        IReadOnlyDictionary<string, IFeatureSource> inputs,
        IReadOnlyDictionary<string, object?> parameters,
        ExecutionContext context,
        CancellationToken cancellationToken);
}
```

### 6.2 分析项与方案

```csharp
public sealed class AnalysisItem
{
    public string Id { get; init; }
    public string OperatorId { get; init; }
    public IReadOnlyDictionary<string, InputBinding> Inputs { get; init; }
    public IReadOnlyDictionary<string, object?> Parameters { get; init; }
    public OutputBinding Output { get; init; }
    public ItemExecutionPolicy ExecutionPolicy { get; init; }
}

public sealed class AnalysisPlan
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string Version { get; init; }
    public PlanConfig Config { get; init; }
    public IReadOnlyList<AnalysisItem> Items { get; init; }
    public IReadOnlyList<AnalysisPlan> SubPlans { get; init; }
    public PlanExecutionPolicy ExecutionPolicy { get; init; }
}
```

### 6.3 数据源与输出

```csharp
public interface IFeature
{
    string Id { get; }
    Geometry Geometry { get; }
    IReadOnlyDictionary<string, object?> Attributes { get; }
}

public interface IFeatureSource
{
    FeatureSourceMetadata Metadata { get; }
    Envelope BoundingBox { get; }
    ISpatialReference SpatialReference { get; }
    Task<long> GetFeatureCountAsync();
    IAsyncEnumerable<IFeature> GetFeaturesAsync(
        Envelope? boundingBox = null,
        string? filterExpression = null,
        CancellationToken cancellationToken = default);
}

public interface IFeatureSink
{
    Task InitializeAsync(OutputSchema schema, CancellationToken cancellationToken);
    Task WriteAsync(IFeature feature, CancellationToken cancellationToken);
    Task WriteBatchAsync(IAsyncEnumerable<IFeature> features, CancellationToken cancellationToken);
    Task CompleteAsync(CancellationToken cancellationToken);
}
```

### 6.4 执行结果与校验结果

```csharp
public sealed record ExecutionResult
{
    public ExecutionStatus Status { get; init; }
    public IReadOnlyDictionary<string, IFeatureSource> Outputs { get; init; }
    public IReadOnlyList<ExecutionLogEntry> Logs { get; init; }
    public TimeSpan Elapsed { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record ValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<ValidationError> Errors { get; init; }
}
```

## 7. 执行模型设计

### 7.1 DAG 构建

执行流程：

1. 解析方案中的输入绑定
2. 基于 `FromUpstream` 和 `FromSubPlan` 建立依赖边
3. 检测循环依赖
4. 计算拓扑排序和执行层级
5. 生成可调度执行计划

### 7.2 串并行调度

- 同一拓扑层中的分析项可并行执行
- 不同层按依赖顺序执行
- 并行度由 `PlanConfig.MaxParallelism` 控制
- 调度器负责资源分配、状态更新和失败传播

### 7.3 执行上下文

```csharp
public sealed class ExecutionContext
{
    public string PlanId { get; init; }
    public string ExecutionId { get; init; }
    public IResultCache ResultCache { get; init; }
    public ILogger Logger { get; init; }
    public IServiceProvider Services { get; init; }
    public CancellationToken CancellationToken { get; init; }
    public ExecutionStatistics Statistics { get; init; }
}
```

### 7.4 状态机

分析项生命周期：

`Pending -> Queued -> Executing -> Success/Failed/Canceled`

失败后可依据重试策略回到 `Retrying -> Executing`，超过阈值后进入最终失败状态。

### 7.5 并发与背压

- 使用 `SemaphoreSlim` 控制执行并发度
- 使用有界 `Channel<T>` 承接流水线数据，避免内存失控
- 当下游阻塞或资源压力过高时，对上游形成自然背压

### 7.6 优雅关闭

- 停止接受新的调度任务
- 向执行中的算子传播取消令牌
- 持久化必要的中间结果和检查点
- 支持后续恢复执行

## 8. 配置模型设计

### 8.1 方案配置结构

方案配置至少包含以下部分：

- 基本信息：`id`、`name`、`version`
- 运行配置：`config`
- 执行策略：`executionPolicy`
- 子方案：`subPlans`
- 分析项集合：`items`

### 8.2 输入绑定模型

| 类型 | 说明 |
|------|------|
| external | 从外部数据源读取 |
| upstream | 引用上游分析项输出 |
| subPlan | 引用子方案输出 |

### 8.3 输出绑定模型

输出定义至少包含：

- 输出适配器类型
- 输出目标位置
- 可选字段裁剪
- 可选格式说明

## 9. 质检能力设计

### 9.1 质检模式

质检模式是普通分析模式的增强版本，增加：

- 更细粒度日志
- 中间结果保留
- 差异对比
- 数据血缘
- 质量评分

### 9.2 关键模型

```csharp
public sealed record ExecutionLogEntry
{
    public string ExecutionId { get; init; }
    public string ItemId { get; init; }
    public string OperatorId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public LogLevel Level { get; init; }
    public string Message { get; init; }
    public string? FeatureId { get; init; }
}

public sealed record LineageRecord
{
    public string ExecutionId { get; init; }
    public string ItemId { get; init; }
    public string OperatorId { get; init; }
    public IReadOnlyList<string> SourceFeatureIds { get; init; }
    public string OutputFeatureId { get; init; }
    public string TransformType { get; init; }
}
```

## 10. 空间参考与几何处理

### 10.1 空间参考

- 每个 `FeatureSource` 必须声明 CRS
- 执行前检查输入数据 CRS 一致性
- 必要时通过 ProjNet 执行自动转换

### 10.2 几何支持

基于 NTS 支持：

- Point / MultiPoint
- LineString / MultiLineString
- Polygon / MultiPolygon
- GeometryCollection

### 10.3 空间索引

- 大数据集查询优先使用空间索引
- 支持 STRtree 等索引结构
- 索引建立可由算子按需触发

## 11. 性能、容错与安全设计

### 11.1 性能设计

- 基于 `IAsyncEnumerable` 做流式处理
- 支持数据分块和属性下推
- 支持结果缓存和增量计算
- 支持数据分区并行和流水线并行

### 11.2 错误处理

错误分为配置错误、数据源错误、运行时错误、数据错误四类。  
框架使用统一错误码体系，便于日志检索、告警和自动化处理。

### 11.3 安全设计

- 敏感连接信息加密存储
- 数据源访问受权限控制
- API 层支持认证、限流和输入校验
- 执行记录纳入审计日志

## 12. 运维设计

### 12.1 部署形态

- 支持控制台、服务化和容器化部署
- 可接入 Kubernetes 做扩缩容与健康探针管理

### 12.2 监控与诊断

- 导出执行耗时、成功率、吞吐量、资源占用等指标
- 提供健康检查端点和诊断端点
- 支持接入 OpenTelemetry、Prometheus、Grafana

## 13. 术语表

| 术语 | 英文 | 说明 |
|------|------|------|
| 算子 | Operator | 可复用分析算法单元 |
| 分析项 | Analysis Item | 配置了输入输出的算子实例 |
| 分析方案 | Analysis Plan | 由分析项组成的执行单元 |
| DAG | Directed Acyclic Graph | 用于表达执行依赖关系 |
| 要素 | Feature | GIS 中的最小数据单元 |
| CRS | Coordinate Reference System | 坐标参考系统 |
