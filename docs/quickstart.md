# 快速开始指南

## 环境要求

- **.NET 10 SDK** ([下载](https://dotnet.microsoft.com/download/dotnet/10.0))
- Windows x64 / Linux x64（推荐 .NET 官方支持的系统）

## 1. 构建项目

```bash
# Windows
.\build.ps1

# Linux/macOS
./build.sh
```

## 2. 验证安装

```bash
dotnet run --project src/OpenGisDAF.Cli -- help
```

应输出所有可用命令。

## 3. 创建第一个分析方案

创建文件 `my-first-plan.json`：

```json
{
  "id": "hello-daf",
  "name": "我的第一个分析",
  "version": "1.0.0",
  "items": [
    {
      "id": "step1",
      "operatorId": "field_calculator",
      "inputs": {
        "source": {
          "type": "external",
          "sourceId": "data/my-input.geojson"
        }
      },
      "parameters": {
        "target_field": "greeting",
        "expression": "\"Hello, {name}!\"",
        "field_type": "String"
      },
      "output": {
        "adapterType": "geojson",
        "targetPath": "output/result.geojson"
      }
    }
  ],
  "executionPolicy": {
    "failurePolicy": "stopOnAny"
  }
}
```

## 4. 运行分析

```bash
dotnet run --project src/OpenGisDAF.Cli -- run --plan my-first-plan.json
```

## 5. 查看结果

输出文件生成在 `output/result.geojson`，可用 QGIS 或其他 GIS 工具打开。

## 下一步

- [方案配置指南](plan-config-guide.md) — 了解完整的方案结构
- [算子参考](operator-reference.md) — 了解所有可用算子
- [CLI 命令参考](cli-reference.md) — 了解所有命令
