# 贡献指南

感谢你对 OpenGIS Data Analysis Framework 的关注！本文档将帮助你了解如何参与贡献。

## 贡献方式

### 报告问题

如果你发现了 bug 或有功能建议，请在 GitHub Issues 中提交：

1. 搜索已有 issue，确认没有重复
2. 使用清晰的标题描述问题
3. 提供复现步骤（如适用）：环境信息、输入数据描述、期望行为 vs 实际行为

### 贡献代码

1. Fork 本仓库
2. 创建功能分支（`feat/your-feature` 或 `fix/your-fix`）
3. 编写代码和单元测试
4. 确保所有测试通过
5. 提交 Pull Request

### 贡献算子

算子是最常见的贡献类型。开发算子请遵循以下规范：

1. **实现 `IOperator` 接口**（接口定义见设计文档 §6.1）
2. **标注元数据**：通过 `OperatorMetadata` 声明名称、分类、版本、参数定义
3. **算子保持无状态**：不在算子内部保存跨执行的状态
4. **正确使用 `CancellationToken`**：长时间运算需响应取消信号
5. **编写单元测试**：验证算子在正常和异常输入下的行为

算子分类：
- `spatial.relation` — 空间关系判断
- `spatial.computation` — 空间运算
- `spatial.join` — 空间连接
- `attribute` — 属性操作
- `statistics` — 统计分析
- `conversion` — 格式转换
- `qc` — 质检规则

## 代码规范

- 遵循 .NET 官方编码规范
- 使用 `record` 类型定义不可变数据模型
- 公共 API 需有 XML 文档注释
- 异步方法以 `Async` 结尾
- 接口名以 `I` 开头
- 使用 `CancellationToken` 作为最后一个参数（默认值 `default`）

## 提交规范

使用 [Conventional Commits](https://www.conventionalcommits.org/) 格式：

```
<type>(<scope>): <description>

feat(operator): add spatial buffer operator
fix(scheduler): resolve deadlock in parallel execution
docs(readme): update quick start guide
test(validation): add schema validation test cases
```

类型：`feat` / `fix` / `docs` / `test` / `refactor` / `chore` / `perf`

## 测试

- 单元测试：xUnit + Moq + FluentAssertions
- 测试覆盖率目标：核心模块 ≥ 80%
- 新增算子需包含：正常场景、边界条件、异常输入 三类测试用例

运行测试：

```bash
dotnet test
```

## 开发环境

- .NET 10 SDK
- 推荐的 IDE：Rider / Visual Studio 2022+ / VS Code + C# Dev Kit
- 依赖项：GDAL 3.x（用于集成测试）

## 行为准则

- 保持专业和尊重
- 建设性地参与讨论
- 接受建设性反馈
- 聚焦于对项目和社区最有利的方案

## 许可

贡献的代码默认以 MIT 许可证发布。提交 PR 即表示你同意此条款。
