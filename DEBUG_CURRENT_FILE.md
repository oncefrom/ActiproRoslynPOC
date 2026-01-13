# 调试当前文件 - 工作原理

## 问题

之前的实现在多文件项目中会：
1. ❌ 加载所有 .cs 文件
2. ❌ 不清楚到底调试哪个文件
3. ❌ 可能调试错误的文件

## 解决方案

现在的实现会：
1. ✅ **调试当前打开/激活的文件**
2. ✅ 其他文件作为依赖加载（不插桩）
3. ✅ 明确显示正在调试哪个文件

## 工作流程

### 1. 用户操作
```
用户打开 MainWorkflow.cs
用户设置断点
用户按 F9 开始调试
```

### 2. MainViewModel 处理
```csharp
// 当前文件信息
CurrentFilePath = "E:\ai_app\actipro_rpa\TestWorkflows\MainWorkflow.cs"
CurrentFileName = "MainWorkflow.cs"
Code = "<当前编辑器中的代码>"

// 输出日志
AppendOutput("调试文件: MainWorkflow.cs");
AppendOutput("已加载 3 个文件（含依赖）");

// 调用调试器
_debugger.StartDebuggingAsync(codeFiles, _compiler, CurrentFilePath);
```

### 3. DebuggerServiceV3 处理
```csharp
// 遍历所有文件
foreach (var kvp in codeFiles)
{
    var fileName = kvp.Key;  // "MainWorkflow.cs", "Helper.cs", "Config.cs"

    // 判断是否是主文件
    bool isMainFile = fileName.Equals(
        Path.GetFileName(mainFilePath),  // "MainWorkflow.cs"
        StringComparison.OrdinalIgnoreCase
    );

    if (isMainFile)
    {
        // ✓ 调试主文件: MainWorkflow.cs (已插桩)
        var (instrumentedCode, lineMapping) = InstrumentCodeWithMapping(code);
        _lineMapping = lineMapping;
        instrumentedFiles[fileName] = instrumentedCode;
    }
    else
    {
        // • 依赖文件: Helper.cs (未插桩)
        // • 依赖文件: Config.cs (未插桩)
        instrumentedFiles[fileName] = code;
    }
}
```

### 4. 调试输出示例

```
[开始调试 (F9)]
=== 开始调试 ===
设置了 2 个断点
调试文件: MainWorkflow.cs
已加载 3 个文件（含依赖）

[调试输出窗口]
[DebuggerV3] ✓ 调试主文件: MainWorkflow.cs (已插桩)
[DebuggerV3] 行号映射: 10->10, 12->11, 14->12
[DebuggerV3] • 依赖文件: Helper.cs (未插桩)
[DebuggerV3] • 依赖文件: Config.cs (未插桩)
[DebuggerV3] 找到工作流类: MainWorkflow
[DebuggerV3] 回调委托已设置
[DebuggerV3] 开始调用 Execute 方法
```

## 关键代码

### MainViewModel.cs
```csharp
private async Task ExecuteStartDebugAsync()
{
    // ...

    if (hasOtherCsFiles)
    {
        // 多文件模式：调试当前文件，但包含其他文件以支持引用
        AppendOutput($"调试文件: {CurrentFileName}");

        // 加载所有文件（包括依赖）
        var codeFiles = new Dictionary<string, string>();
        var csFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories);

        foreach (var filePath in csFiles)
        {
            var fileName = Path.GetFileName(filePath);
            var code = File.ReadAllText(filePath);
            codeFiles[fileName] = code;
        }

        AppendOutput($"已加载 {codeFiles.Count} 个文件（含依赖）");

        // 启动调试：明确指定当前文件为主调试对象
        success = await _debugger.StartDebuggingAsync(codeFiles, _compiler, CurrentFilePath);
    }
    else
    {
        // 单文件模式：直接调试当前编辑器中的代码
        AppendOutput($"调试文件: {CurrentFileName}");
        success = await _debugger.StartDebuggingAsync(Code, _compiler);
    }
}
```

### DebuggerServiceV3.cs
```csharp
// 判断是否是主文件（需要插桩的文件）
bool isMainFile = string.IsNullOrEmpty(mainFilePath) ||
                 fileName.Equals(Path.GetFileName(mainFilePath), StringComparison.OrdinalIgnoreCase) ||
                 codeFiles.Count == 1;

if (isMainFile)
{
    // 对主文件插桩
    System.Diagnostics.Debug.WriteLine($"[DebuggerV3] ✓ 调试主文件: {fileName} (已插桩)");
    // ...
}
else
{
    // 其他文件保持原样
    System.Diagnostics.Debug.WriteLine($"[DebuggerV3] • 依赖文件: {fileName} (未插桩)");
    // ...
}
```

## 使用场景

### 场景 1: 单文件项目
```
项目目录:
  MainWorkflow.cs  <-- 当前打开

操作: F9 开始调试
结果:
  - 调试文件: MainWorkflow.cs
  - 直接调试当前编辑器中的代码
```

### 场景 2: 多文件项目
```
项目目录:
  MainWorkflow.cs  <-- 当前打开
  Helper.cs
  Config.cs

操作: F9 开始调试
结果:
  - 调试文件: MainWorkflow.cs
  - 已加载 3 个文件（含依赖）
  - ✓ 调试主文件: MainWorkflow.cs (已插桩)
  - • 依赖文件: Helper.cs (未插桩)
  - • 依赖文件: Config.cs (未插桩)
```

### 场景 3: 切换到其他文件调试
```
项目目录:
  MainWorkflow.cs
  Helper.cs  <-- 切换到这个文件并打开
  Config.cs

操作:
  1. 双击打开 Helper.cs
  2. 设置断点
  3. F9 开始调试

结果:
  - 调试文件: Helper.cs
  - 已加载 3 个文件（含依赖）
  - ✓ 调试主文件: Helper.cs (已插桩)
  - • 依赖文件: MainWorkflow.cs (未插桩)
  - • 依赖文件: Config.cs (未插桩)
```

## 优势

✅ **明确**: 始终调试当前打开的文件，不会混淆
✅ **灵活**: 可以轻松切换到不同文件进行调试
✅ **高效**: 只对需要调试的文件进行插桩
✅ **智能**: 自动包含依赖文件以支持引用
✅ **可见**: 清晰的日志显示哪个文件被插桩

## 注意事项

1. **确保文件已保存**: 调试前会自动保存当前文件的修改
2. **依赖文件**: 其他文件作为依赖加载，但不会被调试
3. **切换文件**: 切换到不同文件后，需要重新开始调试才能调试新文件
4. **断点位置**: 断点应该设置在当前打开的文件中

---

**更新时间**: 2026-01-12
**版本**: DebuggerServiceV3 改进版
**状态**: ✅ 已实现
