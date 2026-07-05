# CLI 命令参考

## 全局用法

```
daf <command> [options]
```

所有命令失败时返回非零退出码。

---

## run — 执行方案

```
daf run --plan <path>
```

加载方案文件 → 校验 → 执行 → 输出结果。支持 Ctrl+C 取消执行。

**选项：**

| 选项 | 说明 |
|------|------|
| `--plan <path>` | 方案 JSON 文件路径（必填） |

**示例：**

```bash
daf run --plan my-plan.json
```

**输出：**

```
开始执行方案: My Plan (ID: my-plan)
=== 执行完成 ===
总耗时: 1.23s
  buffer (step1): 成功 (0.89s)
```

如启用 QC 模式，会自动生成质检报告 `{plan}.qc-report.json`。

---

## validate — 校验方案

```
daf validate --plan <path>
```

仅校验方案结构和业务规则，不执行。

**选项：**

| 选项 | 说明 |
|------|------|
| `--plan <path>` | 方案 JSON 文件路径（必填） |

**示例：**

```bash
daf validate --plan my-plan.json
```

---

## operator — 算子管理

### operator list — 列出算子

```
daf operator list [--category <name>]
```

**选项：**

| 选项 | 说明 |
|------|------|
| `--category <name>` | 按分类筛选（可选） |

**示例：**

```bash
daf operator list
daf operator list --category 空间运算
```

### operator import — 导入算子插件

```
daf operator import --dll <path>
```

**选项：**

| 选项 | 说明 |
|------|------|
| `--dll <path>` | DLL 文件路径（必填） |

---

## plan — 方案管理

### plan list — 列出方案

```
daf plan list [--group <name>]
```

**示例：**

```bash
daf plan list
daf plan list --group land-use
```

### plan create — 创建方案

```
daf plan create --name <name> [--group <name>]
```

创建空方案模板。

### plan copy — 复制方案

```
daf plan copy --source <id> --target <id>
```

跨 group 复制。格式：`group/name`。

### plan export — 导出方案

```
daf plan export --plan <id> --output <path>
```

将方案导出为 JSON 文件。

---

## help — 帮助

```
daf help
daf --help
daf -h
```

---

## 退出码

| 码 | 含义 |
|----|------|
| 0 | 成功 |
| 1 | 错误（参数错误、方案校验失败、执行失败） |
