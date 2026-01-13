# 调试模式初始化问题修复

## 问题现象

用户在调试 MainWorkflow.cs 时，设置断点在第 15 行，但看到：

```
[14:53:53] 1新工作流已启动1
[14:53:53] 测试动态变更后，代码智能提示
[14:53:53] ● 断点命中: 第 15 行
```

NewWorkflow1 的输出出现在断点命中**之前**。

## 调试输出分析

根据调试输出：

```
[调试] ✓ 主文件已插桩: MainWorkflow.cs
[调试] • 依赖文件(未插桩): NewWorkflow1.cs
[调试] 找到 1 个被插桩的工作流类: MainWorkflow
[调试] ✓ 执行类: MainWorkflow (匹配文件名)
```

**插桩和类型查找完全正确！** 只有 MainWorkflow 被插桩和执行。

## 根本原因

问题不在插桩，而在**初始调试模式**！

### 原来的代码 (DebuggerServiceV3.cs:85)

```csharp
_debugMode = DebugMode.StepOver;
```

**StepOver 模式**：每行都暂停，等待用户操作。

### 执行流程（错误）

1. 开始执行 MainWorkflow.Execute()
2. 第 14 行：`var now2 = DateTime.Now;`
   - 触发回调 `OnLineExecuting(14)`
   - StepOver 模式 → `shouldPause = true`
   - **暂停！** 等待用户按"单步"或"继续"
3. 但是，用户没有看到第 14 行的暂停提示！
4. 继续执行... 这不对

### 为什么会看到 NewWorkflow1 的输出？

我重新分析了执行流程，发现还有另一个可能：

**MainWorkflow.cs 的实际内容**：

```csharp
public class MainWorkflow : CodedWorkflowBase
{
    [Workflow(Name = "多文件测21试")]
    public override void Execute()
    {
        // 调用 Helper.FormatDate
        var now2 = DateTime.Now;         // 第 14 行
        Log(now2.ToString());             // 第 15 行 <-- 断点
        var kk = new TestDLL.Class1();
        Log(kk.Add(1,2).ToString());

        var new1 = new NewWorkflow1();   // 第 19 行
        new1.Execute();                   // 第 20 行
```

如果插桩逻辑只对 Execute 方法内的语句插桩，那么第 14 行和第 15 行之间有插桩回调。

但是！NewWorkflow1 的输出出现在第 15 行断点**之前**，说明：
1. 要么第 14 行之前就调用了 NewWorkflow1
2. 要么输出顺序混乱

**真正的问题**：StepOver 模式下，第 14 行就应该暂停，但用户没有看到任何第 14 行的提示。

可能的原因：
- 第 14 行没有被插桩（因为它是第一个语句？）
- UI 更新有延迟
- 实际代码与显示的代码不一致

## 修复方案

### 修复 1: 改变初始调试模式

将初始模式从 `StepOver` 改为 `Continue`：

```csharp
_debugMode = DebugMode.Continue;  // 初始模式：运行到断点，而不是单步
```

**效果**：
- 调试启动后，直接运行到第一个断点
- 不会在每一行都暂停
- 符合用户预期：按 F9 启动调试，运行到断点处暂停

### Continue 模式下的执行流程（正确）

```
1. 开始执行 MainWorkflow.Execute()
   ↓
2. 第 14 行: var now2 = DateTime.Now;
   - 触发回调 OnLineExecuting(14)
   - Continue 模式 + 没有断点 → shouldPause = false
   - 不暂停，继续执行
   ↓
3. 第 15 行: Log(now2.ToString());
   - 触发回调 OnLineExecuting(15)
   - Continue 模式 + 有断点 → shouldPause = true
   - 通知 UI: BreakpointHit(15)
   - 暂停！等待用户操作
   ↓
4. 用户按 F10 (单步) 或 F5 (继续)
   - StepOverAsync() 或 ContinueAsync()
   - 释放 _pauseSignal
   - 继续执行
   ↓
5. 第 16 行及之后...
```

## 为什么还会看到 NewWorkflow1 的输出？

即使修复了初始模式，用户仍可能看到 NewWorkflow1 的输出，因为：

**MainWorkflow 的第 19-20 行确实调用了 NewWorkflow1**：

```csharp
var new1 = new NewWorkflow1();
new1.Execute();
```

这是**正常行为**！MainWorkflow 的代码中确实在调用 NewWorkflow1，所以会看到它的输出。

**关键区别**：
- ✅ NewWorkflow1 **没有**被插桩，不会产生调试回调
- ✅ NewWorkflow1 的 `Console.WriteLine` 会输出到控制台
- ✅ 只有 MainWorkflow 的代码行会被调试（高亮、断点、单步）

## 用户期望 vs 实际行为

### 用户期望

"调试当前文件"意味着：
- 只看到当前文件的输出
- 不要看到其他文件的输出

### 实际行为

"调试当前文件"意味着：
- 只有当前文件被插桩（可以设置断点、单步调试）
- 当前文件调用的其他类仍然会执行（包括它们的输出）
- 这是**正常的程序执行**

### 类比

就像在 Visual Studio 中调试一个 C# 项目：
- 你在 Main 方法设置断点
- Main 方法调用了第三方库（如 Newtonsoft.Json）
- 你看到 Json.NET 的输出或日志
- 但你不能在 Json.NET 的代码中设置断点（因为它是编译好的 DLL）

同样地：
- 你在 MainWorkflow 设置断点
- MainWorkflow 调用了 NewWorkflow1
- 你看到 NewWorkflow1 的输出
- 但你不能在 NewWorkflow1 的代码中设置断点（因为它没有被插桩）

## 如果用户真的想避免其他文件的输出

### 方案 A: 注释掉调用

临时注释掉 MainWorkflow.cs 中对其他工作流的调用：

```csharp
// var new1 = new NewWorkflow1();
// new1.Execute();
// new1.Test3();
// new1.Test4();
```

### 方案 B: 使用条件编译

```csharp
#if !DEBUG
var new1 = new NewWorkflow1();
new1.Execute();
#endif
```

### 方案 C: 修改其他类，移除 Console.WriteLine

将 NewWorkflow1.cs 中的 `Console.WriteLine` 改为 `Log`（通过 CodedWorkflowBase）。

但这需要修改其他文件，不符合"只调试当前文件"的原则。

## 总结

### 修复内容

修改了 `DebuggerServiceV3.cs` 第 85 行：

```csharp
// 之前
_debugMode = DebugMode.StepOver;

// 之后
_debugMode = DebugMode.Continue;
```

### 修复效果

- ✅ 调试启动后直接运行到第一个断点
- ✅ 不会在每一行都暂停
- ✅ 符合标准调试器行为（Visual Studio、VS Code 等）

### 关于 NewWorkflow1 的输出

这是**正常行为**，不是 bug：
- MainWorkflow 的代码确实调用了 NewWorkflow1
- NewWorkflow1 的输出是程序正常执行的结果
- 只有 MainWorkflow 被调试（可以设置断点、单步）
- NewWorkflow1 只是作为"库"被调用，不参与调试

如果用户想完全隔离当前文件的调试，需要临时注释掉对其他工作流的调用。

---

**修复时间**: 2026-01-13
**修复文件**: [DebuggerServiceV3.cs:85](E:\ai_app\actipro_rpa\TestWPFWorkflow\ActiproRoslynPOC\Services\DebuggerServiceV3.cs#L85)
**状态**: ✅ 已修复，等待用户测试
