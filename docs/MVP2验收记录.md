# MVP2验收记录

2026年10月10日，Excel导入阶段验收07至12通过。目标仍为.NET Framework 4.8、Windows x64、WebAPI2、SQLite、NPOI 2.7.6。导出、样式对照及完整录入→导入→导出链路留到MVP3和MVP4。

## 规则及实现

工作表必须为“项目汇总清单”，按完整表头识别五列明细或十列汇总；表头换列可读取，重复字段、缺工作表、缺关键列或文件损坏在写入前拒绝。十列模式依据原题2.6：仅处理“一 建设安装工程费”内数字序号的非固定工项明细。中文序号章节、六个固定工项、准确匹配的合计和说明行跳过。

损坏章节报告SECTION_INVALID并清除旧归属；随后无法确认归属的费用报告SECTION_UNCLEAR。完整有效章节可恢复解析；切换到设计费等正常章节后停止建设安装工程费明细识别。费用名称中仅含“税金”或“合计”字样不会误判为合计。

OtherFeeWorkbookReader负责NPOI解析；OtherFeeImportService复用OtherFee和Money规则；ImportUpsert以项目+规范名称匹配，逐行事务保存并保留原Id和rowid顺序。无效同名行不会覆盖此前成功数据。空收入保存NULL；成本原始负值在舍入前拒绝。

报告字段为SuccessCount、CreatedCount、UpdatedCount、FailureCount、Errors。成功按输入行计数，新增加更新等于成功数；失败按行计数，同一行的多个字段错误不会重复增加FailureCount。Errors含从1开始的RowNumber、Field、Code、Reason。

## 验收映射

| 编号 | 验收内容 | 证据 |
| --- | --- | --- |
| 07 | 未修改原五列附件导入两次 | 首次2成功/1新增/1更新/0失败，最终一条50/70；再次2成功/0新增/2更新/0失败。真实进程重启保留Id与值 |
| 08 | 十列含章节、固定工项、合计、说明 | 建设安装工程费中有效其他费用入库；六项及正常章节/合计跳过，切换设计费后停止识别 |
| 09 | 五列/十列换列、缺列、重复表头 | 换列金额准确；结构失败不写入，包括十列重复成本价列 |
| 10 | 数值、文本、公式、错误公式及类型 | 数值/金额文本/可计算公式通过；公式错误、日期、布尔值、非法文本逐行失败，后续有效行继续 |
| 11 | 空收入、空成本、最后同名行无效 | 空收入及公式空文本为NULL；空成本失败；成功值、Id和顺序保留；1.005先舍入为1.01 |
| 12 | 项目不匹配、可疑业务行、损坏章节 | 报告Excel行号、字段及原因；未知归属不保存；章节恢复后后续有效行可保存 |

补充覆盖：旧式.xls读取、损坏文件、缺工作表、无文件/多个文件、JSON上传及损坏multipart请求，均有明确响应，错误上传不写库。

## 可重复运行

在b-group目录执行以下命令，将源附件路径改成实际路径。每条测试创建独立临时库；运行演示创建独立进程和artifacts内的新库，不修改App_Data演示库。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test.ps1 -OutputPath artifacts/mvp2-validation-build -OriginalImportFile 'C:\路径\导入其他费用清单.xlsx'
powershell -ExecutionPolicy Bypass -File .\scripts\smoke.ps1 -SkipBuild -ApiDirectory artifacts/mvp2-validation-build/Release/net48
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-import.ps1 -SkipBuild -ApiDirectory artifacts/mvp2-validation-build/Release/net48 -File 'C:\路径\导入其他费用清单.xlsx'
```

本次锁定依赖还原及Release构建成功，0警告、0错误；68个自动化用例通过，0失败、0跳过（包含原附件测试）。CRUD真实HTTP回归9项、导入真实HTTP检查8项全部通过。导入演示另生成独立测试文件：2行成功、3行失败，错误行号2/3/5；原记录未被无效行覆盖，空收入NULL、后续有效记录及跨项目隔离正确。

原附件导入前后SHA256相同：F3BC9CEC88617A661F71B9A4847237FEC37494C3C4A52587AC74FE7FA3E1CA6F。源码无该附件副本，也不依赖附件中的费用名称写特例。

本次证据路径：artifacts/test-results/mvp2.trx；artifacts/import-smoke/a8158565daf14ec690c232e9fbc665d7/import-http-smoke.json；artifacts/smoke/36dcd52b2602408a83d0628a4613c5b1/http-smoke.json。产物不进入公开Git，执行脚本可重新生成。

## 适用边界

正常逐行业务错误继续处理；文件结构错误全文件拒绝。未预期的系统数据库故障会停止请求，先前已提交的行保留，可通过查询接口确认；当前没有将系统故障包装为部分成功报告。汇总计算、税金、导出格式和最终演示录屏尚待后续阶段。
