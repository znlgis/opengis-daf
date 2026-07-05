# 方案配置指南

## 概述

分析方案（AnalysisPlan）是一个 JSON 文档，描述了一次数据分析的完整流程：输入数据 → 算子处理 → 输出目标。方案通过 `daf run --plan <path>` 命令执行。

## 顶层结构

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `id` | string | ✅ | 方案唯一标识（如 `"land-use-analysis"`） |
| `name` | string | ✅ | 方案名称 |
| `version` | string | ✅ | 方案版本号（推荐语义化版本 `"1.0.0"`） |
| `group` | string | ❌ | 分组名（用于计划管理分类） |
| `items` | array | ✅ | 分析项列表（至少 1 个） |
| `subPlans` | array | ❌ | 子方案（P3 功能，当前忽略） |
| `executionPolicy` | object | ❌ | 执行策略配置 |

## 分析项（items）

每个 item 定义一次算子调用：

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `id` | string | ✅ | 项目内唯一 ID（如 `"step-buffer"`） |
| `operatorId` | string | ✅ | 算子 ID（如 `"buffer"`、`"clip"`） |
| `inputs` | dict | ✅ | 输入绑定 `{ 绑定名 → InputBinding }` |
| `parameters` | dict | ❌ | 算子参数 `{ 参数名 → 值 }` |
| `output` | object | ✅ | 输出绑定 |
| `executionPolicy` | object | ❌ | 算子级执行策略 |

### InputBinding

```json
{
  "type": "external",
  "sourceId": "data/roads.shp",
  "outputKey": null
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `type` | enum | `external`（文件路径）、`upstream`（上游 item 输出）、`subPlan`（P3，不支持） |
| `sourceId` | string | 数据源路径（external）/ 上游 itemId（upstream） |
| `outputKey` | string? | 上游 item 输出的键名（默认 `"output"`） |

### OutputBinding

```json
{
  "adapterType": "geojson",
  "targetPath": "output/result.geojson",
  "connectionConfig": null,
  "formatOptions": null,
  "isIntermediate": false
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `adapterType` | string | 输出适配器：`console`、`geojson`、`shapefile`、`postgis` |
| `targetPath` | string | 输出文件路径（console 忽略） |
| `connectionConfig` | object? | 数据库连接配置（仅 postgis） |
| `formatOptions` | object? | 格式化选项（编码、小数位数等） |
| `isIntermediate` | bool | 是否中间结果（可被缓存复用） |

### ItemExecutionPolicy

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `qcMode` | bool | false | 启用质检模式 |
| `maxRetries` | int | 0 | 失败重试次数 |
| `retryInterval` | string | "00:00:05" | 重试间隔 |
| `timeout` | string | "00:30:00" | 超时时间 |
| `exponentialBackoff` | bool | true | 是否指数退避 |

### PlanExecutionPolicy

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `failurePolicy` | enum | stopOnAny | 失败策略：`stopOnAny`（遇错即停）/ `continueIndependent`（继续独立项） |
| `maxParallelism` | int | 4 | 最大并行度（P2 启用） |

## 完整示例

### 单步缓冲区分析

```json
{
  "id": "buffer-demo",
  "name": "缓冲区分析示例",
  "version": "1.0.0",
  "items": [
    {
      "id": "step1",
      "operatorId": "buffer",
      "inputs": {
        "source": { "type": "external", "sourceId": "data/points.geojson" }
      },
      "parameters": { "distance": 0.5 },
      "output": {
        "adapterType": "geojson",
        "targetPath": "output/buffered.geojson"
      }
    }
  ]
}
```

### 两步串联分析

```json
{
  "id": "serial-demo",
  "name": "串联分析示例",
  "version": "1.0.0",
  "items": [
    {
      "id": "step-clip",
      "operatorId": "clip",
      "inputs": {
        "source": { "type": "external", "sourceId": "data/polygons.geojson" },
        "clip": { "type": "external", "sourceId": "data/boundary.geojson" }
      },
      "output": { "adapterType": "geojson", "targetPath": "output/clipped.geojson" }
    },
    {
      "id": "step-field",
      "operatorId": "field_calculator",
      "inputs": {
        "source": { "type": "upstream", "sourceId": "step-clip", "outputKey": "output" }
      },
      "parameters": {
        "target_field": "area_sqkm",
        "expression": "area * 0.000001",
        "field_type": "Double"
      },
      "output": { "adapterType": "geojson", "targetPath": "output/final.geojson" }
    }
  ]
}
```

### 质检方案

```json
{
  "id": "qc-demo",
  "name": "属性完整性检查",
  "version": "1.0.0",
  "items": [
    {
      "id": "qc-step",
      "operatorId": "attribute_completeness_checker",
      "inputs": {
        "source": { "type": "external", "sourceId": "data/landuse.geojson" }
      },
      "parameters": { "required_fields": "code,name,area" },
      "output": { "adapterType": "console" },
      "executionPolicy": { "qcMode": true }
    }
  ]
}
```
