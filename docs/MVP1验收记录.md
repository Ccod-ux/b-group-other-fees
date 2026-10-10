# MVP1验收记录

本阶段完成费用维护与持久化，不代表 B 组完整通过。依据是 `其他费用录入、导入与导出-需求实现.docx` 的条款；原始材料、冲突及暂定边界见需求方案和 README。

## 当前阶段与题目对应关系

| 原题条款 | 当前状态 | 实现与证据 |
| --- | --- | --- |
| 2.1 / 4.2 项目与六个固定工项 | 完成基础建模及持久化 | Project、FixedWorkItem、DesignFee；schema/seed；Reset 数量与固定工项金额测试 |
| 2.2 其他费用字段与唯一性 | 完成 | OtherFee；同项目唯一约束；空值、重名、跨项目、Unicode 重名测试 |
| 2.3 金额、利润和利润率 | 费用层完成；汇总层待MVP3 | Money、OtherFee；原题521/520、亏损、空/零收入、舍入与范围测试 |
| 2.4 / 3.1 CRUD及明确错误 | 完成 | 四个费用接口；完整CRUD、400/404/409、未知字段与错误JSON测试 |
| 2.10 同一持久化业务模型 | CRUD完成；导入/导出待实现 | 单一OtherFee模型，查询读取SQLite；重启后金额及Id不变 |
| 3.4 本地WebAPI与Reset | 完成 | OWIN宿主；真实HTTP与停止/重启演示；Reset清空及失败回滚测试 |
| 4.1 Framework4.8 / NPOI | Framework4.8完成；NPOI待MVP2 | net48目标，Windows本机执行；当前没有Excel或COM代码 |
| 4.3 / 4.4 四层与仓储 | 本阶段完成 | 领域独立于SQL/HTTP；应用依赖仓储接口；参数化SQL在Infrastructure |
| 4.6 / 6.1 / 6.2 测试、说明、演示 | 本阶段完成；完整链路待实现 | NUnit、README、业务与代码讲解、真实HTTP脚本 |
| 2.5—2.7 / 3.2 导入 | 待MVP2 | 按表头/章节识别、各种单元格类型、同名覆盖、失败行报告均未实现 |
| 2.8—2.9 / 3.3 / 4.5 导出 | 待MVP3 | 汇总/税金、序号、文件流、样式、合并区域、打印设置均未实现 |
| 6.3 录入→导入→导出完整链路 | 未完成 | 后续完成导入和导出后再进行最终验收 |

## 可重复验证

在 `b-group` 目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\smoke.ps1 -SkipBuild
```

自动化测试每条使用独立临时库并初始化，结束后清理该测试目录。HTTP 演示使用独立库和进程，结果/日志保留在 artifacts，不会清空用户的 App_Data 演示库。

## 本次结果

2026-10-10，当前 Windows 环境：

- Release 构建成功，0警告、0错误；NuGet锁定还原成功。
- 49个自动化用例通过，0失败、0跳过。包含8个并发同名请求只保留1笔，及Reset失败回滚。
- 9项真实HTTP检查通过：Reset、新增、重名、停止并重启后的查询、修改、负成本、删除、再次删除、再次Reset。
- 原题521/520结果：利润1.00，ProfitRatePercent=0.19；修改为50/70后利润-20.00，ProfitRatePercent=-40.00，Id不变。

自动化结果：[mvp1.trx](../artifacts/test-results/mvp1.trx)。HTTP记录所在目录：[artifacts/smoke](../artifacts/smoke)。这些是运行产物，源码不依赖它们；新一轮执行会产生新的记录。

## 仍需注意的口径

空收入利润、金额舍入方法、百分数精度、名称规范化及保留名称，属于 README 中已明确标注的补充约定。测试证明当前代码符合这些约定，不代表原题作者已明确认可所有边界口径。后续导入导出要沿用或统一调整，不能分别使用不同公式。
