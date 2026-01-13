# PDB 方案：变量提取完整实现

## 挑战：为什么变量提取很难

### 问题 1: 变量名优化

```csharp
// 源代码
public void Calculate()
{
    int width = 10;
    int height = 20;
    int area = width * height;
}

// 编译后的 IL（Release 模式）
.method public void Calculate()
{
    .locals init (int32 V_0, int32 V_1, int32 V_2)
    IL_0000: ldc.i4.s 10
    IL_0002: stloc.0      // V_0 (变量名消失！)
    IL_0003: ldc.i4.s 20
    IL_0005: stloc.1      // V_1
    IL_0006: ldloc.0
    IL_0007: ldloc.1
    IL_0008: mul
    IL_0009: stloc.2      // V_2
    IL_000A: ret
}
```

**问题**: V_0、V_1、V_2 是什么变量？编译器不保留名称！

**解决方案**: PDB 的 **LocalScope** 和 **LocalVariable** 信息

### 问题 2: 访问变量值需要停在断点上

只有线程暂停时，才能安全读取栈帧（Stack Frame）的变量表。

---

## 完整方案架构

### 架构图

```
┌─────────────────────────────────────────────────────┐
│                 IDE 进程 (Debugger)                 │
│  ┌───────────────────────────────────────────────┐ │
│  │  ActiproRoslynPOC.exe                         │ │
│  │  - 编译代码 (生成 DLL + PDB)                  │ │
│  │  - 启动 WorkflowRunner.exe 进程               │ │
│  │  - 附加调试器 (ICorDebug)                     │ │
│  │  - 设置断点 (通过 PDB 映射 IL 偏移量)         │ │
│  │  - 监听事件 (Breakpoint, Exception)           │ │
│  └───────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────┘
                       ↓ 进程间通信
┌─────────────────────────────────────────────────────┐
│              WorkflowRunner.exe (Debuggee)          │
│  ┌───────────────────────────────────────────────┐ │
│  │  - 加载 DLL + PDB                             │ │
│  │  - 执行 workflow.Execute()                    │ │
│  │  - 等待调试器控制                             │ │
│  └───────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────┘
                       ↑
                  断点触发时
                       ↓
┌─────────────────────────────────────────────────────┐
│             变量提取流程 (在 IDE 进程中)            │
│  1. 获取当前线程 (ICorDebugThread)                 │
│  2. 获取当前栈帧 (ICorDebugFrame)                  │
│  3. 从 PDB 读取 LocalScope 和 LocalVariable        │
│  4. 遍历变量槽位 (Slot Index)                      │
│  5. 调用 ICorDebugILFrame::GetLocalVariable()      │
│  6. 显示到 IDE 的变量窗口                          │
└─────────────────────────────────────────────────────┘
```

---

## 实现步骤

### 步骤 1: 创建 WorkflowRunner.exe

```csharp
// WorkflowRunner.exe - 独立的执行进程

class Program
{
    static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: WorkflowRunner.exe <assembly-path>");
            return;
        }

        string assemblyPath = args[0];
        string pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");

        // 加载 DLL 和 PDB
        var dllBytes = File.ReadAllBytes(assemblyPath);
        var pdbBytes = File.ReadAllBytes(pdbPath);
        var assembly = Assembly.Load(dllBytes, pdbBytes);

        // 查找工作流类
        var workflowType = assembly.GetTypes()
            .FirstOrDefault(t => t.IsSubclassOf(typeof(CodedWorkflowBase)));

        if (workflowType == null)
        {
            Console.WriteLine("Error: No workflow class found");
            return;
        }

        // 创建实例并执行
        var workflow = Activator.CreateInstance(workflowType) as CodedWorkflowBase;

        Console.WriteLine("Workflow started. Debugger can attach now.");
        Console.WriteLine($"Process ID: {Process.GetCurrentProcess().Id}");

        // 等待调试器附加（可选）
        while (!Debugger.IsAttached)
        {
            Thread.Sleep(100);
        }

        workflow.Execute();

        Console.WriteLine("Workflow completed.");
    }
}
```

### 步骤 2: IDE 启动并附加调试器

```csharp
// ActiproRoslynPOC - IDE 进程

using Microsoft.Samples.Debugging.MdbgEngine;
using System.Diagnostics;

public class PdbDebuggerController
{
    private MDbgEngine _debugEngine;
    private MDbgProcess _debugProcess;
    private Dictionary<int, int> _breakpoints = new Dictionary<int, int>(); // line -> IL offset

    public async Task<bool> StartDebuggingAsync(
        string dllPath,
        string pdbPath,
        List<int> breakpointLines)
    {
        // 1. 临时保存 DLL 和 PDB 到磁盘
        File.WriteAllBytes(dllPath, compiledDllBytes);
        File.WriteAllBytes(pdbPath, compiledPdbBytes);

        // 2. 启动 WorkflowRunner.exe
        var runnerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WorkflowRunner.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = runnerPath,
            Arguments = $"\"{dllPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo);

        // 3. 初始化调试引擎
        _debugEngine = new MDbgEngine();
        _debugProcess = _debugEngine.Attach(process.Id);

        // 4. 设置断点
        await SetBreakpointsAsync(pdbPath, breakpointLines);

        // 5. 监听调试事件
        _debugProcess.PostDebugEvent += OnDebugEvent;

        // 6. 启动执行
        _debugProcess.Go();

        return true;
    }

    private async Task SetBreakpointsAsync(string pdbPath, List<int> lineNumbers)
    {
        // 读取 PDB 获取行号到 IL 偏移量的映射
        var pdbHelper = new PdbReader(pdbPath);

        foreach (var line in lineNumbers)
        {
            int ilOffset = pdbHelper.GetILOffsetForLine(line);
            if (ilOffset >= 0)
            {
                // 设置断点
                var breakpoint = _debugProcess.Breakpoints.CreateBreakpoint(
                    "SourceCode.cs",  // 文件名
                    line              // 行号
                );
                breakpoint.Bind();
                _breakpoints[line] = ilOffset;
            }
        }
    }

    private void OnDebugEvent(object sender, DebugEventArgs e)
    {
        if (e.EventType == ManagedCallbackType.OnBreakpoint)
        {
            // 断点命中
            var thread = _debugProcess.ActiveThread;
            var frame = thread.CurrentFrame;

            // 提取变量
            var variables = ExtractVariables(frame);

            // 通知 UI
            VariablesExtracted?.Invoke(variables);

            // 暂停，等待用户操作 (StepOver/Continue)
        }
    }
}
```

### 步骤 3: 从 PDB 读取变量信息

```csharp
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

public class PdbReader
{
    private MetadataReader _reader;
    private Dictionary<string, List<LocalVariableInfo>> _methodVariables;

    public PdbReader(string pdbPath)
    {
        var pdbBytes = File.ReadAllBytes(pdbPath);
        var provider = MetadataReaderProvider.FromPortablePdbStream(
            new MemoryStream(pdbBytes)
        );
        _reader = provider.GetMetadataReader();

        BuildVariableMap();
    }

    private void BuildVariableMap()
    {
        _methodVariables = new Dictionary<string, List<LocalVariableInfo>>();

        foreach (var methodHandle in _reader.MethodDebugInformation)
        {
            var methodDebugInfo = _reader.GetMethodDebugInformation(methodHandle);

            if (methodDebugInfo.LocalSignature.IsNil)
                continue;

            // 获取方法的局部变量签名
            var localSignature = _reader.GetStandaloneSignature(methodDebugInfo.LocalSignature);
            var localVariables = DecodeLocalVariables(localSignature);

            // 获取方法名称
            var methodName = GetMethodName(methodHandle);
            _methodVariables[methodName] = localVariables;
        }

        // 读取局部变量作用域
        foreach (var scopeHandle in _reader.LocalScopes)
        {
            var scope = _reader.GetLocalScope(scopeHandle);

            foreach (var varHandle in scope.GetLocalVariables())
            {
                var variable = _reader.GetLocalVariable(varHandle);

                // 重要：Slot Index（槽位索引）
                int slotIndex = variable.Index;

                // 变量名
                string name = _reader.GetString(variable.Name);

                // 将名称映射到槽位
                MapVariableNameToSlot(scope.Method, slotIndex, name);
            }
        }
    }

    public List<LocalVariableInfo> GetVariablesForMethod(string methodName)
    {
        return _methodVariables.TryGetValue(methodName, out var vars)
            ? vars
            : new List<LocalVariableInfo>();
    }
}

public class LocalVariableInfo
{
    public int SlotIndex { get; set; }      // V_0, V_1, V_2...
    public string Name { get; set; }         // "width", "height", "area"
    public Type Type { get; set; }           // int, string, etc.
    public int StartOffset { get; set; }     // 作用域开始 IL 偏移量
    public int EndOffset { get; set; }       // 作用域结束 IL 偏移量
}
```

### 步骤 4: 从栈帧提取变量值

```csharp
using Microsoft.Samples.Debugging.MdbgEngine;

public Dictionary<string, object> ExtractVariables(MDbgFrame frame)
{
    var variables = new Dictionary<string, object>();

    // 1. 获取当前方法名
    var methodName = frame.Function.FullName;

    // 2. 从 PDB 获取该方法的变量定义
    var variableInfos = _pdbReader.GetVariablesForMethod(methodName);

    // 3. 获取当前 IL 偏移量（判断变量是否在作用域内）
    int currentILOffset = frame.IP;

    // 4. 遍历所有变量
    foreach (var varInfo in variableInfos)
    {
        // 检查变量是否在当前作用域内
        if (currentILOffset >= varInfo.StartOffset &&
            currentILOffset < varInfo.EndOffset)
        {
            try
            {
                // 5. 通过槽位索引获取变量值
                var value = frame.GetArgument(varInfo.SlotIndex);

                // 6. 转换为 C# 对象
                object convertedValue = ConvertMDbgValue(value);

                variables[varInfo.Name] = convertedValue;
            }
            catch (Exception ex)
            {
                variables[varInfo.Name] = $"<error: {ex.Message}>";
            }
        }
    }

    return variables;
}

private object ConvertMDbgValue(MDbgValue value)
{
    if (value == null) return null;

    // 基础类型
    if (value.IsComplexType == false)
    {
        switch (value.TypeName)
        {
            case "System.Int32":
                return int.Parse(value.GetStringValue(0));
            case "System.String":
                return value.GetStringValue(0);
            case "System.Boolean":
                return bool.Parse(value.GetStringValue(0));
            // ... 其他类型
            default:
                return value.GetStringValue(0);
        }
    }

    // 复杂类型（对象、数组）
    var fields = new Dictionary<string, object>();
    foreach (MDbgValue field in value.GetFields())
    {
        fields[field.Name] = ConvertMDbgValue(field);
    }
    return fields;
}
```

---

## 完整示例：单步调试时的变量提取

### 用户代码

```csharp
public class MyWorkflow : CodedWorkflowBase
{
    public override void Execute()
    {
        int x = 10;              // 第 8 行 - 断点
        int y = 20;              // 第 9 行
        int sum = x + y;         // 第 10 行
        Console.WriteLine(sum);  // 第 11 行
    }
}
```

### PDB 中记录的信息

```
LocalScope:
  StartOffset: IL_0000
  EndOffset:   IL_000F
  Variables:
    - Slot: 0, Name: "x", Type: System.Int32, Start: IL_0002, End: IL_000F
    - Slot: 1, Name: "y", Type: System.Int32, Start: IL_0005, End: IL_000F
    - Slot: 2, Name: "sum", Type: System.Int32, Start: IL_0009, End: IL_000F

SequencePoints (行号映射):
  IL_0000 -> Line 8
  IL_0002 -> Line 9
  IL_0005 -> Line 10
  IL_0009 -> Line 11
```

### 断点在第 10 行触发时

```csharp
// IDE 接收到断点事件
OnDebugEvent(e)
{
    var frame = _debugProcess.ActiveThread.CurrentFrame;
    var currentIP = frame.IP;  // IL_0005

    // 从 PDB 读取变量信息
    var variables = _pdbReader.GetVariablesForMethod("MyWorkflow.Execute");

    // 提取在作用域内的变量
    foreach (var varInfo in variables)
    {
        if (currentIP >= varInfo.StartOffset)
        {
            var value = frame.GetArgument(varInfo.SlotIndex);

            // 输出到 IDE 的变量窗口
            Console.WriteLine($"{varInfo.Name} = {value}");
        }
    }
}

// 输出结果：
// x = 10     (Slot 0, 已赋值)
// y = 20     (Slot 1, 已赋值)
// sum = ?    (Slot 2, 尚未赋值，因为还没执行到 IL_0009)
```

---

## 关键技术点总结

### 1. 变量名恢复

```csharp
// LocalVariable 表
Slot Index | Variable Name | Type        | Scope
-----------|---------------|-------------|------------
0          | x             | int         | IL_0002-IL_000F
1          | y             | int         | IL_0005-IL_000F
2          | sum           | int         | IL_0009-IL_000F
```

**关键**: PDB 的 `LocalVariable` 记录了 **Slot Index → Variable Name** 的映射。

### 2. 作用域判断

```csharp
if (currentILOffset >= varInfo.StartOffset &&
    currentILOffset < varInfo.EndOffset)
{
    // 变量在当前作用域内，可以访问
}
```

**原因**: 局部变量可能只在代码块内有效，例如：

```csharp
{
    int temp = 5;  // temp 只在这个块内有效
}
// 这里无法访问 temp
```

### 3. 值提取

```csharp
// ICorDebugILFrame 提供的方法
var value = frame.GetLocalVariable(slotIndex);
```

**底层原理**:
- 线程暂停时，栈帧（Stack Frame）在内存中保持静止
- 调试器可以读取栈帧的局部变量表（Local Variable Table）
- Slot Index 就是局部变量表的索引

---

## 实际挑战和解决方案

### 挑战 1: 优化器可能移除变量

**问题**: Release 模式下，编译器可能优化掉未使用的变量。

**解决方案**:
- 编译时使用 `OptimizationLevel.Debug`
- 使用 `[MethodImpl(MethodImplOptions.NoOptimization)]` 禁用优化

### 挑战 2: 异步方法的状态机

**问题**: `async/await` 会被编译器转换为状态机，局部变量被提升为字段。

```csharp
// 原始代码
async Task DoWork()
{
    int x = 10;
    await Task.Delay(100);
    Console.WriteLine(x);
}

// 编译后的状态机
class <DoWork>d__0
{
    public int x;  // 变量被提升为字段
    // ...
}
```

**解决方案**:
- 从状态机对象的字段中提取变量
- 使用 `ICorDebugValue::GetFieldValue()` 而不是 `GetLocalVariable()`

### 挑战 3: 泛型和闭包

**问题**: 泛型和闭包会生成额外的类型，变量可能在不同的对象中。

**解决方案**:
- 递归遍历 `this` 对象的字段
- 检查编译器生成的类（名称包含 `<>c__DisplayClass`）

---

## 推荐的 NuGet 包

### Microsoft.Diagnostics.Runtime (ClrMD)

```bash
Install-Package Microsoft.Diagnostics.Runtime
```

**优点**:
- 高层 API，比直接用 ICorDebug 简单
- 支持附加到进程或 Dump 文件

**示例**:
```csharp
using Microsoft.Diagnostics.Runtime;

var dataTarget = DataTarget.AttachToProcess(processId, suspend: true);
var runtime = dataTarget.ClrVersions[0].CreateRuntime();

foreach (var thread in runtime.Threads)
{
    foreach (var frame in thread.StackTrace)
    {
        Console.WriteLine($"Method: {frame.Method.Name}");

        // 获取局部变量
        foreach (var local in frame.Method.LocalVariables)
        {
            Console.WriteLine($"  {local.Name} = {local.GetValue(frame)}");
        }
    }
}
```

### System.Reflection.Metadata

```bash
Install-Package System.Reflection.Metadata
```

用于读取 PDB 文件。

---

## 最终建议

### 对于您的 RPA 产品

**短期方案（推荐）**:
- 保持当前的插桩方案
- 使用 PDB 增强：只在 PDB 标记的可执行行插桩
- 变量提取继续使用反射（`GetFields`/`GetProperties`）

**长期方案（高端路线）**:
- 实现进程分离
- 使用 ClrMD 实现真正的调试器
- 提供 Visual Studio 级别的调试体验

### 工作量评估

| 任务 | 难度 | 时间 |
|------|------|------|
| 读取 PDB 信息 | ⭐⭐ | 1-2 天 |
| 进程分离架构 | ⭐⭐⭐ | 3-5 天 |
| ICorDebug/ClrMD 集成 | ⭐⭐⭐⭐ | 1-2 周 |
| 变量提取（含异步/泛型）| ⭐⭐⭐⭐⭐ | 2-3 周 |
| 完整测试和优化 | ⭐⭐⭐ | 1 周 |

**总计**: 约 1-2 个月的开发时间

---

## 结论

PDB 方案是专业的调试解决方案，但实现复杂度很高。

**我的建议**:
1. 先实现 **PDB 增强插桩**（投入产出比最高）
2. 如果产品需要更专业的调试体验，再考虑完整的 PDB 方案
3. 变量提取可以先用简单的反射，等调试器成熟后再切换到栈帧读取

需要我提供 PDB 增强插桩的具体实现代码吗？

---

**最后更新**: 2026-01-13
**适用于**: 企业级 RPA 产品的调试解决方案
