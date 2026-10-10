# 第一节：一笔其他费用怎样进入系统

这一节的目标：你能独立解释“为什么这样设计”，并沿着一笔新增请求找到对应代码。先理解新增这条流程，再看修改和删除。

## 1. 这笔数据表示什么

原题示例：

```json
{"ProjectId":"7-51231123","Name":"测试其他费用","IncomePrice":521.00,"CostPrice":520.00}
```

它表示在某个项目的六个固定工项之后增加一笔其他费用。`ProjectId` 表示归属项目，服务端生成的 `Id` 表示这一笔记录。一个项目可以有多笔费用；同项目内费用名称不能重复，不同项目可以同名。

收入是这笔费用收取的金额，成本是为它付出的金额：

```text
利润 = 521 - 520 = 1元
利润率 = 1 / 521 × 100% ≈ 0.19%
```

这不是 A 组按人工费等基数取费的“利润费”。B 组讨论的是收入与成本相减后的盈亏。亏损允许存在，例如收入 50、成本 70，利润 -20，利润率 -40%。

初始化数据中，项目通过 ProjectId 关联六个固定工项、一笔设计费及多笔其他费用。它们会共同参与后续清单导出；这一阶段的费用接口只维护其他费用。

## 2. 为什么先分四层

| 层 | 这层回答的问题 | 本项目对应文件 | Java 类比 |
| --- | --- | --- | --- |
| Interface 接口层 | HTTP 请求如何接收、响应？ | `OtherFeesController.cs` | Spring MVC Controller |
| Application 应用层 | 新增这个动作按什么顺序执行？ | `OtherFeeService.cs` | Service 用例编排 |
| Domain 领域层 | 一笔费用必须遵守什么规则？ | `OtherFee.cs`、`Money.cs` | 含业务行为的领域对象 |
| Infrastructure 基础设施层 | 数据如何放进 SQLite？ | `SqliteOtherFeeRepository.cs` | Repository/DAO |

四个独立项目让依赖关系更容易检查：Domain 不认识 HTTP、SQLite；Application 使用 Domain 和仓储接口；Infrastructure 实现这些接口；Api 负责组装它们。Api 是一个可执行的宿主，在本机启动 WebAPI。

这阶段使用 WebAPI 2 和 OWIN 自托管，目标 .NET Framework 4.8；项目没有要求部署到 IIS，采用自托管便于你本机演示。等业务层稳定后，宿主可以调整，费用规则仍然集中在领域对象中。

## 3. 顺着请求读四个文件

先打开 [OtherFeesController.cs](../src/OtherFees.Api/OtherFeesController.cs)，找到 `Create`。它接收 JSON，检查请求格式和未知字段，然后调用应用服务。它不计算利润，也不访问 SQL。`[HttpPost]` 和 `[Route(...)]` 可以类比 Java 的 `@PostMapping`；`[FromBody]` 可以类比 `@RequestBody`。

再看 [OtherFeeService.cs](../src/OtherFees.Application/OtherFeeService.cs) 的 `Create`：

```csharp
OtherFee fee = OtherFee.Create(projectId, name, income, cost, remark);
RequireProject(fee.ProjectId);
RequireUniqueName(fee);
return View(fees.Insert(fee));
```

顺序是：构造有效费用 → 确认项目存在 → 查询同项目同名记录 → 让领域判断名称是否可用 → 保存并转换成响应。单笔费用本身无法知道数据库里有没有重名，因此应用服务通过仓储取得这个事实，领域中的 `EnsureNameAvailable` 决定重名时拒绝。

接着看 [OtherFee.cs](../src/OtherFees.Domain/OtherFee.cs) 的 `ChangeDetails`：名称不能空，成本必须有值且不能为负；先验证、转换所有新值，全部有效后才改变对象。如果成本无效，不会出现名称改了、金额没改的半完成状态。创建和修改都复用它，避免两套校验出现差异。

最后看 [SqliteOtherFeeRepository.cs](../src/OtherFees.Infrastructure/SqliteOtherFeeRepository.cs) 的 `Insert`、`Save`。SQL 只出现在这里，通过参数绑定写入数据库。事务保证写入过程完整，查询到保存结果后提交。金额不使用 SQL 浮点数，而是整数分。

`Program.cs` 在启动时创建数据库、仓储和服务，并交给接口层。当前只有少量对象，显式构造足够，不需要为了分层先引入复杂容器。

## 4. 收入、成本为什么存，利润为什么不存

收入、成本是业务输入，要持久化。利润是由二者决定的结果，查询时通过属性计算：

```csharp
public decimal Profit => (Income?.Amount ?? 0m) - Cost.Amount;
```

`=>` 是表达式形式的返回值，可以理解为 Java 的 `getProfit()` 方法。`?.` 表示对象为空就不继续取属性；`?? 0m` 表示结果为空时使用 0。`m` 表示 C# 的 decimal 字面值。

这样修改成本后，利润总是按最新金额计算。客户端不能提交利润，数据库也不保存另一份可能过期的利润。

空收入保存为 null；暂定利润计算时把它当 0，利润率按原题要求为 0。保存 null 是为了保留“未填写”的信息；填写 0 与未填写的业务状态不同。这里的空收入利润口径是补充约定，原题只明确了它的利润率口径。

金额使用 C# 的 `decimal`，可以类比 Java 中用 `BigDecimal` 处理金额，但二者的范围和用法并不完全相同。`decimal?` 表示可以为 null 的 decimal，类似 Java 中可以为 null 的对象类型。`Money` 负责金额精度及可保存范围，比如 12.345 元按当前约定变为 12.35 元，保存 1235 分。

API 的 `ProfitRatePercent` 是百分数值，如 `0.19` 表示 `0.19%`。后续写 Excel 百分比单元格时需要转换回比例 `0.0019`；不能直接写 `0.19` 再套百分比格式，否则会显示 `19%`。具体导出计算应复用同一口径。

## 5. 重名为什么检查两次

应用服务提前检查可以及时返回清楚的业务错误。数据库的 `UNIQUE(ProjectId, NormalizedName)` 则保证并发时也不会保存两笔重名费用。

例如两个请求同时检查，可能都觉得名称可用。只有其中一个能成功写入；另一个触发数据库唯一约束，仓储把它转换为同样的 `NAME_DUPLICATE` 错误，接口返回 409。测试已覆盖这个场景。

名称在进入领域时去掉两端空白并进行 Unicode 规范化。此阶段采用大小写敏感比较；这是补充约定。同名 Excel 覆盖更新将在独立的导入用例中实现，手工新增仍保持拒绝规则。

## 6. 修改、删除、重启和 Reset

修改通过记录 `Id` 找到原费用，复用 `ChangeDetails`，再检查新名称是否与同项目其他记录冲突。检查时排除自己的 Id，因此只改金额而不改名称可以正常提交。修改不会更换 Id、所属项目或新增顺序。

删除按记录 Id 执行；不存在时返回 404。已有项目没有任何费用时查询返回 `[]`；项目不存在时返回 404。两种状态不能混淆。

正常启动只初始化空库，不清空已有数据，所以服务重启后新增的费用还在。Reset 是显式恢复验收基线：一个事务中清空业务表并重新初始化项目、固定工项和设计费。种子无效时整笔事务回滚，原数据仍保留。每轮题目验证先 Reset，就不会依赖上轮残留的数据。

## 7. 先用自己的话解释这四件事

1. ProjectId 与费用 Id 各代表什么？为什么修改用费用 Id？
2. 为什么客户端不能传利润，数据库也不存利润？
3. 为什么利润率是 0.19%，分母用的是收入 521？
4. 为什么同项目重名要同时有应用检查和数据库约束？

你能沿着 Controller → Service → OtherFee → Repository 指出答案后，再看 [验收记录](MVP1验收记录.md)，理解每类测试在证明什么。这一节还不要求你掌握全部 C# 语法；先能够把业务动作与代码职责对应起来。
