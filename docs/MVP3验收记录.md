# MVP3验收记录

2026年10月10日，标准Excel导出阶段验收13至17通过。目标仍为.NET Framework 4.8、Windows x64、WebAPI2、SQLite、NPOI 2.7.6。最终交付状态后续更新于MVP4交付记录，运行演示采用截图和Excel对照。

## 实现

`GET /api/projects/{projectId}/summary/export`返回xlsx下载，含Excel MIME类型、attachment下载头及中文文件名。未知项目404；缺固定工项或设计费400 `SUMMARY_DATA_INCOMPLETE`。错误仍返回统一JSON，不生成空的成功文件。

仓储在同一个事务中读取Projects、FixedWorkItems、DesignFees、OtherFees，随后释放数据库连接。应用服务将领域层ProjectSummary的计算结果交给NPOI生成器；不直接从请求JSON或输入Excel导出，不读取原始附件模板，也不按费用名称写特例。

六个固定工项后，其他费用按rowid创建顺序从7续排。每条领域汇总明细保留SourceId；Id可追溯至数据库记录。建设安装工程费汇总全部明细，设计费参与不含税合计；税金收入、成本分别舍入至分，税金利润为两者之差。含税合计为不含税加税金，汇总利润率重新计算。

金额以decimal计算，Excel存数值、金额格式0.00，利润率存比例、格式0.00%。空收入C列为空，计算时按0；空/零面积F/G/H为数值0；已有正面积则计算单方金额。税金行F至I为空。种子税率为9%，仓储读取项目已保存的税率，不针对项目Id写固定值。

## 验收映射

| 编号 | 验收内容 | 证据 |
| --- | --- | --- |
| 13 | 未修改原附件导入后导出 | 首次2成功/1新增/1更新，最终50/70；不含税、税金、含税金额符合下表；进程重启后结果一致 |
| 14 | 零基础521/520和负利润 | 税金46.89/46.80、利润0.09，含税567.89/566.80、利润1.09；原附件负利润方向正确；1/521显示0.19% |
| 15 | 0条、1条、30条其他费用 | 六项保留，连续序号及合计位置正确；打印范围动态扩展，样式数不会随费用行数增加 |
| 16 | 重新打开并对照样例 | NPOI回读检查数值类型、空值、格式、字体、颜色、边框、列宽/行高、合并及打印范围；原样例和实际导出已渲染逐行对照 |
| 17 | 修改、删除、重启、缺基础记录 | 修改后的名称、金额、备注即时出现在导出；删除记录消失且序号连续；重启保留；缺设计费/固定项明确失败 |

| 原附件导出汇总行 | 收入 | 成本 | 利润 |
| --- | ---: | ---: | ---: |
| 不含税合计 | 34558.40 | 122765.73 | -88207.33 |
| 税金（9%） | 3110.26 | 11048.92 | -7938.66 |
| 含税合计 | 37668.66 | 133814.65 | -96145.99 |

补充覆盖空/零/负收入、汇总率不能平均、半分税金分别舍入、持久化项目名称/面积/设计费/税率、跨项目隔离、并发生成、文本不写成公式、导出清单回读导入保留原Id和空收入。

## 格式及打印

保留项目汇总清单工作表、A1:J1合并、十列表头、微软雅黑字体（标题/表头10磅加粗、数据9磅）、五类RGB底色、细边框，以及列宽8/28/18/18/18/10/18/18/15/12。标题28磅，其余24磅。数值右对齐，两位金额及百分比格式按合并需求修正；样例的文本金额、错误税金方向和序号43不复制。

原样例仅包含页边距，没有显式纸张、方向、缩放、打印区域或重复标题设置。导出保留默认纸张设置及0.7/0.75英寸页边距、0.3英寸页眉页脚边距，并增加随行数变化的打印区域A1:J末行。渲染图用于对照清单布局，未把预览图作为打印分页证明。

## 复现与证据

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test.ps1 -OutputPath artifacts/mvp3-validation-build -OriginalImportFile 'C:\路径\导入其他费用清单.xlsx'
powershell -ExecutionPolicy Bypass -File .\scripts\smoke.ps1 -SkipBuild -ApiDirectory artifacts/mvp3-validation-build/Release/net48
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-import.ps1 -SkipBuild -ApiDirectory artifacts/mvp3-validation-build/Release/net48 -File 'C:\路径\导入其他费用清单.xlsx'
powershell -ExecutionPolicy Bypass -File .\scripts\smoke-export.ps1 -SkipBuild -ApiDirectory artifacts/mvp3-validation-build/Release/net48 -File 'C:\路径\导入其他费用清单.xlsx'
```

锁定依赖还原及Release构建0警告、0错误；91个自动化用例通过、0失败、0跳过。真实HTTP：CRUD9项、导入8项、导出链路15项，共32项通过。演示使用独立进程和独立临时库，未Reset用户的App_Data演示库。

原附件导入前后SHA256不变：F3BC9CEC88617A661F71B9A4847237FEC37494C3C4A52587AC74FE7FA3E1CA6F。

本次证据：`artifacts/test-results/mvp3.trx`；`artifacts/export-smoke/d852168a6d7041bbb5f2a7181fe529f1/export-http-smoke.json`及其中`original-import-summary.xlsx`；CRUD `artifacts/smoke/ee7a6fef86a2436995a0e1d4f7f31c62/http-smoke.json`；导入`artifacts/import-smoke/3c2aa70e47574caab5db785365996f52/import-http-smoke.json`。原附件、运行库、Excel证据和日志不进入公开Git；脚本可重新生成。

## 后续工作

MVP4已整理干净源码验证记录、实现思路Word、提交包、接口示例及运行截图；详情见[MVP4交付记录](MVP4交付记录.md)。原题没有规定视频录制。导入遇到未预期系统数据库故障的边界仍见MVP2验收记录。
