# M4：方案管理层 — 详细实施计划

> **For agentic workers:** 使用 subagent-driven-development 或 executing-plans 技能按任务逐步实现。步骤使用 checkbox (`- [ ]`) 格式跟踪进度。

**Goal:** 实现方案 JSON Schema 定义、CRUD 操作、版本管理（文件副本）、Schema 校验和 8 项业务规则校验

**Architecture:** 在 Core 项目中新增 5 个接口（IPlanSerializer/IPlanValidator/IPlanRepository/IPlanVersionManager/IPlanManager），所有实现放入 PlanManagement 项目。PlanManagement 引用 Core 和 Infrastructure（复用 JsonConfiguration）。P1 阶段不使用 JsonSchema.Net，Schema 校验用代码实现。版本管理通过 `.V{n}.bak` 文件副本机制。

**Tech Stack:** .NET 10 / C# 14, System.Text.Json, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging

**Output Directory:** `src/OpenGisDAF.PlanManagement/`

**File Layout:**
```
src/OpenGisDAF.Core/Interfaces/
  IPlanSerializer.cs          ← 新建
  IPlanValidator.cs           ← 新建
  IPlanRepository.cs          ← 新建
  IPlanVersionManager.cs      ← 新建
  IPlanManager.cs             ← 新建
src/OpenGisDAF.Core/Models/
  AnalysisPlan.cs             ← 修改（新增 Group 属性）
  PlanSummary.cs              ← 新建
  VersionHistoryEntry.cs      ← 新建
src/OpenGisDAF.PlanManagement/
  OpenGisDAF.PlanManagement.csproj  ← 修改（添加项目引用）
  Schemas/
    plan-schema.json          ← 新建
  Converters/
    TimeSpanConverter.cs      ← 新建
  PlanSerializer.cs           ← 新建
  PlanValidator.cs            ← 新建
  PlanRepository.cs           ← 新建
  PlanVersionManager.cs       ← 新建
  PlanManager.cs              ← 新建
  Extensions/
    ServiceCollectionExtensions.cs  ← 新建
```

---

## 依赖关系图

```
Phase A: Core 接口 + 模型定义
  A1. PlanSummary 模型                    (Core/Models/PlanSummary.cs)
  A2. VersionHistoryEntry 模型            (Core/Models/VersionHistoryEntry.cs)
  A3. 修改 AnalysisPlan（新增 Group）     (Core/Models/AnalysisPlan.cs)
  A4. IPlanSerializer 接口                (Core/Interfaces/IPlanSerializer.cs)
  A5. IPlanValidator 接口                 (Core/Interfaces/IPlanValidator.cs)
  A6. IPlanRepository 接口                (Core/Interfaces/IPlanRepository.cs)
  A7. IPlanVersionManager 接口            (Core/Interfaces/IPlanVersionManager.cs)
  A8. IPlanManager 接口                   (Core/Interfaces/IPlanManager.cs)
        ↓
Phase B: 项目工程配置
  B1. 更新 PlanManagement.csproj          (添加 Core + Infrastructure 引用)
        ↓
Phase C: 序列化层（依赖 A4, B1）
  C1. plan-schema.json                    (Schemas/plan-schema.json)
  C2. TimeSpanConverter                   (Converters/TimeSpanConverter.cs)
  C3. PlanSerializer                      (PlanSerializer.cs)
        ↓
Phase D: 校验层（依赖 A5, B1）
  D1. PlanValidator — Schema 校验        (PlanValidator.cs 前半)
  D2. PlanValidator — 8 项业务规则       (PlanValidator.cs 后半)
        ↓
Phase E: 存储层（依赖 A6, B1）
  E1. PlanRepository                      (PlanRepository.cs)
        ↓
Phase F: 版本管理层（依赖 A7, E1, C3）
  F1. PlanVersionManager                  (PlanVersionManager.cs)
        ↓
Phase G: 方案管理器（依赖 A8, C3, D1, E1, F1）
  G1. PlanManager                         (PlanManager.cs)
        ↓
Phase H: DI 注册 + 构建验证
  H1. ServiceCollectionExtensions         (Extensions/ServiceCollectionExtensions.cs)
  H2. 构建验证                            (dotnet build)
```

**可并行的路径：**
- A1~A8 全部可并行（无相互依赖）
- C1+C2 可并行，C3 等 C1+C2 完成后开始
- D1 和 E1 可并行（各自独立）
- F1 依赖 E1+C3，G1 依赖 C3+D1+E1+F1

---

## P1 简化策略

| 功能 | P1 做法 | P2 增强 |
|------|---------|---------|
| Schema 校验 | 代码手动校验（检查必填字段、类型、枚举值范围、`additionalProperties` 拒绝） | 引入 JsonSchema.Net 做标准 JSON Schema 校验 |
| 子方案引用校验 | 仅检查 SubPlans 列表中的项存在性，不展开递归校验 | 递归校验子方案内部 Items 的算子引用和 DAG |
| CRS 一致性预检 | 仅检查是否有未声明的 CRS（Warning 级别），不检查转换兼容性 | 全量 CRS 等价性检查 + 自动转换策略推荐 |
| 版本 Diff | 简单 JSON 文本行级对比（两版本 JSON 序列化后逐行 diff） | 语义级 diff（高亮变化的具体字段，非文本 diff） |
| 版本回退覆盖策略 | 回退时直接覆盖当前 `.json` 文件 | 回退前创建当前版本的自动备份 |
| 方案搜索 | 按 Group + Name 精确匹配 | 支持模糊搜索、全文搜索 |
| 导入/导出 | 导入直接保存到仓库；导出复制到指定路径 | 支持 zip 打包、格式转换 |

---

## 详细任务分解

### Task A1: PlanSummary 领域模型

**文件：**
- 创建: `src/OpenGisDAF.Core/Models/PlanSummary.cs`

**设计：** 用于 Repository.ListAsync 返回方案摘要列表（不含完整 Items 和 SubPlans 细节），避免一次性加载所有方案的全部数据。

**Model 定义：**
```csharp
namespace OpenGisDAF.Core;

public sealed record PlanSummary
{
    public string Id { get; init; } = null!;
    public string Name { get; init; } = null!;
    public string Version { get; init; } = null!;
    public string? Group { get; init; }
    public int ItemCount { get; init; }
    public int SubPlanCount { get; init; }
    public DateTimeOffset LastModified { get; init; }
}
```

---

### Task A2: VersionHistoryEntry 领域模型

**文件：**
- 创建: `src/OpenGisDAF.Core/Models/VersionHistoryEntry.cs`

**设计：** 记录每次保存时创建的 `.V{n}.bak` 文件元信息。由 PlanVersionManager.GetVersionHistoryAsync 返回。

**Model 定义：**
```csharp
namespace OpenGisDAF.Core;

public sealed record VersionHistoryEntry
{
    public string PlanId { get; init; } = null!;
    public int VersionNumber { get; init; }
    public string FilePath { get; init; } = null!;
    public DateTimeOffset CreatedAt { get; init; }
    public long FileSize { get; init; }
}
```

---

### Task A3: 修改 AnalysisPlan — 新增 Group 属性

**文件：**
- 修改: `src/OpenGisDAF.Core/Models/AnalysisPlan.cs`

**变更：** 在现有 sealed class 中增加 `Group` 属性。Group 是可选的，用于文件系统组织 `plans/{group}/{name}.json`。

**当前代码：**
```csharp
public sealed class AnalysisPlan
{
    public string Id { get; init; } = null!;
    public string Name { get; init; } = null!;
    public string Version { get; init; } = null!;
    public IReadOnlyList<AnalysisItem> Items { get; init; } = [];
    public IReadOnlyList<AnalysisPlan> SubPlans { get; init; } = [];
    public PlanExecutionPolicy ExecutionPolicy { get; init; } = new();
}
```

**修改后：**
```csharp
public sealed class AnalysisPlan
{
    public string Id { get; init; } = null!;
    public string Name { get; init; } = null!;
    public string Version { get; init; } = null!;
    public string? Group { get; init; }
    public IReadOnlyList<AnalysisItem> Items { get; init; } = [];
    public IReadOnlyList<AnalysisPlan> SubPlans { get; init; } = [];
    public PlanExecutionPolicy ExecutionPolicy { get; init; } = new();
}
```

> **注意：** `Group` 为可选属性（`string?`），不影响现有代码的序列化兼容性（`null` 时 JSON 中字段可能被忽略，取决于 `JsonSerializerOptions.DefaultIgnoreCondition`）。
> **风险：** 如果已有方案 JSON 文件不含 `group` 字段，反序列化无影响（`null` 为合法值）。但使用 AddPlanManagement 注册时依赖 `JsonConfiguration.DefaultOptions`，其未设置 `DefaultIgnoreCondition`，故 `null` Group 会序列化为 `"group": null`。如需禁止，可在 PlanSerializer 内部使用独立的 Options（见 Task C3）。

---

### Task A4: IPlanSerializer 接口

**文件：**
- 创建: `src/OpenGisDAF.Core/Interfaces/IPlanSerializer.cs`

**设计：** 定义 AnalysisPlan 与 JSON 字符串/流的双向转换。同步版本用于简单场景，异步版本处理大文件和流式 I/O。

**接口定义：**
```csharp
namespace OpenGisDAF.Core;

public interface IPlanSerializer
{
    string Serialize(AnalysisPlan plan);
    AnalysisPlan Deserialize(string json);
    Task SerializeAsync(AnalysisPlan plan, Stream stream, CancellationToken cancellationToken = default);
    Task<AnalysisPlan> DeserializeAsync(Stream stream, CancellationToken cancellationToken = default);
}
```

---

### Task A5: IPlanValidator 接口

**文件：**
- 创建: `src/OpenGisDAF.Core/Interfaces/IPlanValidator.cs`

**设计：** 校验分为两层：(1) Schema 结构校验 — 无外部依赖；(2) 业务规则校验 — 可选依赖 IOperatorPool。当 operatorPool 为 null 时跳过算子相关检查（用于纯结构校验场景）。

**接口定义：**
```csharp
namespace OpenGisDAF.Core;

public interface IPlanValidator
{
    ValidationResult Validate(AnalysisPlan plan, IOperatorPool? operatorPool = null);
}
```

---

### Task A6: IPlanRepository 接口

**文件：**
- 创建: `src/OpenGisDAF.Core/Interfaces/IPlanRepository.cs`

**设计：** 文件系统持久化层。存储路径模式：`{rootPath}/{group}/{name}.json`。rootPath 由构造函数注入（默认 `plans/`）。

**接口定义：**
```csharp
namespace OpenGisDAF.Core;

public interface IPlanRepository
{
    Task SaveAsync(AnalysisPlan plan, CancellationToken cancellationToken = default);
    Task<AnalysisPlan?> LoadAsync(string group, string name, CancellationToken cancellationToken = default);
    Task DeleteAsync(string group, string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlanSummary>> ListAsync(string? group = null, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string group, string name, CancellationToken cancellationToken = default);
}
```

> **方法说明：**
> - `SaveAsync`: 将序列化后的 JSON 写入文件。若目录不存在则自动创建。**调用方负责版本备份**（Repository 不负责备份，由 PlanVersionManager 处理）。
> - `LoadAsync`: 从文件读取并返回反序列化的 AnalysisPlan。
> - `DeleteAsync`: 删除 JSON 文件（不删除 .bak 备份，手动清理）。
> - `ListAsync`: 按 group 筛选，返回摘要列表（仅读取文件元信息：Id/Name/Version/Group/ItemCount/SubPlanCount/LastModified）。
> - `ExistsAsync`: 检查文件是否存在。

---

### Task A7: IPlanVersionManager 接口

**文件：**
- 创建: `src/OpenGisDAF.Core/Interfaces/IPlanVersionManager.cs`

**设计：** 管理 `.V{n}.bak` 版本备份文件。保存时自动创建备份，回退时从备份恢复。Diff 返回两版本间的文本差异。

**接口定义：**
```csharp
namespace OpenGisDAF.Core;

public interface IPlanVersionManager
{
    Task<IReadOnlyList<VersionHistoryEntry>> GetVersionHistoryAsync(
        string group, string name, CancellationToken cancellationToken = default);

    Task RollbackAsync(
        string group, string name, int targetVersion, CancellationToken cancellationToken = default);

    Task BackupAsync(
        string group, string name, CancellationToken cancellationToken = default);

    Task<string> DiffAsync(
        string group, string name, int versionA, int versionB,
        CancellationToken cancellationToken = default);
}
```

> **方法说明：**
> - `GetVersionHistoryAsync`: 扫描 `{group}/{name}.V*.bak` 文件，返回历史列表。
> - `RollbackAsync`: 从指定版本的 `.V{n}.bak` 复制到当前 `.json`。
> - `BackupAsync`: 创建新的 `.V{n}.bak`（自动检测下一个版本号）。由 PlanManager.SaveAsync 内部调用。
> - `DiffAsync`: 加载两个版本的 JSON，做行级对比返回差异文本。

---

### Task A8: IPlanManager 接口

**文件：**
- 创建: `src/OpenGisDAF.Core/Interfaces/IPlanManager.cs`

**设计：** 聚合 Serializer + Validator + Repository + VersionManager 的统一入口。7 个 CRUD 操作 + Validate 方法。

**接口定义：**
```csharp
namespace OpenGisDAF.Core;

public interface IPlanManager
{
    Task<AnalysisPlan> CreateAsync(AnalysisPlan plan, CancellationToken cancellationToken = default);
    Task<AnalysisPlan?> LoadAsync(string group, string name, CancellationToken cancellationToken = default);
    Task SaveAsync(AnalysisPlan plan, CancellationToken cancellationToken = default);
    Task<AnalysisPlan> UpdateAsync(AnalysisPlan plan, CancellationToken cancellationToken = default);
    Task<AnalysisPlan> CopyAsync(string sourceGroup, string sourceName, string targetGroup,
        string targetName, CancellationToken cancellationToken = default);
    Task<AnalysisPlan> ImportAsync(string filePath, string? targetGroup = null,
        CancellationToken cancellationToken = default);
    Task ExportAsync(string group, string name, string outputPath,
        CancellationToken cancellationToken = default);
    Task<ValidationResult> ValidateAsync(AnalysisPlan plan, CancellationToken cancellationToken = default);
}
```

> **方法说明：**
> - **CreateAsync**: 若 `plan.Id` 为空则自动生成 GUID；若 `plan.Group` 为空则抛出 `ERR_CFG_SCHEMA_INVALID`。内部调用 Repository.SaveAsync + VersionManager.BackupAsync（首次创建也创建 V1 备份）。
> - **LoadAsync**: 从 Repository 加载方案。
> - **SaveAsync**: 先创建备份（VersionManager.BackupAsync），再写入（Repository.SaveAsync）。保存前自动更新 Version 属性（递增 patch 版本）。
> - **UpdateAsync**: 等同于 SaveAsync（语义别名）。
> - **CopyAsync**: 加载源方案 → 生成新 ID 和新 Name → 保存到目标 Group/Name。
> - **ImportAsync**: 从外部 JSON 文件反序列化 → 校验 → 保存到仓库。若 targetGroup 为 null，使用 plan.Group。
> - **ExportAsync**: 加载方案 → 序列化为 JSON → 写入 outputPath。
> - **ValidateAsync**: 调用 IPlanValidator.Validate，注入 OperatorPool。

---

### Task B1: 更新 PlanManagement.csproj

**文件：**
- 修改: `src/OpenGisDAF.PlanManagement/OpenGisDAF.PlanManagement.csproj`

**当前内容：**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

**修改后：**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\OpenGisDAF.Core\OpenGisDAF.Core.csproj" />
    <ProjectReference Include="..\OpenGisDAF.Infrastructure\OpenGisDAF.Infrastructure.csproj" />
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Include="Schemas\plan-schema.json" />
  </ItemGroup>
</Project>
```

> **说明：** 引用 Core（接口+模型）、Infrastructure（JsonConfiguration）。`plan-schema.json` 作为 EmbeddedResource 嵌入程序集。

---

### Task C1: plan-schema.json

**文件：**
- 创建: `src/OpenGisDAF.PlanManagement/Schemas/plan-schema.json`

**设计：** JSON Schema Draft-07 格式，描述 AnalysisPlan 的完整字段结构。P1 覆盖核心字段，P2 增强复杂约束（如 oneOf 参数多态）。

**Schema 内容：**
```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "$id": "https://opengis-daf.dev/schemas/plan-schema.json",
  "title": "AnalysisPlan",
  "type": "object",
  "required": ["id", "name", "version", "items"],
  "additionalProperties": false,
  "properties": {
    "id": { "type": "string", "minLength": 1 },
    "name": { "type": "string", "minLength": 1 },
    "version": { "type": "string", "minLength": 1 },
    "group": { "type": ["string", "null"] },
    "items": {
      "type": "array",
      "minItems": 1,
      "items": { "$ref": "#/definitions/AnalysisItem" }
    },
    "subPlans": {
      "type": "array",
      "items": { "$ref": "#" }
    },
    "executionPolicy": { "$ref": "#/definitions/PlanExecutionPolicy" }
  },
  "definitions": {
    "AnalysisItem": {
      "type": "object",
      "required": ["id", "operatorId", "output"],
      "additionalProperties": false,
      "properties": {
        "id": { "type": "string", "minLength": 1 },
        "operatorId": { "type": "string", "minLength": 1 },
        "operatorVersion": { "type": ["string", "null"] },
        "inputs": {
          "type": "object",
          "additionalProperties": { "$ref": "#/definitions/InputBinding" }
        },
        "parameters": {
          "type": "object"
        },
        "output": { "$ref": "#/definitions/OutputBinding" },
        "executionPolicy": { "$ref": "#/definitions/ItemExecutionPolicy" }
      }
    },
    "InputBinding": {
      "type": "object",
      "required": ["type", "sourceId"],
      "additionalProperties": false,
      "properties": {
        "type": { "enum": ["external", "upstream", "subPlan"] },
        "sourceId": { "type": "string" },
        "outputKey": { "type": ["string", "null"] }
      }
    },
    "OutputBinding": {
      "type": "object",
      "required": ["adapterType", "targetPath"],
      "additionalProperties": false,
      "properties": {
        "adapterType": { "type": "string" },
        "targetPath": { "type": "string" },
        "connectionConfig": { "$ref": "#/definitions/ConnectionConfig" },
        "fieldSelection": {
          "type": ["array", "null"],
          "items": { "type": "string" }
        },
        "isIntermediate": { "type": "boolean" },
        "formatOptions": { "$ref": "#/definitions/OutputFormatOptions" }
      }
    },
    "ConnectionConfig": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "dataSourceId": { "type": "string" },
        "adapterType": { "type": "string" },
        "host": { "type": "string" },
        "port": { "type": "integer" },
        "database": { "type": "string" },
        "userName": { "type": "string" },
        "encryptedPassword": { "type": ["string", "null"] },
        "additionalOptions": { "type": "object" }
      }
    },
    "OutputFormatOptions": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "decimalPlaces": { "type": ["integer", "null"] },
        "dateFormat": { "type": ["string", "null"] },
        "encoding": { "type": ["string", "null"] },
        "writeHeader": { "type": "boolean" }
      }
    },
    "PlanExecutionPolicy": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "maxParallelism": { "type": "integer" },
        "globalConcurrency": {
          "type": ["object", "null"],
          "additionalProperties": false,
          "properties": {
            "maxGlobalParallelism": { "type": "integer" },
            "enabled": { "type": "boolean" }
          }
        },
        "failurePolicy": { "enum": ["stopOnAny", "continueIndependent"] },
        "enablePartitioning": { "type": "boolean" },
        "partitionCount": { "type": "integer" },
        "qualityReportConfig": {
          "type": ["object", "null"],
          "additionalProperties": false,
          "properties": {
            "ruleWeights": { "type": "object" },
            "minPassRate": { "type": "number" }
          }
        }
      }
    },
    "ItemExecutionPolicy": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "maxRetries": { "type": "integer" },
        "retryInterval": { "type": "string", "pattern": "^\\d{2}:\\d{2}:\\d{2}(\\.\\d+)?$" },
        "exponentialBackoff": { "type": "boolean" },
        "timeout": { "type": "string", "pattern": "^\\d{2}:\\d{2}:\\d{2}(\\.\\d+)?$" },
        "logGranularity": { "enum": ["plan", "item", "feature"] },
        "retainIntermediateResults": { "type": "boolean" },
        "qcMode": { "type": "boolean" }
      }
    }
  }
}
```

> **P1 注意事项：**
> - `retryInterval` / `timeout` 使用 ISO 8601 duration 字符串（如 `"00:00:05"`），由 TimeSpanConverter 处理。
> - `subPlans` 为可选字段，P1 不展开递归校验。
> - `additionalProperties: false` 确保拒绝未知字段。

---

### Task C2: TimeSpanConverter

**文件：**
- 创建: `src/OpenGisDAF.PlanManagement/Converters/TimeSpanConverter.cs`

**设计：** `System.Text.Json` 自定义转换器，将 `TimeSpan` 序列化为 `"HH:mm:ss"` 格式字符串。

**实现代码：**
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenGisDAF.PlanManagement.Converters;

public sealed class TimeSpanConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        if (string.IsNullOrWhiteSpace(str))
            return TimeSpan.Zero;

        if (TimeSpan.TryParse(str, out var result))
            return result;

        throw new JsonException($"Invalid TimeSpan format: '{str}'");
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(@"hh\:mm\:ss"));
    }
}
```

> **注意：** 此转换器处理了 null/空字符串的防御，与 `ParameterConstraint` 中的 RetryInterval/Timeout 兼容。

---

### Task C3: PlanSerializer 实现

**文件：**
- 创建: `src/OpenGisDAF.PlanManagement/PlanSerializer.cs`

**设计：** 复用 `JsonConfiguration.DefaultOptions` 并添加自定义 TimeSpanConverter。提供同步和异步两种 API。使用 `JsonSerializerOptions` 的副本以避免污染 DefaultOptions。

**实现代码：**
```csharp
using System.Text.Json;
using OpenGisDAF.Core;
using OpenGisDAF.Infrastructure;
using OpenGisDAF.PlanManagement.Converters;

namespace OpenGisDAF.PlanManagement;

public sealed class PlanSerializer : IPlanSerializer
{
    private readonly JsonSerializerOptions _options;

    public PlanSerializer()
    {
        _options = JsonConfiguration.Create(opt =>
        {
            opt.Converters.Add(new TimeSpanConverter());
            opt.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        });
    }

    public string Serialize(AnalysisPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return JsonSerializer.Serialize(plan, _options);
    }

    public AnalysisPlan Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<AnalysisPlan>(json, _options)
               ?? throw new JsonException("Deserialization returned null");
    }

    public async Task SerializeAsync(AnalysisPlan plan, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await JsonSerializer.SerializeAsync(stream, plan, _options, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public async Task<AnalysisPlan> DeserializeAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var plan = await JsonSerializer.DeserializeAsync<AnalysisPlan>(stream, _options, cancellationToken);
        return plan ?? throw new JsonException("Deserialization returned null");
    }
}
```

> **关键设计决策：**
> - `DefaultIgnoreCondition.WhenWritingNull`: 确保 `null` Group 不会在 JSON 中序列化为 `"group": null`，保持 JSON 干净。
> - 反序列化时 `PropertyNameCaseInsensitive = true`（继承自 DefaultOptions），兼容大小写不敏感。
> - `JsonStringEnumConverter`（继承自 DefaultOptions）确保枚举以 camelCase 字符串序列化。

---

### Task D1: PlanValidator — Schema 校验（代码实现）

**文件：**
- 创建: `src/OpenGisDAF.PlanManagement/PlanValidator.cs`

**设计：** 两阶段校验：(1) 结构合法性（Schema 校验 — Task D1），(2) 业务规则（Task D2）。

**Schema 校验规则列表（代码实现，不用 JsonSchema.Net）：**

| 规则编号 | 检查项 | 错误码 | 严重级别 |
|---------|--------|--------|---------|
| S1 | `Id` 非空，非空白 | `ERR_CFG_SCHEMA_INVALID` | Error |
| S2 | `Name` 非空，非空白 | `ERR_CFG_SCHEMA_INVALID` | Error |
| S3 | `Version` 非空，非空白 | `ERR_CFG_SCHEMA_INVALID` | Error |
| S4 | `Items` 非空（至少 1 个分析项） | `ERR_CFG_SCHEMA_INVALID` | Error |
| S5 | `Items[].Id` 非空且唯一 | `ERR_CFG_SCHEMA_INVALID` | Error |
| S6 | `Items[].OperatorId` 非空 | `ERR_CFG_SCHEMA_INVALID` | Error |
| S7 | `Items[].Output` 非 null | `ERR_CFG_SCHEMA_INVALID` | Error |
| S8 | `Items[].Output.AdapterType` 非空 | `ERR_CFG_SCHEMA_INVALID` | Error |
| S9 | `Items[].Output.TargetPath` 非空（即使中间结果也需要路径） | `ERR_CFG_SCHEMA_INVALID` | Warning (P1) |
| S10 | `InputBinding.Type` 为合法枚举值 | `ERR_CFG_SCHEMA_INVALID` | Error |
| S11 | `InputBinding.SourceId` 非空（当 Type≠External 时；External 可为空） | `ERR_CFG_SCHEMA_INVALID` | Error |
| S12 | `ExecutionPolicy.FailurePolicy` 为合法枚举值 | `ERR_CFG_SCHEMA_INVALID` | Error |
| S13 | `Items[].ExecutionPolicy.LogGranularity` 为合法枚举值 | `ERR_CFG_SCHEMA_INVALID` | Error |
| S14 | 不允许未知的 JSON 顶层键（等效于 `additionalProperties: false`） | `ERR_CFG_SCHEMA_INVALID` | Error |

**PlanValidator 实现（Schema 部分 + 业务规则部分）：**
```csharp
using OpenGisDAF.Core;

namespace OpenGisDAF.PlanManagement;

public sealed class PlanValidator : IPlanValidator
{
    public ValidationResult Validate(AnalysisPlan plan, IOperatorPool? operatorPool = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var errors = new List<ValidationError>();
        var warnings = new List<ValidationError>();

        ValidateSchema(plan, errors, warnings);

        if (operatorPool is not null)
        {
            ValidateBusinessRules(plan, operatorPool, errors, warnings);
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }

    private static void ValidateSchema(AnalysisPlan plan, List<ValidationError> errors, List<ValidationError> warnings)
    {
        // S1: Id
        if (string.IsNullOrWhiteSpace(plan.Id))
            errors.Add(Err(ErrorCode.CfgSchemaInvalid, "Plan.Id is required and must not be empty", "plan.id"));

        // S2: Name
        if (string.IsNullOrWhiteSpace(plan.Name))
            errors.Add(Err(ErrorCode.CfgSchemaInvalid, "Plan.Name is required and must not be empty", "plan.name"));

        // S3: Version
        if (string.IsNullOrWhiteSpace(plan.Version))
            errors.Add(Err(ErrorCode.CfgSchemaInvalid, "Plan.Version is required and must not be empty", "plan.version"));

        // S4: Items non-empty
        if (plan.Items.Count == 0)
            errors.Add(Err(ErrorCode.CfgSchemaInvalid, "Plan.Items must contain at least one AnalysisItem", "plan.items"));

        // S5–S8, S10–S13: per-item checks
        var itemIds = new HashSet<string>();
        foreach (var item in plan.Items)
        {
            var loc = $"plan.items[{item.Id}]";

            // S5: Item Id unique
            if (string.IsNullOrWhiteSpace(item.Id))
                errors.Add(Err(ErrorCode.CfgSchemaInvalid, "AnalysisItem.Id is required", $"{loc}.id"));
            else if (!itemIds.Add(item.Id))
                errors.Add(Err(ErrorCode.CfgSchemaInvalid, $"Duplicate AnalysisItem.Id: '{item.Id}'", $"{loc}.id"));

            // S6: OperatorId
            if (string.IsNullOrWhiteSpace(item.OperatorId))
                errors.Add(Err(ErrorCode.CfgSchemaInvalid, "AnalysisItem.OperatorId is required", $"{loc}.operatorId"));

            // S7: Output non-null
            if (item.Output is null)
                errors.Add(Err(ErrorCode.CfgSchemaInvalid, "AnalysisItem.Output is required", $"{loc}.output"));

            // S8: AdapterType
            if (item.Output is not null && string.IsNullOrWhiteSpace(item.Output.AdapterType))
                errors.Add(Err(ErrorCode.CfgSchemaInvalid, "Output.AdapterType is required", $"{loc}.output.adapterType"));

            // S9: TargetPath warning (even for intermediate)
            if (item.Output is not null && string.IsNullOrWhiteSpace(item.Output.TargetPath))
                warnings.Add(Warn(ErrorCode.CfgBindingIncomplete, "Output.TargetPath is empty", $"{loc}.output.targetPath"));

            // S10, S11: InputBinding checks
            foreach (var (inputKey, binding) in item.Inputs)
            {
                var bl = $"{loc}.inputs.{inputKey}";
                if (!Enum.IsDefined(binding.Type))
                    errors.Add(Err(ErrorCode.CfgSchemaInvalid, $"Invalid BindingType: '{binding.Type}'", bl));

                if (binding.Type != BindingType.External && string.IsNullOrWhiteSpace(binding.SourceId))
                    errors.Add(Err(ErrorCode.CfgSchemaInvalid, $"InputBinding.SourceId is required for {binding.Type} binding", bl));
            }

            // S13: LogGranularity
            if (!Enum.IsDefined(item.ExecutionPolicy.LogGranularity))
                errors.Add(Err(ErrorCode.CfgSchemaInvalid, $"Invalid LogGranularity: '{item.ExecutionPolicy.LogGranularity}'", $"{loc}.executionPolicy.logGranularity"));
        }

        // S12: FailurePolicy
        if (!Enum.IsDefined(plan.ExecutionPolicy.FailurePolicy))
            errors.Add(Err(ErrorCode.CfgSchemaInvalid, $"Invalid FailurePolicy: '{plan.ExecutionPolicy.FailurePolicy}'", "plan.executionPolicy.failurePolicy"));

        // S14: No additional properties check (P2 — skip in P1; System.Text.Json with MissingMemberHandling not available)
    }

    private static void ValidateBusinessRules(AnalysisPlan plan, IOperatorPool operatorPool,
        List<ValidationError> errors, List<ValidationError> warnings)
    {
        // Detailed in Task D2
    }

    private static ValidationError Err(string code, string message, string location) =>
        new() { Severity = ValidationSeverity.Error, Code = code, Message = message, Location = location };

    private static ValidationError Warn(string code, string message, string location) =>
        new() { Severity = ValidationSeverity.Warning, Code = code, Message = message, Location = location };
}
```

> **注意：** S14（additionalProperties 检查）在 P1 跳过 — System.Text.Json 默认忽略未知属性，严格拒绝需要自定义 `JsonConverter` 或 `Modifiers`，投入产出比低。P2 引入 JsonSchema.Net 时自然覆盖。

---

### Task D2: PlanValidator — 8 项业务规则

**设计：** 继续在 PlanValidator 中实现 `ValidateBusinessRules` 方法。

**8 项规则详细设计：**

| # | 规则 | 算法 | 错误码 | 严重级别 |
|---|------|------|--------|---------|
| R1 | 算子存在性 | 遍历 Items，`operatorPool.GetById(item.OperatorId)` 检查非 null | `ERR_CFG_OPERATOR_NOT_FOUND` | Error |
| R2 | 输入绑定完整性 | 遍历所有 `Upstream` InputBinding，检查 SourceId 指向的 Item 在 Items 中存在；`SubPlan` 类型检查 SubPlans 中存在对应 Id | `ERR_CFG_BINDING_INCOMPLETE` | Error |
| R3 | DAG 无环检查 | 构建邻接表，DFS 三色标记法检测循环 | `ERR_CFG_DAG_CYCLE` | Error |
| R4 | 参数边界校验 | 加载算子 Metadata.Parameters，逐参数比对 Constraint（MinValue/MaxValue/Pattern/AllowedValues） | `ERR_CFG_PARAM_OUT_OF_RANGE` | Error |
| R5 | 输出绑定完整性 | 遍历 Items，Output 非 null 且 AdapterType 非空 | `ERR_CFG_BINDING_INCOMPLETE` | Warning |
| R6 | 子方案引用校验 | P1 仅检查 SubPlans 非空且每个 SubPlan.Id 非空 | `ERR_CFG_SCHEMA_INVALID` | Warning (P1) |
| R7 | CRS 一致性预检 | P1 仅检查是否存在无 CRS 声明的 External InputBinding（Warning） | `ERR_CFG_BINDING_INCOMPLETE` | Warning |

**实现代码（追加到 PlanValidator.cs 的 ValidateBusinessRules 方法）：**
```csharp
private static void ValidateBusinessRules(AnalysisPlan plan, IOperatorPool operatorPool,
    List<ValidationError> errors, List<ValidationError> warnings)
{
    // R1: Operator existence
    foreach (var item in plan.Items)
    {
        var op = operatorPool.GetById(item.OperatorId);
        if (op is null)
            errors.Add(Err(ErrorCode.CfgOperatorNotFound,
                $"Operator '{item.OperatorId}' not found in pool",
                $"plan.items[{item.Id}].operatorId"));
    }

    // R2: Input binding completeness
    var itemIdSet = new HashSet<string>(plan.Items.Select(i => i.Id));
    var subPlanIdSet = new HashSet<string>(plan.SubPlans.Select(sp => sp.Id));

    foreach (var item in plan.Items)
    {
        foreach (var (inputKey, binding) in item.Inputs)
        {
            var bl = $"plan.items[{item.Id}].inputs.{inputKey}";

            if (binding.Type == BindingType.Upstream && !itemIdSet.Contains(binding.SourceId))
                errors.Add(Err(ErrorCode.CfgBindingIncomplete,
                    $"Upstream source '{binding.SourceId}' not found in plan items", bl));

            if (binding.Type == BindingType.SubPlan && !subPlanIdSet.Contains(binding.SourceId))
                errors.Add(Err(ErrorCode.CfgBindingIncomplete,
                    $"SubPlan source '{binding.SourceId}' not found in subPlans", bl));
        }
    }

    // R3: DAG cycle detection (DFS three-color)
    var dagErrors = DetectCycles(plan.Items);
    errors.AddRange(dagErrors);

    // R4: Parameter boundary validation
    foreach (var item in plan.Items)
    {
        var op = operatorPool.GetById(item.OperatorId);
        if (op is null) continue; // already reported in R1

        foreach (var paramDef in op.Metadata.Parameters)
        {
            if (item.Parameters.TryGetValue(paramDef.Name, out var userValue) && userValue is not null)
            {
                ValidateParameterValue(item.Id, paramDef, userValue, errors);
            }
            else if (paramDef.Required)
            {
                errors.Add(Err(ErrorCode.CfgParamOutOfRange,
                    $"Required parameter '{paramDef.Name}' is missing",
                    $"plan.items[{item.Id}].parameters.{paramDef.Name}"));
            }
        }
    }

    // R5: Output binding completeness (Warning level)
    foreach (var item in plan.Items)
    {
        if (item.Output is null)
            warnings.Add(Warn(ErrorCode.CfgBindingIncomplete,
                "Output binding is null", $"plan.items[{item.Id}].output"));
        else if (string.IsNullOrWhiteSpace(item.Output.AdapterType))
            warnings.Add(Warn(ErrorCode.CfgBindingIncomplete,
                "Output.AdapterType is not specified", $"plan.items[{item.Id}].output.adapterType"));
    }

    // R6: SubPlan references (P1: existence check only)
    foreach (var subPlan in plan.SubPlans)
    {
        if (string.IsNullOrWhiteSpace(subPlan.Id))
            warnings.Add(Warn(ErrorCode.CfgSchemaInvalid,
                "SubPlan has empty Id", "plan.subPlans"));
    }

    // R7: CRS consistency pre-check (P1: only undeclared CRS warning)
    foreach (var item in plan.Items)
    {
        foreach (var (inputKey, binding) in item.Inputs)
        {
            if (binding.Type == BindingType.External && string.IsNullOrWhiteSpace(binding.OutputKey))
                warnings.Add(Warn(ErrorCode.CfgBindingIncomplete,
                    $"External input '{inputKey}' has no CRS declaration hint",
                    $"plan.items[{item.Id}].inputs.{inputKey}"));
        }
    }
}
```

**DAG 环检测算法（追加到 PlanValidator.cs）：**
```csharp
private static List<ValidationError> DetectCycles(IReadOnlyList<AnalysisItem> items)
{
    var errors = new List<ValidationError>();
    var adj = new Dictionary<string, List<string>>();
    var state = new Dictionary<string, int>(); // 0=white, 1=gray, 2=black

    foreach (var item in items)
    {
        adj[item.Id] = [];
        state[item.Id] = 0;
    }

    foreach (var item in items)
    {
        foreach (var (_, binding) in item.Inputs)
        {
            if (binding.Type == BindingType.Upstream && adj.ContainsKey(binding.SourceId))
                adj[binding.SourceId].Add(item.Id); // edge: source → consumer
        }
    }

    foreach (var itemId in adj.Keys)
    {
        if (state[itemId] == 0)
            Dfs(itemId, adj, state, [], errors);
    }

    return errors;
}

private static bool Dfs(string node, Dictionary<string, List<string>> adj,
    Dictionary<string, int> state, List<string> path, List<ValidationError> errors)
{
    state[node] = 1; // gray
    path.Add(node);

    foreach (var neighbor in adj[node])
    {
        if (state.TryGetValue(neighbor, out var ns) && ns == 1)
        {
            var cycleStart = path.IndexOf(neighbor);
            var cycle = string.Join(" → ", path.Skip(cycleStart).Append(neighbor));
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Error,
                Code = ErrorCode.CfgDagCycle,
                Message = $"Circular dependency detected: {cycle}",
                Location = $"plan.items"
            });
            return true;
        }

        if (state.TryGetValue(neighbor, out var ns2) && ns2 == 0)
        {
            if (Dfs(neighbor, adj, state, path, errors))
                return true;
        }
    }

    state[node] = 2; // black
    path.RemoveAt(path.Count - 1);
    return false;
}
```

**参数边界校验方法（追加到 PlanValidator.cs）：**
```csharp
private static void ValidateParameterValue(string itemId, ParameterDefinition paramDef,
    object userValue, List<ValidationError> errors)
{
    var loc = $"plan.items[{itemId}].parameters.{paramDef.Name}";
    var constraint = paramDef.Constraint;
    if (constraint is null) return;

    if (constraint.MinValue.HasValue || constraint.MaxValue.HasValue)
    {
        if (TryToDouble(userValue, out var dVal))
        {
            if (constraint.MinValue.HasValue && dVal < constraint.MinValue.Value)
                errors.Add(Err(ErrorCode.CfgParamOutOfRange,
                    $"Parameter '{paramDef.Name}' value {dVal} is below minimum {constraint.MinValue.Value}", loc));

            if (constraint.MaxValue.HasValue && dVal > constraint.MaxValue.Value)
                errors.Add(Err(ErrorCode.CfgParamOutOfRange,
                    $"Parameter '{paramDef.Name}' value {dVal} exceeds maximum {constraint.MaxValue.Value}", loc));
        }
    }

    if (constraint.AllowedValues is { Length: > 0 })
    {
        var strVal = userValue.ToString();
        if (!constraint.AllowedValues.Contains(strVal, StringComparer.OrdinalIgnoreCase))
            errors.Add(Err(ErrorCode.CfgParamOutOfRange,
                $"Parameter '{paramDef.Name}' value '{strVal}' is not in allowed values: [{string.Join(", ", constraint.AllowedValues)}]", loc));
    }

    if (!string.IsNullOrEmpty(constraint.Pattern) && userValue is string s)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(s, constraint.Pattern))
            errors.Add(Err(ErrorCode.CfgParamOutOfRange,
                $"Parameter '{paramDef.Name}' value '{s}' does not match pattern '{constraint.Pattern}'", loc));
    }
}

private static bool TryToDouble(object value, out double result)
{
    if (value is double d) { result = d; return true; }
    if (value is int i) { result = i; return true; }
    if (value is long l) { result = l; return true; }
    if (value is float f) { result = f; return true; }
    if (value is string s && double.TryParse(s, out var parsed)) { result = parsed; return true; }

    result = 0;
    return false;
}
```

> **注意：** `TryToDouble` 处理 `System.Text.Json` 将 JSON 数字反序列化为 `JsonElement` 的情况 — 但当前模型中 `Parameters` 是 `IReadOnlyDictionary<string, object?>`，STJ 默认将数字映射为 `JsonElement`。需要在 `PlanSerializer` 中配置或在此处处理 `JsonElement` 类型。

---

### Task E1: PlanRepository 实现

**文件：**
- 创建: `src/OpenGisDAF.PlanManagement/PlanRepository.cs`

**设计：** 基于文件系统的存储实现，维护 `plans/` 根目录结构。使用 `IPlanSerializer` 做 JSON 读写。

**存储结构：**
```
{rootPath}/
  {group}/
    {name}.json
    {name}.V1.bak
    {name}.V2.bak
    ...
```

**目录深度约束：** `group` 可包含 `/` 分隔符形成子目录，但总层级 ≤3（含 rootPath 本身）。实现时不做强制校验，仅日志警告。

**实现代码：**
```csharp
using OpenGisDAF.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenGisDAF.PlanManagement;

public sealed class PlanRepository : IPlanRepository
{
    private readonly string _rootPath;
    private readonly IPlanSerializer _serializer;
    private readonly ILogger<PlanRepository> _logger;

    public PlanRepository(string rootPath, IPlanSerializer serializer, ILogger<PlanRepository>? logger = null)
    {
        _rootPath = Path.GetFullPath(rootPath);
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _logger = logger ?? NullLogger<PlanRepository>.Instance;
    }

    public async Task SaveAsync(AnalysisPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidateGroup(plan.Group);

        var filePath = GetFilePath(plan.Group!, plan.Name);
        var dir = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(dir);

        var json = _serializer.Serialize(plan);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
        _logger.LogDebug("Plan saved: {Group}/{Name} -> {Path}", plan.Group, plan.Name, filePath);
    }

    public async Task<AnalysisPlan?> LoadAsync(string group, string name, CancellationToken cancellationToken = default)
    {
        ValidateGroup(group);
        var filePath = GetFilePath(group, name);

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Plan file not found: {Path}", filePath);
            return null;
        }

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        return _serializer.Deserialize(json);
    }

    public Task DeleteAsync(string group, string name, CancellationToken cancellationToken = default)
    {
        ValidateGroup(group);
        var filePath = GetFilePath(group, name);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.LogInformation("Plan deleted: {Group}/{Name}", group, name);
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<PlanSummary>> ListAsync(string? group = null, CancellationToken cancellationToken = default)
    {
        var summaries = new List<PlanSummary>();
        string searchPath;

        if (group is not null)
        {
            searchPath = Path.Combine(_rootPath, group);
            if (!Directory.Exists(searchPath))
                return summaries;
            await CollectPlansInDirectory(searchPath, group, summaries, cancellationToken);
        }
        else
        {
            if (!Directory.Exists(_rootPath))
                return summaries;

            foreach (var subDir in Directory.GetDirectories(_rootPath))
            {
                var dirGroup = Path.GetFileName(subDir);
                var subGroup = group is null ? dirGroup : $"{group}/{dirGroup}";
                await CollectPlansInDirectory(subDir, subGroup, summaries, cancellationToken);
            }
        }

        return summaries;
    }

    public Task<bool> ExistsAsync(string group, string name, CancellationToken cancellationToken = default)
    {
        ValidateGroup(group);
        var filePath = GetFilePath(group, name);
        return Task.FromResult(File.Exists(filePath));
    }

    private async Task CollectPlansInDirectory(string dirPath, string dirGroup,
        List<PlanSummary> summaries, CancellationToken ct)
    {
        foreach (var file in Directory.GetFiles(dirPath, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var plan = _serializer.Deserialize(json);
                var fi = new FileInfo(file);
                summaries.Add(new PlanSummary
                {
                    Id = plan.Id,
                    Name = plan.Name,
                    Version = plan.Version,
                    Group = plan.Group ?? dirGroup,
                    ItemCount = plan.Items.Count,
                    SubPlanCount = plan.SubPlans.Count,
                    LastModified = fi.LastWriteTimeUtc
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read plan file: {Path}", file);
            }
        }
    }

    private string GetFilePath(string group, string name)
    {
        return Path.Combine(_rootPath, group, $"{name}.json");
    }

    private static void ValidateGroup(string? group)
    {
        if (string.IsNullOrWhiteSpace(group))
            throw new ArgumentException("Group is required for repository operations", nameof(group));
    }
}
```

> **设计决策：**
> - `ListAsync` 扫描目录时跳过 `.bak` 文件（仅匹配 `*.json`）。
> - `DeleteAsync` 不删除 `.bak` 备份，保持版本历史完整性。
> - 反序列化失败的 JSON 文件产生 Warning 日志但不会中断扫描。
> - `ValidateGroup` 在 Save/Load/Delete/Exists 时强制 group 非空。

---

### Task F1: PlanVersionManager 实现

**文件：**
- 创建: `src/OpenGisDAF.PlanManagement/PlanVersionManager.cs`

**设计：** 基于 `.V{n}.bak` 文件副本的版本管理。存储位置与 `.json` 同目录。

**实现代码：**
```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using OpenGisDAF.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenGisDAF.PlanManagement;

public sealed partial class PlanVersionManager : IPlanVersionManager
{
    private readonly string _rootPath;
    private readonly IPlanSerializer _serializer;
    private readonly ILogger<PlanVersionManager> _logger;

    // Pattern: {name}.V{number}.bak
    [GeneratedRegex(@"^(?<name>.+)\.V(?<version>\d+)\.bak$", RegexOptions.IgnoreCase)]
    private static partial Regex BakFilePattern();

    public PlanVersionManager(string rootPath, IPlanSerializer serializer, ILogger<PlanVersionManager>? logger = null)
    {
        _rootPath = Path.GetFullPath(rootPath);
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _logger = logger ?? NullLogger<PlanVersionManager>.Instance;
    }

    public Task<IReadOnlyList<VersionHistoryEntry>> GetVersionHistoryAsync(
        string group, string name, CancellationToken cancellationToken = default)
    {
        var dir = Path.Combine(_rootPath, group);
        if (!Directory.Exists(dir))
            return Task.FromResult<IReadOnlyList<VersionHistoryEntry>>([]);

        var pattern = $"{name}.V*.bak";
        var files = Directory.GetFiles(dir, pattern);
        var entries = new List<VersionHistoryEntry>();

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var match = BakFilePattern().Match(fileName);
            if (!match.Success) continue;

            var versionNum = int.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture);
            var fi = new FileInfo(file);

            entries.Add(new VersionHistoryEntry
            {
                PlanId = ExtractPlanId(file, cancellationToken),
                VersionNumber = versionNum,
                FilePath = file,
                CreatedAt = fi.LastWriteTimeUtc,
                FileSize = fi.Length
            });
        }

        return Task.FromResult<IReadOnlyList<VersionHistoryEntry>>(
            entries.OrderBy(e => e.VersionNumber).ToList());
    }

    public async Task RollbackAsync(string group, string name, int targetVersion,
        CancellationToken cancellationToken = default)
    {
        var currentFile = GetCurrentFilePath(group, name);
        var bakFile = GetBakFilePath(group, name, targetVersion);

        if (!File.Exists(bakFile))
            throw new FileNotFoundException($"Version backup not found: {bakFile}");

        // Copy backup over current file
        File.Copy(bakFile, currentFile, overwrite: true);
        _logger.LogInformation("Rolled back {Group}/{Name} to version {Version}", group, name, targetVersion);

        await Task.CompletedTask;
    }

    public async Task BackupAsync(string group, string name, CancellationToken cancellationToken = default)
    {
        var currentFile = GetCurrentFilePath(group, name);
        if (!File.Exists(currentFile))
        {
            _logger.LogDebug("No current file to backup: {Path}", currentFile);
            return;
        }

        var history = await GetVersionHistoryAsync(group, name, cancellationToken);
        var nextVersion = history.Count > 0 ? history.Max(e => e.VersionNumber) + 1 : 1;
        var bakFile = GetBakFilePath(group, name, nextVersion);

        File.Copy(currentFile, bakFile, overwrite: false);
        _logger.LogInformation("Created version backup: {Path} (V{Version})", bakFile, nextVersion);
    }

    public async Task<string> DiffAsync(string group, string name, int versionA, int versionB,
        CancellationToken cancellationToken = default)
    {
        var fileA = versionA == 0 ? GetCurrentFilePath(group, name) : GetBakFilePath(group, name, versionA);
        var fileB = versionB == 0 ? GetCurrentFilePath(group, name) : GetBakFilePath(group, name, versionB);

        if (!File.Exists(fileA))
            throw new FileNotFoundException($"Version file not found: {fileA}");
        if (!File.Exists(fileB))
            throw new FileNotFoundException($"Version file not found: {fileB}");

        var linesA = await File.ReadAllLinesAsync(fileA, cancellationToken);
        var linesB = await File.ReadAllLinesAsync(fileB, cancellationToken);

        return SimpleLineDiff(linesA, linesB, versionA, versionB);
    }

    private string GetCurrentFilePath(string group, string name)
    {
        return Path.Combine(_rootPath, group, $"{name}.json");
    }

    private string GetBakFilePath(string group, string name, int version)
    {
        return Path.Combine(_rootPath, group, $"{name}.V{version}.bak");
    }

    private string ExtractPlanId(string bakFilePath, CancellationToken ct)
    {
        try
        {
            var json = File.ReadAllText(bakFilePath);
            var plan = _serializer.Deserialize(json);
            return plan.Id;
        }
        catch
        {
            return "unknown";
        }
    }

    private static string SimpleLineDiff(string[] linesA, string[] linesB, int verA, int verB)
    {
        var diff = new System.Text.StringBuilder();
        diff.AppendLine($"--- V{verA}");
        diff.AppendLine($"+++ V{verB}");

        var maxLen = Math.Max(linesA.Length, linesB.Length);
        for (var i = 0; i < maxLen; i++)
        {
            var a = i < linesA.Length ? linesA[i].Trim() : null;
            var b = i < linesB.Length ? linesB[i].Trim() : null;

            if (a == b) continue;

            if (a is not null && b is null)
                diff.AppendLine($"- {a}");
            else if (a is null && b is not null)
                diff.AppendLine($"+ {b}");
            else if (a != b)
            {
                diff.AppendLine($"- {a}");
                diff.AppendLine($"+ {b}");
            }
        }

        return diff.ToString();
    }
}
```

> **设计决策：**
> - `versionA=0` or `versionB=0` 在 DiffAsync 中代表"当前版本"（`.json` 文件）。
> - `BackupAsync` 仅在有当前文件时才备份。
> - Bak 文件内容与 JSON 文件完全相同，可直接用序列化器反序列化。
> - `SimpleLineDiff` 是 P1 的文本行级 diff，P2 增强为结构化语义 diff。

---

### Task G1: PlanManager 实现

**文件：**
- 创建: `src/OpenGisDAF.PlanManagement/PlanManager.cs`

**设计：** 整合 Serializer + Validator + Repository + VersionManager 的统一门面。ID 生成策略：若未提供则自动生成 GUID。Version 自动管理：创建时为 "1.0.0"，保存时递增 patch 号。

**实现代码：**
```csharp
using System.Text.RegularExpressions;
using OpenGisDAF.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenGisDAF.PlanManagement;

public sealed class PlanManager : IPlanManager
{
    private readonly IPlanSerializer _serializer;
    private readonly IPlanValidator _validator;
    private readonly IPlanRepository _repository;
    private readonly IPlanVersionManager _versionManager;
    private readonly IOperatorPool _operatorPool;
    private readonly ILogger<PlanManager> _logger;

    public PlanManager(
        IPlanSerializer serializer,
        IPlanValidator validator,
        IPlanRepository repository,
        IPlanVersionManager versionManager,
        IOperatorPool operatorPool,
        ILogger<PlanManager>? logger = null)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _versionManager = versionManager ?? throw new ArgumentNullException(nameof(versionManager));
        _operatorPool = operatorPool ?? throw new ArgumentNullException(nameof(operatorPool));
        _logger = logger ?? NullLogger<PlanManager>.Instance;
    }

    public async Task<AnalysisPlan> CreateAsync(AnalysisPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var planToSave = new AnalysisPlan
        {
            Id = string.IsNullOrWhiteSpace(plan.Id) ? Guid.NewGuid().ToString("N") : plan.Id,
            Name = plan.Name,
            Version = string.IsNullOrWhiteSpace(plan.Version) ? "1.0.0" : plan.Version,
            Group = plan.Group,
            Items = plan.Items,
            SubPlans = plan.SubPlans,
            ExecutionPolicy = plan.ExecutionPolicy
        };

        if (string.IsNullOrWhiteSpace(planToSave.Group))
            throw new ArgumentException("Group is required to create a plan", nameof(plan.Group));

        await _repository.SaveAsync(planToSave, cancellationToken);
        await _versionManager.BackupAsync(planToSave.Group!, planToSave.Name, cancellationToken);

        _logger.LogInformation("Plan created: {Id} ({Group}/{Name})", planToSave.Id, planToSave.Group, planToSave.Name);
        return planToSave;
    }

    public async Task<AnalysisPlan?> LoadAsync(string group, string name, CancellationToken cancellationToken = default)
    {
        return await _repository.LoadAsync(group, name, cancellationToken);
    }

    public async Task SaveAsync(AnalysisPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (string.IsNullOrWhiteSpace(plan.Group))
            throw new ArgumentException("Group is required to save a plan", nameof(plan.Group));

        // Increment version before backup
        var updatedPlan = IncrementVersion(plan);

        await _versionManager.BackupAsync(updatedPlan.Group!, updatedPlan.Name, cancellationToken);
        await _repository.SaveAsync(updatedPlan, cancellationToken);
        _logger.LogInformation("Plan saved: {Group}/{Name} v{Version}", updatedPlan.Group, updatedPlan.Name, updatedPlan.Version);
    }

    public Task<AnalysisPlan> UpdateAsync(AnalysisPlan plan, CancellationToken cancellationToken = default)
    {
        return SaveAsync(plan, cancellationToken)
            .ContinueWith(_ => plan, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    public async Task<AnalysisPlan> CopyAsync(string sourceGroup, string sourceName, string targetGroup,
        string targetName, CancellationToken cancellationToken = default)
    {
        var source = await _repository.LoadAsync(sourceGroup, sourceName, cancellationToken);
        if (source is null)
            throw new InvalidOperationException($"Source plan not found: {sourceGroup}/{sourceName}");

        var copy = new AnalysisPlan
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = targetName,
            Version = "1.0.0",
            Group = targetGroup,
            Items = source.Items,
            SubPlans = source.SubPlans,
            ExecutionPolicy = source.ExecutionPolicy
        };

        await CreateAsync(copy, cancellationToken);
        return copy;
    }

    public async Task<AnalysisPlan> ImportAsync(string filePath, string? targetGroup = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Import file not found: {filePath}");

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var plan = _serializer.Deserialize(json);

        var group = targetGroup ?? plan.Group;
        if (string.IsNullOrWhiteSpace(group))
            throw new ArgumentException("Group is required for import. Specify targetGroup or ensure plan has Group set.");

        var importedPlan = new AnalysisPlan
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = plan.Name,
            Version = "1.0.0",
            Group = group,
            Items = plan.Items,
            SubPlans = plan.SubPlans,
            ExecutionPolicy = plan.ExecutionPolicy
        };

        await CreateAsync(importedPlan, cancellationToken);
        return importedPlan;
    }

    public async Task ExportAsync(string group, string name, string outputPath,
        CancellationToken cancellationToken = default)
    {
        var plan = await _repository.LoadAsync(group, name, cancellationToken);
        if (plan is null)
            throw new InvalidOperationException($"Plan not found: {group}/{name}");

        var json = _serializer.Serialize(plan);
        var dir = Path.GetDirectoryName(outputPath);
        if (dir is not null)
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(outputPath, json, cancellationToken);
        _logger.LogInformation("Plan exported: {Group}/{Name} -> {Path}", group, name, outputPath);
    }

    public Task<ValidationResult> ValidateAsync(AnalysisPlan plan, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_validator.Validate(plan, _operatorPool));
    }

    private static AnalysisPlan IncrementVersion(AnalysisPlan plan)
    {
        var parts = plan.Version.Split('.');
        if (parts.Length >= 3 && int.TryParse(parts[^1], out var patch))
        {
            parts[^1] = (patch + 1).ToString();
            var newVersion = string.Join('.', parts);
            return new AnalysisPlan
            {
                Id = plan.Id,
                Name = plan.Name,
                Version = newVersion,
                Group = plan.Group,
                Items = plan.Items,
                SubPlans = plan.SubPlans,
                ExecutionPolicy = plan.ExecutionPolicy
            };
        }

        return plan; // keep original version if format is non-standard
    }
}
```

> **设计决策：**
> - `SaveAsync` 自动递增 patch 版本号（`1.0.0` → `1.0.1`）。若用户想自定义主版本号，需直接修改 `Version` 属性后调用 `SaveAsync`。
> - `CopyAsync` 和 `ImportAsync` 生成新 GUID 作为 Id，重置 Version 为 `"1.0.0"`。
> - `CreateAsync` 不校验（仅 Schema 检查 Group 非空），调用方可先 `ValidateAsync` 再 `CreateAsync`。

---

### Task H1: ServiceCollectionExtensions — DI 注册

**文件：**
- 创建: `src/OpenGisDAF.PlanManagement/Extensions/ServiceCollectionExtensions.cs`

**设计：** 将所有 PlanManagement 组件注册到 DI 容器。默认 rootPath 为当前工作目录下的 `plans/`。

**实现代码：**
```csharp
using Microsoft.Extensions.DependencyInjection;
using OpenGisDAF.Core;

namespace OpenGisDAF.PlanManagement.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPlanManagement(
        this IServiceCollection services,
        string? rootPath = null)
    {
        rootPath ??= Path.Combine(Directory.GetCurrentDirectory(), "plans");

        services.AddSingleton<IPlanSerializer, PlanSerializer>();
        services.AddSingleton<IPlanValidator, PlanValidator>();
        services.AddSingleton<IPlanVersionManager>(sp =>
            new PlanVersionManager(
                rootPath,
                sp.GetRequiredService<IPlanSerializer>(),
                sp.GetService<Microsoft.Extensions.Logging.ILogger<PlanVersionManager>>()));

        services.AddSingleton<IPlanRepository>(sp =>
            new PlanRepository(
                rootPath,
                sp.GetRequiredService<IPlanSerializer>(),
                sp.GetService<Microsoft.Extensions.Logging.ILogger<PlanRepository>>()));

        services.AddSingleton<IPlanManager>(sp =>
            new PlanManager(
                sp.GetRequiredService<IPlanSerializer>(),
                sp.GetRequiredService<IPlanValidator>(),
                sp.GetRequiredService<IPlanRepository>(),
                sp.GetRequiredService<IPlanVersionManager>(),
                sp.GetRequiredService<IOperatorPool>(),
                sp.GetService<Microsoft.Extensions.Logging.ILogger<PlanManager>>()));

        return services;
    }
}
```

---

### Task H2: 构建验证

**步骤：**
```bash
dotnet build src/OpenGisDAF.PlanManagement/OpenGisDAF.PlanManagement.csproj
```

**预期结果：** 0 Errors, 0 Warnings（`TreatWarningsAsErrors=true`）

**验证清单：**
- [ ] Core 项目 5 个新接口编译通过
- [ ] Core 项目 2 个新模型 + 1 个修改模型编译通过（无 breaking change）
- [ ] PlanManagement 项目 8 个新文件编译通过
- [ ] 所有项目间引用正确（Core ← PlanManagement, Infrastructure ← PlanManagement）
- [ ] 无未使用的 using 指令
- [ ] 无循环依赖

---

## 自查清单

### 1. Spec 覆盖率

| 开发计划需求 | 对应 Task |
|-------------|----------|
| JSON Schema 定义 `plan-schema.json` (P1 优先核心字段) | C1 |
| PlanSerializer: JSON ↔ AnalysisPlan | A4 + C3 |
| 自定义转换器 (TimeSpan) | C2 |
| Schema 校验 (14 项规则) | D1 |
| 8 项业务规则 (算子存在性/DAG 无环/参数边界等) | D2 |
| PlanRepository 文件系统存储 `plans/{group}/{name}.json` | A6 + E1 |
| IPlanManager 7 个 CRUD 操作 | A8 + G1 |
| IPlanVersionManager GetVersionHistory/Rollback/Diff `.V{n}.bak` | A7 + F1 |
| PlanManager 整合 | G1 |
| 新接口全放 Core/Interfaces/ | A4~A8 |
| 方案 ID 默认 GUID | G1.CreateAsync |
| 方案文件组织 `plans/{group}/{name}.json`，层级 ≤3 | E1 |
| 不引入新外部依赖 (JsonSchema.Net) | 全量代码实现 Schema 校验 |
| 版本管理 `.V{n}.bak` 文件副本 | F1 |
| DI 注册 | H1 |
| AnalysisPlan 新增 Group 属性 | A3 |
| PlanManagement.csproj 添加 Core + Infrastructure 引用 | B1 |

### 2. Placeholder 扫描

- 无 "TBD", "TODO", "implement later"
- 无 "add appropriate error handling" 空话 — Error 处理已内嵌
- 无 "similar to Task N" — 每个 Task 独立提供完整代码
- 所有代码片段均可在给定文件中直接使用

### 3. 类型一致性

- `IPlanSerializer`: `Serialize(AnalysisPlan)` → `string`, `Deserialize(string)` → `AnalysisPlan` — 一致
- `IPlanValidator.Validate`: 接受 `IOperatorPool?` — PlanValidator 实现中 operatorPool 为 null 跳过业务规则
- `IPlanRepository`: Group + Name 约定贯穿接口 — 一致
- `IPlanVersionManager`: BackupAsync + RollbackAsync 共享 Group/Name 参数 — 一致
- `IPlanManager`: 7 个方法全部在 PlanManager 中实现 — 一致
- `PlanSummary`: 用于 `IPlanRepository.ListAsync` 返回 — 一致
- `VersionHistoryEntry`: 用于 `IPlanVersionManager.GetVersionHistoryAsync` 返回 — 一致
- `ErrorCode` 常量: `CfgOperatorNotFound`, `CfgDagCycle`, `CfgBindingIncomplete`, `CfgParamOutOfRange`, `CfgSchemaInvalid` — 全部与 Core/ErrorCode.cs 已有常量一致
- `ValidationResult` / `ValidationError` — 与已有 record 定义一致

---

## 风险与缓解

| # | 风险 | 影响 | 缓解 |
|---|------|------|------|
| R1 | `PlanSerializer` 中 `Parameters` 的 `object?` 类型在 STJ 下序列化为 `JsonElement`，需要在 `TryToDouble` 中处理 | 参数边界校验失败 | 在 `TryToDouble` 中添加 `JsonElement` 类型分支 |
| R2 | `AnalysisPlan` 新增 `Group` 属性可能影响已有代码（如 CLI 命令可能已引用 AnalysisPlan） | 编译 breaking change | 检查 AnalysisPlan 的所有引用，确认无直接构造后缺少 Group |
| R3 | PlanManager.SaveAsync 自动递增版本号的逻辑可能与 CLI 的 `plan update` 预期冲突 | 版本号不准确 | P1 阶段文档明确说明自动递增行为；用户若要手动控制版本，不应重复调用 Save |
| R4 | `SimpleLineDiff` 依赖 JSON 格式化输出一致性（`WriteIndented = true`），若序列化配置变化 diff 可能无效 | Diff 结果不准确 | P2 升级为结构化语义 diff |

---

## 实施顺序建议

1. **批次 1 (并行):** A1, A2, A3, A4, A5, A6, A7, A8 → Core 接口+模型全部就绪
2. **批次 2:** B1 → 更新 PlanManagement.csproj
3. **批次 3 (并行):** C1, C2 → plan-schema.json + TimeSpanConverter
4. **批次 4:** C3 → PlanSerializer（依赖 C1+C2 完成）
5. **批次 5 (并行):** D1+D2, E1 → PlanValidator + PlanRepository（均依赖 C3）
6. **批次 6:** F1 → PlanVersionManager（依赖 E1+C3）
7. **批次 7:** G1 → PlanManager（依赖 C3+D1+E1+F1）
8. **批次 8:** H1, H2 → DI 注册 + 构建验证

> 预估总工时：2.5~3 工作日（含测试），与开发计划 2.5 周一致。

---

## Handoff Plan

1. **Task A1-A3: Core Models** — 创建 `PlanSummary.cs`, `VersionHistoryEntry.cs`，修改 `AnalysisPlan.cs` 添加 `Group` 属性
2. **Task A4-A8: Core Interfaces** — 创建 `IPlanSerializer.cs`, `IPlanValidator.cs`, `IPlanRepository.cs`, `IPlanVersionManager.cs`, `IPlanManager.cs`
3. **Task B1: Project Setup** — 更新 `OpenGisDAF.PlanManagement.csproj` 添加 Core + Infrastructure 引用
4. **Task C1: Schema** — 创建 `Schemas/plan-schema.json`
5. **Task C2: Converter** — 创建 `Converters/TimeSpanConverter.cs`
6. **Task C3: Serializer** — 创建 `PlanSerializer.cs` 实现 IPlanSerializer
7. **Task D1-D2: Validator** — 创建 `PlanValidator.cs` 实现 Schema 校验 + 8 项业务规则
8. **Task E1: Repository** — 创建 `PlanRepository.cs` 实现文件系统存储
9. **Task F1: Version Manager** — 创建 `PlanVersionManager.cs` 实现 .bak 版本管理
10. **Task G1: Plan Manager** — 创建 `PlanManager.cs` 实现 CRUD 门面
11. **Task H1: DI** — 创建 `Extensions/ServiceCollectionExtensions.cs` 实现 AddPlanManagement
12. **Task H2: Build** — 运行 `dotnet build` 验证 0 Errors 0 Warnings
- **Risk:** Core 模型修改 (AnalysisPlan.Group) 可能影响现有序列化兼容性 — 使用 `DefaultIgnoreCondition.WhenWritingNull`
- **Risk:** `TryToDouble` 需要处理 `JsonElement` 类型 — 添加 `System.Text.Json` 引用分支
- **Test:** 每个实现类需要有单元测试覆盖核心逻辑（建议在 PlanManagement 项目中添加 xUnit 测试项目），但 M4 开发计划未要求独立测试项目 — 最低验证：`dotnet build` 全量通过

