# 调试多文件输出问题排查

## 问题现象

用户在调试 MainWorkflow.cs 时，设置断点在第 15 行 `Log(now2.ToString());`

但是输出显示：

```
[14:33:11] 1新工作流已启动1
[14:33:11] 测试动态变更后，代码智能提示
[14:33:11] ● 断点命中: 第 15 行
```

**问题**：在断点命中**之前**，NewWorkflow1 的输出就已经显示了！

这两行输出来自 NewWorkflow1.cs：

```csharp
public class NewWorkflow1 : CodedWorkflowBase
{
    public override void Execute()
    {
        Console.WriteLine("1新工作流已启动1");  // <-- 第一行输出
        Test2("1");
    }

    public void Test2(string abc){
        Console.WriteLine("测试动态变更后，代码智能提示");  // <-- 第二行输出
    }
}
```

## 可能的原因

### 假设 1: 多个文件被插桩

如果 NewWorkflow1.cs 也被插桩了（添加了 `__debugCallback` 字段），那么它可能被误认为是要执行的工作流类。

### 假设 2: mainFilePath 参数问题

如果 `mainFilePath` 为空或 null，根据判断逻辑：

```csharp
bool isMainFile = string.IsNullOrEmpty(mainFilePath) ||
                 fileName.Equals(Path.GetFileName(mainFilePath), StringComparison.OrdinalIgnoreCase) ||
                 codeFiles.Count == 1;
```

所有文件都会被判断为主文件，全部被插桩！

### 假设 3: 执行了错误的类

即使只有 MainWorkflow 被插桩，如果类型查找逻辑出错，可能执行了 NewWorkflow1。

## 已添加的调试输出

为了排查问题，我在 DebuggerServiceV3.cs 中添加了详细的调试输出：

### 1. 主文件路径（行 93-97）

```csharp
var mainFileInfo = $"[调试] 主文件路径: {mainFilePath ?? "null"}";
System.Diagnostics.Debug.WriteLine(mainFileInfo);
if (_uiContext != null)
    _uiContext.Post(_ => OutputMessage?.Invoke(mainFileInfo), null);
```

**输出内容**：显示传入的 `mainFilePath` 参数值

### 2. 插桩信息（行 115-128）

```csharp
if (isMainFile)
{
    var msg = $"[调试] ✓ 主文件已插桩: {fileName}";
    System.Diagnostics.Debug.WriteLine(msg);
    if (_uiContext != null)
        _uiContext.Post(_ => OutputMessage?.Invoke(msg), null);
    instrumentedFiles[fileName] = instrumentedCode;
}
else
{
    var msg = $"[调试] • 依赖文件(未插桩): {fileName}";
    System.Diagnostics.Debug.WriteLine(msg);
    if (_uiContext != null)
        _uiContext.Post(_ => OutputMessage?.Invoke(msg), null);
    instrumentedFiles[fileName] = code;
}
```

**输出内容**：显示每个文件是否被插桩

### 3. 候选类型（行 189-192）

```csharp
var msg1 = $"[调试] 找到 {candidateTypes.Count} 个被插桩的工作流类: {string.Join(", ", candidateTypes.Select(t => t.Name))}";
System.Diagnostics.Debug.WriteLine(msg1);
if (_uiContext != null)
    _uiContext.Post(_ => OutputMessage?.Invoke(msg1), null);
```

**输出内容**：显示有多少个类被插桩（有 `__debugCallback` 字段）

### 4. 执行的类（行 204-221）

```csharp
if (workflowType != null)
{
    var msg2 = $"[调试] ✓ 执行类: {workflowType.Name} (匹配文件名)";
    System.Diagnostics.Debug.WriteLine(msg2);
    if (_uiContext != null)
        _uiContext.Post(_ => OutputMessage?.Invoke(msg2), null);
}
```

或

```csharp
if (workflowType != null)
{
    var msg3 = $"[调试] ⚠ 执行类: {workflowType.Name} (未匹配文件名，使用第一个)";
    System.Diagnostics.Debug.WriteLine(msg3);
    if (_uiContext != null)
        _uiContext.Post(_ => OutputMessage?.Invoke(msg3), null);
}
```

**输出内容**：显示最终执行的是哪个类

## 预期的正确输出

如果一切正常，用户应该看到：

```
=== 开始调试 ===
设置了 2 个断点
调试文件: MainWorkflow.cs
检测到项目目录中有其他文件，加载依赖文件...
  [依赖] DataProcessing.cs
  [依赖] ErrorHandlingExample.cs
  [依赖] HelloWorld.cs
  [依赖] Helper.cs
  [主] MainWorkflow.cs
  [依赖] NewWorkflow1.cs
  [依赖] NewWorkflow2.cs
  [依赖] NewWorkflow3.cs
  [依赖] UserInputExample.cs
  [依赖] WorkflowTemplate.cs
[调试] 主文件路径: E:\ai_app\actipro_rpa\TestWorkflows\MainWorkflow.cs
[调试] • 依赖文件(未插桩): DataProcessing.cs
[调试] • 依赖文件(未插桩): ErrorHandlingExample.cs
[调试] • 依赖文件(未插桩): HelloWorld.cs
[调试] • 依赖文件(未插桩): Helper.cs
[调试] ✓ 主文件已插桩: MainWorkflow.cs
[调试] • 依赖文件(未插桩): NewWorkflow1.cs
[调试] • 依赖文件(未插桩): NewWorkflow2.cs
[调试] • 依赖文件(未插桩): NewWorkflow3.cs
[调试] • 依赖文件(未插桩): UserInputExample.cs
[调试] • 依赖文件(未插桩): WorkflowTemplate.cs
[调试] 找到 1 个被插桩的工作流类: MainWorkflow
[调试] ✓ 执行类: MainWorkflow (匹配文件名)
● 断点命中: 第 15 行
```

## 可能的异常输出

### 情况 1: 所有文件都被插桩

```
[调试] 主文件路径: null
[调试] ✓ 主文件已插桩: DataProcessing.cs
[调试] ✓ 主文件已插桩: ErrorHandlingExample.cs
[调试] ✓ 主文件已插桩: HelloWorld.cs
[调试] ✓ 主文件已插桩: Helper.cs
[调试] ✓ 主文件已插桩: MainWorkflow.cs
[调试] ✓ 主文件已插桩: NewWorkflow1.cs
...
[调试] 找到 10 个被插桩的工作流类: DataProcessing, ErrorHandlingExample, HelloWorld, MainWorkflow, NewWorkflow1, ...
[调试] ⚠ 执行类: DataProcessing (未匹配文件名，使用第一个)
```

**原因**：`mainFilePath` 是 null，导致所有文件都被判断为主文件

### 情况 2: 文件名不匹配

```
[调试] 主文件路径: E:\ai_app\actipro_rpa\TestWorkflows\MainWorkflow.cs
[调试] • 依赖文件(未插桩): DataProcessing.cs
...
[调试] ✓ 主文件已插桩: MainWorkflow.cs
[调试] • 依赖文件(未插桩): NewWorkflow1.cs
...
[调试] 找到 1 个被插桩的工作流类: MainWorkflow
[调试] ⚠ 执行类: MainWorkflow (未匹配文件名，使用第一个)
```

**原因**：`_mainFileName` 与实际类名不匹配（不太可能）

### 情况 3: 字典顺序问题

如果文件在字典中的顺序不是按照加载顺序，可能 NewWorkflow1.cs 被先处理。

## 排查步骤

### 步骤 1: 查看调试输出

用户重新编译并运行后，查看输出窗口中的 `[调试]` 前缀的消息。

### 步骤 2: 确认主文件路径

检查输出中的：
```
[调试] 主文件路径: ...
```

- 如果是 `null`：说明 MainViewModel 传递的 `CurrentFilePath` 有问题
- 如果是完整路径：继续下一步

### 步骤 3: 确认插桩情况

检查输出中的：
```
[调试] ✓ 主文件已插桩: ...
[调试] • 依赖文件(未插桩): ...
```

- 应该只有一个文件显示"主文件已插桩"
- 其他文件应该显示"依赖文件(未插桩)"

### 步骤 4: 确认候选类型

检查输出中的：
```
[调试] 找到 X 个被插桩的工作流类: ...
```

- 应该只有 1 个类（MainWorkflow）
- 如果有多个，说明有多个文件被插桩了

### 步骤 5: 确认执行的类

检查输出中的：
```
[调试] ✓ 执行类: ...
```

- 应该是 MainWorkflow
- 如果是其他类，说明类型查找逻辑有问题

## 修复方案

根据排查结果，可能的修复方案：

### 方案 1: 修复 mainFilePath 传递

如果 `mainFilePath` 是 null，修改 MainViewModel.cs：

```csharp
// 确保 CurrentFilePath 不为空
if (string.IsNullOrEmpty(CurrentFilePath))
{
    AppendOutput("[错误] 当前文件路径为空");
    return;
}

success = await _debugger.StartDebuggingAsync(codeFiles, _compiler, CurrentFilePath);
```

### 方案 2: 改进 isMainFile 判断逻辑

移除 `string.IsNullOrEmpty(mainFilePath)` 条件：

```csharp
// 判断是否是主文件（需要插桩的文件）
bool isMainFile = !string.IsNullOrEmpty(mainFilePath) &&
                 fileName.Equals(Path.GetFileName(mainFilePath), StringComparison.OrdinalIgnoreCase);
```

这样只有明确匹配的文件才会被插桩。

### 方案 3: 文件顺序问题

如果是字典顺序问题，确保 MainViewModel 在加载文件时，主文件最后加载（或者显式标记）。

## 下一步

等待用户提供新的调试输出，根据输出信息确定具体问题并实施修复。

---

**更新时间**: 2026-01-13
**状态**: 等待用户测试并提供调试输出
