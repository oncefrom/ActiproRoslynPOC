# 工作流运行功能说明

## 概述

当前的运行功能（**不是调试**）会根据项目目录中的文件数量自动选择模式：
- **单文件模式**: 只编译当前编辑器中的代码
- **多文件模式**: 编译项目目录中的所有 .cs 文件，并执行当前文件对应的类

---

## 运行流程

### 入口点: `ExecuteRun()`

[MainViewModel.cs:204-227](E:\ai_app\actipro_rpa\TestWPFWorkflow\ActiproRoslynPOC\ViewModels\MainViewModel.cs#L204-L227)

```csharp
private void ExecuteRun()
{
    Output = "";
    Diagnostics.Clear();

    // 检查是否有项目目录
    string projectDirectory = GetProjectDirectory();

    // 判断是否需要包含其他文件
    bool hasOtherCsFiles = !string.IsNullOrEmpty(projectDirectory) &&
                          Directory.Exists(projectDirectory) &&
                          Directory.GetFiles(projectDirectory, "*.cs").Length > 1;

    if (hasOtherCsFiles)
    {
        // 多文件模式
        ExecuteWithDependencies(projectDirectory);
    }
    else
    {
        // 单文件模式
        ExecuteSingleFile();
    }
}
```

**判断条件**:
- 如果项目目录存在 **且** 目录中有多个 .cs 文件 → **多文件模式**
- 否则 → **单文件模式**

---

## 模式 1: 单文件模式

### 触发条件
- 项目目录不存在
- 项目目录中只有 1 个 .cs 文件

### 执行流程

[MainViewModel.cs:229-274](E:\ai_app\actipro_rpa\TestWPFWorkflow\ActiproRoslynPOC\ViewModels\MainViewModel.cs#L229-L274)

```
1. 清空输出
   ↓
2. 编译当前编辑器中的代码 (Code 属性)
   ↓
3. 编译失败？
   → 是：显示错误并停止
   → 否：继续
   ↓
4. 在程序集中查找 CodedWorkflowBase 的子类
   ↓
5. 找不到？
   → 是：显示错误并停止
   → 否：继续
   ↓
6. 创建工作流实例
   ↓
7. 订阅 LogEvent 事件（输出到界面）
   ↓
8. 调用 workflow.Execute()
   ↓
9. 显示执行结果
```

**关键代码**:
```csharp
// 编译当前代码
var result = _compiler.Compile(Code);

// 查找工作流类
var type = result.Assembly.GetTypes()
    .FirstOrDefault(t => t.IsSubclassOf(typeof(CodedWorkflowBase)) && !t.IsAbstract);

// 创建实例并执行
var workflow = _compiler.CreateInstance<CodedWorkflowBase>(result.Assembly, type.Name);
workflow.LogEvent += (s, msg) => AppendOutput(msg);
workflow.Execute();
```

---

## 模式 2: 多文件模式

### 触发条件
- 项目目录存在
- 项目目录中有 **2 个或以上** .cs 文件

### 执行流程

[MainViewModel.cs:288-350](E:\ai_app\actipro_rpa\TestWPFWorkflow\ActiproRoslynPOC\ViewModels\MainViewModel.cs#L288-L350)

```
1. 清空输出
   ↓
2. 如果当前文件有未保存的修改 → 自动保存
   ↓
3. 扫描项目目录，加载所有 .cs 文件
   ↓
4. 显示所有加载的文件名
   ↓
5. 编译所有文件（CompileMultiple）
   ↓
6. 编译失败？
   → 是：显示错误并停止
   → 否：继续
   ↓
7. 根据当前文件名推断目标类名
   例如: "MainWorkflow.cs" → "MainWorkflow"
   ↓
8. 在程序集中查找匹配的工作流类
   优先匹配: 类名 == 推断的类名
   Fallback: 第一个 CodedWorkflowBase 子类
   ↓
9. 创建工作流实例
   ↓
10. 订阅 LogEvent 事件
   ↓
11. 调用 workflow.Execute()
   ↓
12. 显示执行结果
```

**关键代码**:
```csharp
// 加载所有 .cs 文件
var codeFiles = new Dictionary<string, string>();
var csFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories);

foreach (var filePath in csFiles)
{
    var fileName = Path.GetFileName(filePath);
    var code = File.ReadAllText(filePath);
    codeFiles[fileName] = code;
}

// 编译
var compileResult = _compiler.CompileMultiple(codeFiles);

// 推断类名
var targetTypeName = GetTypeNameFromFile(CurrentFilePath); // "MainWorkflow.cs" → "MainWorkflow"

// 查找匹配的类型
var workflowType = FindWorkflowType(compileResult.Assembly, targetTypeName);
```

---

## 类型查找逻辑

### `FindWorkflowType` 方法

[MainViewModel.cs:367-393](E:\ai_app\actipro_rpa\TestWPFWorkflow\ActiproRoslynPOC\ViewModels\MainViewModel.cs#L367-L393)

```csharp
private Type FindWorkflowType(Assembly assembly, string preferredTypeName)
{
    // 1. 获取所有工作流类型
    var workflowTypes = assembly.GetTypes()
        .Where(t => t.IsSubclassOf(typeof(CodedWorkflowBase)) && !t.IsAbstract)
        .ToList();

    if (workflowTypes.Count == 0)
        return null;

    // 2. 优先匹配指定名称
    if (!string.IsNullOrEmpty(preferredTypeName))
    {
        var matchedType = workflowTypes.FirstOrDefault(
            t => t.Name.Equals(preferredTypeName, StringComparison.OrdinalIgnoreCase)
        );
        if (matchedType != null)
            return matchedType;
    }

    // 3. 如果只有一个类型，返回它
    if (workflowTypes.Count == 1)
        return workflowTypes[0];

    // 4. 多个类型时，返回第一个并警告
    AppendOutput($"[警告] 找到 {workflowTypes.Count} 个工作流类型，使用第一个: {workflowTypes[0].Name}");
    return workflowTypes[0];
}
```

**查找顺序**:
1. ✅ **优先**: 类名与当前文件名匹配（不区分大小写）
2. ✅ **其次**: 如果只有一个工作流类，直接使用
3. ⚠️ **兜底**: 使用第一个工作流类（会显示警告）

---

## 项目目录配置

### `GetProjectDirectory` 方法

[MainViewModel.cs:276-286](E:\ai_app\actipro_rpa\TestWPFWorkflow\ActiproRoslynPOC\ViewModels\MainViewModel.cs#L276-L286)

```csharp
private string GetProjectDirectory()
{
    // 方法 1: 从配置文件读取
    // return ConfigurationManager.AppSettings["ProjectDirectory"];

    // 方法 2: 固定目录（当前使用）
    return @"E:\ai_app\actipro_rpa\TestWorkflows";

    // 方法 3: 当前文件所在目录
    // return Path.GetDirectoryName(CurrentFilePath);
}
```

**当前配置**: 固定返回 `E:\ai_app\actipro_rpa\TestWorkflows`

**可选方案**:
- **方案 1**: 从 App.config 读取配置
- **方案 2**: 固定目录（当前使用）
- **方案 3**: 使用当前文件所在目录

---

## 使用场景

### 场景 1: 独立文件

```
项目目录: E:\ai_app\actipro_rpa\TestWorkflows
文件列表:
  - SimpleWorkflow.cs  (仅此一个文件)

当前打开: SimpleWorkflow.cs
```

**运行行为**:
- ✅ 触发 **单文件模式**
- ✅ 只编译当前编辑器中的代码
- ✅ 执行 SimpleWorkflow 类

### 场景 2: 多文件项目 - 运行主文件

```
项目目录: E:\ai_app\actipro_rpa\TestWorkflows
文件列表:
  - MainWorkflow.cs
  - Helper.cs
  - Config.cs

当前打开: MainWorkflow.cs
```

**运行行为**:
- ✅ 触发 **多文件模式**
- ✅ 加载并编译所有 3 个文件
- ✅ 根据文件名推断类名: "MainWorkflow"
- ✅ 查找并执行 MainWorkflow 类
- ✅ MainWorkflow 可以引用 Helper 和 Config 中的类

### 场景 3: 多文件项目 - 运行其他文件

```
项目目录: E:\ai_app\actipro_rpa\TestWorkflows
文件列表:
  - MainWorkflow.cs
  - TestWorkflow.cs
  - Helper.cs

当前打开: TestWorkflow.cs
```

**运行行为**:
- ✅ 触发 **多文件模式**
- ✅ 加载并编译所有 3 个文件
- ✅ 根据文件名推断类名: "TestWorkflow"
- ✅ 查找并执行 TestWorkflow 类

---

## 输出示例

### 单文件模式输出

```
开始编译...
编译成功 (123ms)
开始执行...
1新工作流已启动1
测试动态变更后，代码智能提示
执行完成，耗时 156ms
返回结果: Success
```

### 多文件模式输出

```
项目模式：编译 E:\ai_app\actipro_rpa\TestWorkflows 中的所有 .cs 文件
  - MainWorkflow.cs
  - Helper.cs
  - Config.cs
编译成功！
执行类型: MainWorkflow
1新工作流已启动1
测试动态变更后，代码智能提示
执行完成！
返回结果: Success
```

---

## 常见问题

### Q1: 多文件模式下如何确定执行哪个类？

**A**: 根据**当前打开的文件名**推断类名。

例如:
- 打开 `MainWorkflow.cs` → 执行 `MainWorkflow` 类
- 打开 `TestFlow.cs` → 执行 `TestFlow` 类

### Q2: 如果类名和文件名不匹配会怎样？

**A**: 会出现以下情况之一:

1. **找到其他工作流类**: 使用第一个找到的类，并显示警告
2. **找不到任何类**: 显示错误 `[错误] 在 XXX.cs 中找不到 CodedWorkflowBase 的子类`

**建议**: 保持类名与文件名一致。

### Q3: 为什么多文件模式要加载所有文件？

**A**: 因为您的工作流可能引用其他文件中的类。例如:

```csharp
// MainWorkflow.cs
public class MainWorkflow : CodedWorkflowBase
{
    public override void Execute()
    {
        var helper = new Helper();  // Helper 定义在 Helper.cs
        helper.DoSomething();
    }
}
```

如果不加载 `Helper.cs`，编译会失败。

### Q4: 可以强制使用单文件模式吗？

**A**: 目前不能直接强制。但有两个方法:

1. **临时方法**: 将其他 .cs 文件移出项目目录
2. **修改代码**: 修改 `GetProjectDirectory()` 返回 `null`

### Q5: 如何更改项目目录？

**A**: 修改 [MainViewModel.cs:282](E:\ai_app\actipro_rpa\TestWPFWorkflow\ActiproRoslynPOC\ViewModels\MainViewModel.cs#L282):

```csharp
private string GetProjectDirectory()
{
    return @"E:\your\new\path";
}
```

或者使用当前文件目录:
```csharp
private string GetProjectDirectory()
{
    return Path.GetDirectoryName(CurrentFilePath);
}
```

---

## 关键组件

### 1. RoslynCompilerService

**单文件编译**:
```csharp
var result = _compiler.Compile(Code);
```

**多文件编译**:
```csharp
var result = _compiler.CompileMultiple(codeFiles);
```

### 2. CodedWorkflowBase

所有工作流类必须继承此基类:

```csharp
public class MyWorkflow : CodedWorkflowBase
{
    public override void Execute()
    {
        // 工作流逻辑
    }
}
```

**关键成员**:
- `Execute()`: 执行入口
- `LogEvent`: 日志事件，用于输出到界面
- `Result`: 执行结果

---

## 与调试模式的区别

| 特性 | 运行模式 (ExecuteRun) | 调试模式 (ExecuteStartDebugAsync) |
|------|---------------------|--------------------------------|
| 代码插桩 | ❌ 无 | ✅ 有 |
| 暂停功能 | ❌ 无 | ✅ 有（断点、单步） |
| 行号指示 | ❌ 无 | ✅ 有 |
| 变量查看 | ❌ 无 | ✅ 有 |
| 执行速度 | ⚡ 快 | 🐌 慢（因为插桩） |
| 多文件支持 | ✅ 是 | ⚠️ 当前只支持单文件 |
| 使用场景 | 正常运行和测试 | 调试和排错 |

---

## 总结

### ✅ 运行模式特点

1. **智能模式选择**: 自动判断单文件 vs 多文件
2. **多文件支持**: 可以引用其他文件中的类
3. **文件名匹配**: 根据当前文件名执行对应的类
4. **性能更好**: 没有代码插桩，执行速度快
5. **自动保存**: 多文件模式会自动保存修改

### 🎯 使用建议

- **单个独立工作流**: 让它自动选择单文件模式
- **有依赖关系的项目**: 让它使用多文件模式
- **保持命名一致**: 文件名和类名保持一致，避免混淆

---

**最后更新**: 2026-01-13
**当前版本**: 支持单文件和多文件模式的运行功能
