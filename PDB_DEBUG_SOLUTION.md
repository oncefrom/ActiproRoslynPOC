# PDB 调试方案详解

## 什么是 PDB

**PDB (Program Database)** 是 Windows 平台上的调试符号文件，包含：
- **源代码位置信息**: 哪行代码对应哪段 IL 指令
- **变量名和类型**: 局部变量、字段的名称和类型信息
- **方法名**: 原始的方法名称（未混淆）
- **断点映射**: IL 偏移量到源代码行号的映射

---

## PDB vs 当前的代码插桩方案

### 当前方案（代码插桩）

```csharp
// 原始代码
public override void Execute()
{
    int x = 10;       // 第 10 行
    int y = 20;       // 第 11 行
}

// 插桩后
public override void Execute()
{
    __debugCallback?.Invoke(10);  // 插入的回调
    int x = 10;
    __debugCallback?.Invoke(11);  // 插入的回调
    int y = 20;
}
```

**缺点**:
- ❌ 修改了原始代码
- ❌ 需要行号映射（插桩导致行号偏移）
- ❌ 性能开销（每行都调用回调）
- ❌ 可能影响代码逻辑（极端情况）

### PDB 方案（调试符号）

```csharp
// 原始代码（不修改）
public override void Execute()
{
    int x = 10;       // 第 10 行
    int y = 20;       // 第 11 行
}

// 编译为 IL + PDB
IL_0000: ldc.i4.s 10      // 对应源码第 10 行
IL_0002: stloc.0          // 存储到局部变量 0 (x)
IL_0003: ldc.i4.s 20      // 对应源码第 11 行
IL_0005: stloc.1          // 存储到局部变量 1 (y)
IL_0006: ret

// PDB 文件记录映射
IL_0000 -> Line 10
IL_0003 -> Line 11
```

**优点**:
- ✅ 不修改原始代码
- ✅ 准确的行号映射
- ✅ 支持变量查看（通过符号信息）
- ✅ 标准的调试方式

---

## Roslyn 生成 PDB

### 基本编译（不带 PDB）

```csharp
var compilation = CSharpCompilation.Create(
    "MyAssembly",
    syntaxTrees: new[] { syntaxTree },
    references: references,
    options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
);

using var ms = new MemoryStream();
var result = compilation.Emit(ms);  // 只生成 DLL
```

### 编译并生成 PDB

```csharp
var compilation = CSharpCompilation.Create(
    "MyAssembly",
    syntaxTrees: new[] { syntaxTree },
    references: references,
    options: new CSharpCompilationOptions(
        OutputKind.DynamicallyLinkedLibrary,
        optimizationLevel: OptimizationLevel.Debug  // 关键：Debug 模式
    )
);

using var dllStream = new MemoryStream();
using var pdbStream = new MemoryStream();

var result = compilation.Emit(
    peStream: dllStream,
    pdbStream: pdbStream,  // PDB 输出流
    options: new EmitOptions(
        debugInformationFormat: DebugInformationFormat.PortablePdb  // 使用跨平台 PDB 格式
    )
);

if (result.Success)
{
    dllStream.Seek(0, SeekOrigin.Begin);
    pdbStream.Seek(0, SeekOrigin.Begin);

    // 加载程序集（带 PDB）
    var assembly = Assembly.Load(dllStream.ToArray(), pdbStream.ToArray());
}
```

**关键点**:
1. `OptimizationLevel.Debug` - 不优化，保留调试信息
2. `DebugInformationFormat.PortablePdb` - 使用便携式 PDB（跨平台）
3. 同时生成 DLL 和 PDB 流

---

## PDB 格式对比

### Windows PDB (旧格式)

```
DebugInformationFormat.Pdb
```

**特点**:
- ✅ Visual Studio 原生支持
- ❌ 仅 Windows 平台
- ❌ 格式复杂，非开源

### Portable PDB (新格式，推荐)

```
DebugInformationFormat.PortablePdb
```

**特点**:
- ✅ 跨平台（Windows/Linux/macOS）
- ✅ 开源格式
- ✅ .NET Core/5+/6+ 默认格式
- ✅ 可嵌入到程序集中

---

## 使用 PDB 进行调试

### 方案 1: 使用 .NET 调试器 API (CLR Debugging Services)

```csharp
using System.Diagnostics;
using Microsoft.Diagnostics.Runtime;  // NuGet: Microsoft.Diagnostics.Runtime

public class PdbDebugger
{
    private DataTarget _dataTarget;
    private ClrRuntime _runtime;

    public void AttachToProcess(int processId)
    {
        // 附加到进程
        _dataTarget = DataTarget.AttachToProcess(processId, suspend: true);
        _runtime = _dataTarget.ClrVersions[0].CreateRuntime();
    }

    public void SetBreakpoint(string methodName, int lineNumber)
    {
        // 查找方法
        var method = _runtime.GetMethodByName(methodName);

        // 获取 IL 偏移量（通过 PDB）
        var ilOffset = GetILOffsetFromLineNumber(method, lineNumber);

        // 设置断点
        // ... (使用 ICorDebug API)
    }

    private int GetILOffsetFromLineNumber(ClrMethod method, int lineNumber)
    {
        // 通过 PDB 查找行号对应的 IL 偏移量
        // ...
    }
}
```

**缺点**:
- ⚠️ 复杂度高
- ⚠️ 需要附加到进程
- ⚠️ 跨进程调试
- ⚠️ 需要额外的 NuGet 包

### 方案 2: 使用 PDB 读取库 (推荐用于您的场景)

```csharp
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

public class PdbDebugHelper
{
    private MetadataReader _pdbReader;
    private Dictionary<int, int> _lineToILMap = new Dictionary<int, int>();

    public void LoadPdb(Stream pdbStream)
    {
        var readerProvider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
        _pdbReader = readerProvider.GetMetadataReader();

        // 读取 PDB 信息
        BuildLineMapping();
    }

    private void BuildLineMapping()
    {
        foreach (var methodDebugInformationHandle in _pdbReader.MethodDebugInformation)
        {
            var methodDebugInfo = _pdbReader.GetMethodDebugInformation(methodDebugInformationHandle);

            // 获取序列点（源代码行号 -> IL 偏移量）
            var sequencePoints = methodDebugInfo.GetSequencePoints();
            foreach (var sp in sequencePoints)
            {
                if (!sp.IsHidden)
                {
                    int lineNumber = sp.StartLine;
                    int ilOffset = sp.Offset;
                    _lineToILMap[lineNumber] = ilOffset;
                }
            }
        }
    }

    public int GetILOffsetForLine(int lineNumber)
    {
        return _lineToILMap.TryGetValue(lineNumber, out var offset) ? offset : -1;
    }
}
```

**优点**:
- ✅ 只需读取 PDB，不需要附加进程
- ✅ 可以获取行号到 IL 偏移量的映射
- ✅ 使用标准库 (System.Reflection.Metadata)

---

## 完整的 PDB 调试方案

### 架构设计

```
┌─────────────────────────────────────────────┐
│           用户代码（不修改）                │
│   public override void Execute()            │
│   {                                         │
│       int x = 10;    // Line 10             │
│       int y = 20;    // Line 11             │
│   }                                         │
└─────────────────────────────────────────────┘
                    ↓ 编译
┌─────────────────────────────────────────────┐
│          DLL (IL 代码) + PDB                │
│                                             │
│  IL_0000: ldc.i4.s 10  <- Line 10          │
│  IL_0002: stloc.0                          │
│  IL_0003: ldc.i4.s 20  <- Line 11          │
│  IL_0005: stloc.1                          │
│  IL_0006: ret                              │
└─────────────────────────────────────────────┘
                    ↓
┌─────────────────────────────────────────────┐
│            PDB 读取和映射                   │
│                                             │
│  Line 10 -> IL_0000                        │
│  Line 11 -> IL_0003                        │
└─────────────────────────────────────────────┘
                    ↓
┌─────────────────────────────────────────────┐
│         自定义执行引擎 + 断点检查           │
│                                             │
│  foreach IL instruction:                   │
│      if (IsBreakpoint(currentLine))        │
│          Pause()                           │
│      Execute(instruction)                  │
└─────────────────────────────────────────────┘
```

### 实现示例

#### Step 1: 修改编译器生成 PDB

```csharp
// Services/RoslynCompilerService.cs

public CompilationResult CompileWithPdb(string code)
{
    var syntaxTree = CSharpSyntaxTree.ParseText(code, path: "workflow.cs");  // 设置路径

    var compilation = CSharpCompilation.Create(
        "DynamicAssembly_" + Guid.NewGuid(),
        syntaxTrees: new[] { syntaxTree },
        references: GetMetadataReferences(),
        options: new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Debug,  // Debug 模式
            deterministic: true
        )
    );

    using var dllStream = new MemoryStream();
    using var pdbStream = new MemoryStream();

    var emitOptions = new EmitOptions(
        debugInformationFormat: DebugInformationFormat.PortablePdb,
        pdbFilePath: "workflow.pdb"
    );

    var result = compilation.Emit(
        peStream: dllStream,
        pdbStream: pdbStream,
        options: emitOptions
    );

    if (!result.Success)
    {
        return new CompilationResult
        {
            Success = false,
            Diagnostics = GetDiagnostics(result)
        };
    }

    dllStream.Seek(0, SeekOrigin.Begin);
    pdbStream.Seek(0, SeekOrigin.Begin);

    var assembly = Assembly.Load(dllStream.ToArray(), pdbStream.ToArray());

    return new CompilationResult
    {
        Success = true,
        Assembly = assembly,
        PdbData = pdbStream.ToArray()  // 保存 PDB 数据
    };
}
```

#### Step 2: PDB 读取和映射

```csharp
// Services/PdbDebugHelper.cs

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

public class PdbDebugHelper
{
    private MetadataReader _pdbReader;
    private Dictionary<int, SequencePoint> _lineMap = new Dictionary<int, SequencePoint>();

    public void LoadPdb(byte[] pdbData)
    {
        using var stream = new MemoryStream(pdbData);
        var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
        _pdbReader = provider.GetMetadataReader();

        BuildLineMapping();
    }

    private void BuildLineMapping()
    {
        foreach (var methodHandle in _pdbReader.MethodDebugInformation)
        {
            var methodDebugInfo = _pdbReader.GetMethodDebugInformation(methodHandle);
            var sequencePoints = methodDebugInfo.GetSequencePoints();

            foreach (var sp in sequencePoints)
            {
                if (!sp.IsHidden && sp.StartLine != 0xFEEFEE)
                {
                    _lineMap[sp.StartLine] = sp;
                }
            }
        }
    }

    public bool HasLineInfo(int lineNumber)
    {
        return _lineMap.ContainsKey(lineNumber);
    }

    public SequencePoint GetSequencePoint(int lineNumber)
    {
        return _lineMap.TryGetValue(lineNumber, out var sp) ? sp : default;
    }

    public List<int> GetAllExecutableLines()
    {
        return _lineMap.Keys.OrderBy(x => x).ToList();
    }
}
```

#### Step 3: 问题 - 无法直接拦截 IL 执行

**关键限制**:
.NET 没有提供在同一进程内拦截 IL 执行的 API。

**可用方案**:

##### 方案 A: IL 解释器（手动执行）

```csharp
// 自己实现 IL 解释器
public class ILInterpreter
{
    public void Execute(MethodInfo method, PdbDebugHelper pdb)
    {
        // 获取 IL 字节码
        var body = method.GetMethodBody();
        var ilBytes = body.GetILAsByteArray();

        // 解释执行每个 IL 指令
        int pc = 0;  // Program Counter
        while (pc < ilBytes.Length)
        {
            var instruction = ReadInstruction(ilBytes, ref pc);

            // 检查是否到达断点
            if (IsAtBreakpoint(pc, pdb))
            {
                Pause();
            }

            // 执行指令
            ExecuteInstruction(instruction);
        }
    }
}
```

**缺点**:
- ❌ 需要实现完整的 IL 解释器（工作量巨大）
- ❌ 性能差（解释执行比 JIT 慢 100 倍以上）
- ❌ 难以支持所有 IL 指令

##### 方案 B: Profiler API (需要 C++/Native)

使用 CLR Profiler API 注入钩子：

```cpp
// C++ 代码（CLR Profiler）
class MyProfiler : public ICorProfilerCallback
{
    HRESULT JITCompilationStarted(FunctionID functionId, BOOL fIsSafeToBlock)
    {
        // 在 JIT 编译时修改 IL
        // 插入断点检查
    }
};
```

**缺点**:
- ❌ 需要 C++/Native 代码
- ❌ 复杂度极高
- ❌ 需要注入到进程

##### 方案 C: 混合方案（PDB + 轻量级插桩）

```csharp
// 只在 PDB 标记的可执行行插入回调
public class SmartInstrumenter
{
    public string Instrument(string code, PdbDebugHelper pdb)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        var rewriter = new SmartRewriter(pdb);
        var newRoot = rewriter.Visit(root);

        return newRoot.ToFullString();
    }

    private class SmartRewriter : CSharpSyntaxRewriter
    {
        private readonly PdbDebugHelper _pdb;

        public override SyntaxNode VisitBlock(BlockSyntax node)
        {
            var newStatements = new List<StatementSyntax>();

            foreach (var statement in node.Statements)
            {
                var lineNumber = statement.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

                // 只在 PDB 标记为可执行的行插入回调
                if (_pdb.HasLineInfo(lineNumber))
                {
                    var callback = SyntaxFactory.ParseStatement(
                        $"__debugCallback?.Invoke({lineNumber});\r\n"
                    );
                    newStatements.Add(callback);
                }

                newStatements.Add(statement);
            }

            return node.WithStatements(SyntaxFactory.List(newStatements));
        }
    }
}
```

**优点**:
- ✅ 结合 PDB 的准确性
- ✅ 只在有效行插入回调（减少开销）
- ✅ 实现简单

---

## PDB 方案的真正价值

### 在您的场景下

**当前插桩方案已经足够**，因为：
- ✅ 您需要暂停和单步执行
- ✅ .NET 不支持同进程 IL 拦截
- ✅ PDB 无法直接实现断点暂停

**PDB 的实际用途**:
1. **行号验证**: 确保插桩的行号是正确的可执行行
2. **变量信息**: 获取局部变量的名称和类型
3. **优化插桩**: 跳过不可执行的行（空行、注释、大括号）

### 改进建议：使用 PDB 优化插桩

```csharp
public class OptimizedDebuggerService
{
    public async Task<bool> StartDebuggingAsync(
        Dictionary<string, string> codeFiles,
        RoslynCompilerService compiler)
    {
        // 1. 先编译生成 PDB
        var result = compiler.CompileWithPdb(codeFiles);

        // 2. 读取 PDB 获取可执行行列表
        var pdbHelper = new PdbDebugHelper();
        pdbHelper.LoadPdb(result.PdbData);
        var executableLines = pdbHelper.GetAllExecutableLines();

        // 3. 智能插桩：只在可执行行插入回调
        var instrumentedCode = SmartInstrument(codeFiles, executableLines);

        // 4. 重新编译插桩后的代码
        var finalResult = compiler.Compile(instrumentedCode);

        // 5. 执行（同当前方案）
        // ...
    }
}
```

---

## 最终建议

### 保持当前方案，可选增强

**当前方案（代码插桩 + TaskCompletionSource）**:
- ✅ 简单有效
- ✅ 不需要额外依赖
- ✅ 适合您的单进程场景

**可选增强（使用 PDB）**:
1. **验证行号准确性**: 使用 PDB 确保插桩的行是可执行的
2. **获取变量信息**: 使用 PDB 读取变量名和类型
3. **优化性能**: 跳过不可执行的行

### 不建议完全切换到 PDB 方案

原因：
- ❌ .NET 不支持同进程 IL 拦截
- ❌ 需要实现 IL 解释器或使用 Profiler API（工作量大）
- ❌ 性能不如当前方案

---

## 代码示例：PDB 增强版

```csharp
// 增强的编译方法
public CompilationResult CompileWithDebugInfo(string code)
{
    // 1. 编译生成 PDB
    var result = CompileWithPdb(code);
    if (!result.Success) return result;

    // 2. 读取 PDB
    var pdb = new PdbDebugHelper();
    pdb.LoadPdb(result.PdbData);

    // 3. 获取可执行行
    var executableLines = pdb.GetAllExecutableLines();

    // 4. 智能插桩（只在可执行行）
    var instrumented = SmartInstrument(code, executableLines);

    // 5. 重新编译（用于执行）
    return Compile(instrumented);
}
```

---

## 总结

| 特性 | 当前插桩方案 | PDB 方案 | PDB 增强插桩 |
|------|------------|---------|-------------|
| 实现难度 | ⭐ 简单 | ⭐⭐⭐⭐⭐ 复杂 | ⭐⭐ 中等 |
| 准确性 | ⭐⭐⭐ 良好 | ⭐⭐⭐⭐⭐ 完美 | ⭐⭐⭐⭐⭐ 完美 |
| 性能 | ⭐⭐⭐ 良好 | ⭐ 很差 | ⭐⭐⭐⭐ 较好 |
| 变量查看 | ⭐⭐⭐ 反射 | ⭐⭐⭐⭐⭐ PDB | ⭐⭐⭐⭐ PDB+反射 |
| 推荐度 | ⭐⭐⭐⭐ 推荐 | ⭐⭐ 不推荐 | ⭐⭐⭐⭐⭐ 强烈推荐 |

**结论**:
建议使用 **PDB 增强插桩方案** - 结合了两者的优点，既有 PDB 的准确性，又保留了插桩的简单性。

---

**最后更新**: 2026-01-13
**适用于**: ActiproRoslynPOC 项目优化建议
