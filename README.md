# B组后端：其他费用维护与导入

已实现本地 WebAPI、SQLite 初始化、Reset、其他费用新增/查询/修改/删除，以及 MVP2 Excel 导入。目标框架为 **C# / .NET Framework 4.8**，运行于 Windows x64。Excel 导出仍待 MVP3；当前还不是完整 B 组交付。

先读 [第一节：业务与代码](docs/MVP1业务与代码.md)，再运行下面的示例。原题依据为上级目录中的 `其他费用录入、导入与导出-需求实现.docx`，原题与旧材料冲突时以该文档为准。

## 本机运行

在这个 `b-group` 文件夹打开 PowerShell：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\test.ps1 -SkipBuild
powershell -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

启动后访问 `http://localhost:5080/api/health`，按 Ctrl+C 停止。需要 Windows 的 .NET Framework 4.8 或兼容版本；当前机器已验证可运行。首次构建需要联网还原 NuGet 包，便携构建 SDK 位于 `.tools/dotnet`；缺失时构建脚本会下载并校验它。该 SDK 只用于编译，应用运行在 .NET Framework 4.8 上。

保持服务窗口运行，在第二个 PowerShell 窗口依次调用：

```powershell
# 每轮题目验收先Reset，得到确定的初始数据；它会清空当前演示库。
Invoke-RestMethod -Method Post -Uri http://localhost:5080/api/reset

$feeJson = '{"ProjectId":"7-51231123","Name":"测试其他费用","IncomePrice":521.00,"CostPrice":520.00}'
$createdFee = Invoke-RestMethod -Method Post -Uri http://localhost:5080/api/other-fees -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($feeJson))
$createdFee

Invoke-RestMethod -Uri http://localhost:5080/api/projects/7-51231123/other-fees
```

新增应返回利润 `1.00`、`ProfitRatePercent = 0.19`（含义是 `0.19%`）。同项目重复新增返回 409；先前的数据仍保留。JSON 数值可能显示成 `1.0`，这是数值表示差异；金额精度仍为两位小数，固定显示格式由后续 Excel 负责。

完整 CRUD 请求见 [other-fees.http](examples/other-fees.http)。可以在支持 HTTP 文件的工具中执行，也可以复制到 Postman；修改、删除使用新增响应中的真实 `Id`。

## Excel 导入（MVP2）

接口按请求路径指定目标项目。五列表格还必须在每一行提供相同的项目Id；十列标准清单从“其他费用”章节读取费用行。列顺序可以变化，程序按表头定位；重复关键表头或无法识别的文件结构会在写入前整体拒绝。

```powershell
curl.exe -F "file=@C:\路径\导入其他费用清单.xlsx" http://localhost:5080/api/projects/7-51231123/other-fees/import
```

同项目同名导入会覆盖金额和备注，保留原记录 Id 与顺序。逐行验证和保存：有效行成功，失败行在 `Errors` 中返回 Excel 行号、字段、错误码和原因；一个失败行不会撤销此前的成功行，也不会阻止后续行。`FailureCount` 表示失败行数，`Errors` 可包含该行的多个字段错误。

## 接口

| 方法 | 路径 | 成功响应 | 行为 |
| --- | --- | --- | --- |
| POST | `/api/other-fees` | 201，费用 JSON | 新增，禁止同项目重名 |
| GET | `/api/projects/{projectId}/other-fees` | 200，JSON 数组 | 按新增顺序返回全部其他费用，含利润/利润率 |
| POST | `/api/projects/{projectId}/other-fees/import` | 200，导入报告 | multipart/form-data 上传一个 `.xls` 或 `.xlsx` 文件；报告含成功、新增、更新、失败数量和逐行错误 |
| PUT | `/api/other-fees/{id}` | 200，费用 JSON | 替换名称、收入、成本、备注，ID 和所属项目不变 |
| DELETE | `/api/other-fees/{id}` | 204，无响应体 | 按记录 ID 删除 |
| POST | `/api/reset` | 200，初始化数量 JSON | 清空并初始化项目、固定工项、设计费 |
| GET | `/api/health` | 200，状态 JSON | 确认服务可访问 |

PUT 的名称、成本必填；收入、备注省略或传 null 均变为 null。ProjectId 不能通过 PUT 改变。客户端传入 `Profit`、`ProfitRatePercent` 或其他未知字段会返回 400。错误 JSON 包含 `Code`、`Message`、`Field`：输入错误 400、记录/项目不存在 404、同项目重名 409。

## 数据与配置

`scripts/run.ps1` 默认数据库在 `App_Data/other-fees.sqlite`，启动时自动建表并在空库初始化。正常重启保留已有数据；重新验收时先 Reset，避免依赖残留数据。运行脚本可用 `-Url http://localhost:5082/` 换端口；`-ResetDisabled` 禁用 Reset 接口。

直接执行 exe 时读取 [App.config](src/OtherFees.Api/App.config) 的 BaseUrl、DatabasePath、EnableReset；相对数据库路径基于 exe 所在目录。命令行 `--url`、`--database`、`--reset-disabled` 可覆盖配置。运行脚本显式传入项目根目录下的数据库绝对路径，避免因为运行目录不同而读错库。

[schema.sql](database/schema.sql) 是建表脚本；[seed.json](database/seed.json) 是初始化输入，构建时复制到 exe 的 `database` 文件夹。修改种子后重新构建并 Reset 生效。初始化含两个项目，每个项目六个固定工项及一笔设计费，其他费用为零。演示项目的固定工项金额来自原题导出样例，另一个零基础项目用于验证隔离性；项目名称为演示补充。程序业务逻辑没有按样例项目 ID 或费用名称写特例。

项目、固定工项、设计费、其他费用均持久化，并通过 ProjectId 关联。金额使用 `decimal` 计算，SQLite 使用整数“分”保存；空收入保持 SQL NULL。利润、利润率从当前收入和成本计算，不单独存储。

## 原题规则与补充约定

| 来源 | 规则 |
| --- | --- |
| 原题明确 | 名称必填且项目内唯一；成本必填且非负；收入可空；服务端计算利润；空/零收入利润率为零 |
| 原题明确 | 收入减成本得到利润，利润率以收入为分母；四层组织、SQLite、本地 WebAPI、Reset |
| 暂定实现 | 空收入仍保存 null；计算利润时暂按 0 收入，因此成本 20 时利润为 -20。原题没有单独说明这时的利润 |
| 暂定实现 | 先将输入金额四舍五入至分，恰好半分时远离零取整；再计算利润。API 返回百分数值并保留两位，如 1/521 → 0.19%，避免先将比率舍入为 0 |
| 暂定实现 | 名称去掉两端空白并做 Unicode NFC 规范化，按大小写敏感比较；拒绝固定工项名及清单章节/合计名，以免后续导入错误分类 |
| 原题未禁止 | 收入允许负数，成本仍不得为负。负收入按公式计算，可产生不直观的利润率；不额外增加原题未给出的收入限制 |

这些补充约定延续需求方案，属于当前实现选择，并非题目原文全部明确规定。题目清单利润率与 A 组“基数 × 费率”的利润取费是两套计算规则。

## 验证

```powershell
# 自动化测试：独立临时 SQLite 库，不修改你的演示库
powershell -ExecutionPolicy Bypass -File .\scripts\test.ps1

# 真实 HTTP：独立进程、独立库，含停止/重启；默认5091端口
powershell -ExecutionPolicy Bypass -File .\scripts\smoke.ps1 -SkipBuild
```

验证覆盖金额/利润率、字段校验、项目内重名及并发重名、SQLite 持久化、修改/删除、Reset 回滚、JSON 错误与 HTTP 状态码。测试结果写入 `artifacts/test-results/mvp1.trx`；HTTP 演示的 JSON 和日志写入独立的 `artifacts/smoke/{本次ID}` 文件夹。验收映射及尚未完成的条款见 [MVP1验收记录](docs/MVP1验收记录.md)。

## 后续 MVP

| 阶段 | 完成条件 |
| --- | --- |
| MVP1，本次 | 本地能启动；Reset 后 CRUD 正常；边界错误清楚；持久化和计算验证通过 |
| MVP2 | 已实现五列与十列导入、按表头/章节识别、同名覆盖和逐行报告；更多异常单元格与原题演示仍待完整验收 |
| MVP3 | 从数据库生成完整清单；汇总/税金/利润率正确；样式与打印设置一致；导入→导出完整演示 |
| MVP4 | 完整复验、实现思路文档、演示录屏与提交材料齐全，能够独立解释业务及代码 |

## Git 与 GitHub

本项目使用本地 Git 管理版本，并按用户选择同步至公开 GitHub 仓库。`commit` 在本机保存版本，`push` 才上传；提交说明中的 `feat`、`fix`、`docs` 只是改动类型，不会触发上传。

每项独立改动完成并通过相关验证后提交、同步；`main` 保持可运行，每个通过验收的 MVP 用标签标记。MVP2、MVP3 使用工作分支开发，验证后合并。撤销已同步改动优先 `git revert`，避免强制推送改写历史。

源码、测试、schema/seed、依赖锁文件、README及[需求方案](docs/B组需求边界验收标准与MVP实施方案.docx)进入Git。需求方案是上级 `deliverables` 阅读件的同步快照，修改时保持两份一致。原始考题附件、运行数据库、工具包、编译结果、日志和测试产物不进入公开仓库；这些内容通过脚本和Reset复现。

```powershell
git status
git add <本次改动的文件>
git diff --cached
git commit -m "feat: add other fee CRUD"
git push origin main
```

构建工具来源：[Microsoft .NET 8 发布元数据](https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/8.0/releases.json)。包版本在各项目 `.csproj` 与 `packages.lock.json` 中固定；金额业务不依赖构建工具版本。
