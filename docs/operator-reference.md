# 算子参考

## 空间运算

### buffer — 缓冲区分析

| 项目 | 内容 |
|------|------|
| **ID** | `buffer` |
| **分类** | 空间运算 |
| **描述** | 为每个输入要素生成指定距离的缓冲区多边形 |

**参数：**

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `distance` | double | ✅ | 缓冲距离（单位与数据坐标系一致） |

**输入：**

| 绑定名 | 说明 |
|--------|------|
| `source` | 源要素源 |

**输出：**

| 键名 | 说明 |
|------|------|
| `output` | 缓冲区多边形要素源 |

**示例：**

```json
{
  "id": "buffer-example",
  "operatorId": "buffer",
  "inputs": {
    "source": { "type": "external", "sourceId": "data/points.geojson" }
  },
  "parameters": { "distance": 0.5 },
  "output": { "adapterType": "geojson", "targetPath": "output/buffered.geojson" }
}
```

### clip — 裁剪分析

| 项目 | 内容 |
|------|------|
| **ID** | `clip` |
| **分类** | 空间运算 |

**参数：** 无

**输入：**

| 绑定名 | 说明 |
|--------|------|
| `source` | 被裁剪要素源 |
| `clip` | 裁剪面要素源 |

**输出：** `output` — 裁剪后的要素源

---

## 空间关系

### intersect_check — 相交检查

| 项目 | 内容 |
|------|------|
| **ID** | `intersect_check` |
| **分类** | 空间关系 |

**参数：**

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `use_second_input` | bool | true | true=两集合交叉检查，false=自相交检查 |

**输入：**

| 绑定名 | 说明 |
|--------|------|
| `source` | 主要素源 |
| `target` | 第二要素源（`use_second_input=true` 时必填） |

**输出：** `output` — 相交要素对（分析模式）或 `issues` — 问题记录（QC 模式）

### containment_check — 包含检查

| 项目 | 内容 |
|------|------|
| **ID** | `containment_check` |
| **分类** | 空间关系 |

**参数：**

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `relationship` | string | "contains" | 关系类型：`contains` / `within` |

**输入：**

| 绑定名 | 说明 |
|--------|------|
| `source` | 主要素源 |
| `target` | 对比要素源 |

**输出：** `output` — 满足关系的要素对

---

## 属性操作

### field_calculator — 字段计算器

| 项目 | 内容 |
|------|------|
| **ID** | `field_calculator` |
| **分类** | 属性操作 |

**参数：**

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `target_field` | string | ✅ | 新增字段名 |
| `expression` | string | ✅ | 计算表达式（见下方语法） |
| `field_type` | string | ✅ | 字段类型：`String`/`Integer`/`Double`/`Boolean`/`DateTime` |

**表达式语法：**
- 字符串字面量：`"Hello, World!"`
- 字段引用：`{field_name}` 替换为要素属性值
- 算术运算：`{a} + {b} * 2`（支持 + - * / 括号）
- 纯数字：`42.5`

**输入：**

| 绑定名 | 说明 |
|--------|------|
| `source` | 源要素源 |

### null_value_filler — 空值填充

| 项目 | 内容 |
|------|------|
| **ID** | `null_value_filler` |
| **分类** | 属性操作 |

**参数：**

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `target_field` | string | ✅ | 目标字段名 |
| `default_value` | string | ✅ | 默认值 |
| `field_type` | string | ✅ | 字段类型 |

### coordinate_transform — 坐标系转换

| 项目 | 内容 |
|------|------|
| **ID** | `coordinate_transform` |
| **分类** | 格式转换 |

**参数：**

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `source_epsg` | int | ✅ | 源 EPSG 代码 |
| `target_epsg` | int | ✅ | 目标 EPSG 代码 |

---

## 质检规则

### attribute_completeness_checker — 属性完整性检查

| 项目 | 内容 |
|------|------|
| **ID** | `attribute_completeness_checker` |
| **分类** | 质检规则 |

**参数：**

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `required_fields` | string | ✅ | 逗号分隔的必填字段列表（如 `"code,name,area"`） |

**QC 模式输出（`executionPolicy.qcMode = true`）：**
- `issues` — `IssueRecord` 列表
  - `ATTR_MISSING`（Error）：字段值为 null
  - `ATTR_EMPTY`（Warning）：字段值为空字符串

### geometry_validity_checker — 几何有效性检查

| 项目 | 内容 |
|------|------|
| **ID** | `geometry_validity_checker` |
| **分类** | 质检规则 |

**参数：** 无

**QC 模式输出：**
- `GEOM_EMPTY`（Error）：几何为空
- `GEOM_INVALID`（Error）：几何无效（自相交、环方向错误等）
- `GEOM_NOT_SIMPLE`（Warning）：线几何不简单（自交）
