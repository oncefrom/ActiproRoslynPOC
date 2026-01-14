# PDB 调试器实现说明

## 已实现的组件

### 1. PdbReaderService.cs
**功能**: 从 Portable PDB 文件中读取调试信息
- ✅ 解析序列点 (源代码行号 → IL 偏移量映射)
- ✅ 解析局部变量信息 (变量名、作用域、槽位索引)
- ✅ 提供行号到 IL 偏移量的查询接口

**使用方法**:
```csharp
var pdbReader = new PdbReaderService();
pdbReader.LoadFromFile("Workflow.pdb");

// 获取所有可执行行
var executableLines = pdbReader.GetAllExecutableLines();

// 获取行号对应的 IL 偏移量
int ilOffset = pdbReader.GetILOffsetForLine(10);

// 获取方法的局部变量信息
var methodInfo = pdbReader.GetMethodDebugInfo("Execute");
foreach (var variable in methodInfo.LocalVariables)
{
    Console.WriteLine($"{variable.Name} (Slot {variable.SlotIndex})");
}
```

---

### 2. WorkflowRunner.exe
**功能**: 独立的工作流执行进程
- ✅ 加载编译后的 DLL 和 PDB
- ✅ 查找并执行工作流类
- ✅ 支持等待调试器附加 (`--wait-for-debugger` 参数)
- ✅ 显示详细的执行信息和异常堆栈

**使用方法**:
```bash
# 直接执行
WorkflowRunner.exe C:\Temp\Workflow.dll

# 等待调试器附加
WorkflowRunner.exe C:\Temp\Workflow.dll --wait-for-debugger
```

---

### 3. PdbDebuggerController.cs
**功能**: 调试控制器 (使用 ClrMD)
- ✅ 启动 WorkflowRunner 进程
- ✅ 附加到目标进程
- ✅ 保存 DLL 和 PDB 到临时目录
- ⚠️ **局限**: ClrMD 主要用于快照分析,不是实时调试器

**ClrMD 的限制**:
1. ❌ **无法设置断点**: ClrMD 只能读取进程状态,无法注入断点
2. ❌ **无法单步执行**: 无法控制线程执行
3. ❌ **无法暂停进程**: 只能附加并读取快照
4. ✅ **可以读取堆对象**: 可以分析内存中的对象
5. ✅ **可以分析崩溃转储**: 适合事后分析

---

## 完整的 PDB 调试器需要什么?

要实现 Visual Studio 级别的调试体验,需要使用 **ICorDebug API** (CLR 调试服务)。

### ICorDebug vs ClrMD

| 特性 | ClrMD | ICorDebug |
|------|-------|----------|
| 读取堆内存 | ✅ | ✅ |
| 读取线程栈 | ✅ | ✅ |
| 设置断点 | ❌ | ✅ |
| 单步执行 | ❌ | ✅ |
| 暂停/继续 | ❌ | ✅ |
| 变量求值 | ❌ | ✅ |
| 调用函数 | ❌ | ✅ |
| 修改变量 | ❌ | ✅ |
| 实现难度 | ⭐⭐ 简单 | ⭐⭐⭐⭐⭐ 非常复杂 |

---

## 推荐的实现路径

### 方案 A: 使用现有的插桩方案 (推荐)

**优点**:
- ✅ 已经实现并可用 (DebuggerServiceV3.cs)
- ✅ 支持断点、单步、变量查看
- ✅ 不需要进程分离
- ✅ 实现简单

**增强建议**: 使用 PDB 优化插桩
```csharp
// 1. 编译生成 PDB
var result = compiler.CompileWithPdb(code);

// 2. 读取 PDB 获取可执行行
var pdbReader = new PdbReaderService();
pdbReader.LoadFromBytes(result.PdbData);
var executableLines = pdbReader.GetAllExecutableLines();

// 3. 只在可执行行插桩
var instrumentedCode = InstrumentOnlyExecutableLines(code, executableLines);

// 4. 重新编译并执行
var finalResult = compiler.Compile(instrumentedCode);
```

**好处**:
- 减少不必要的回调 (跳过空行、注释、大括号)
- 更准确的行号映射
- 更好的性能

---

### 方案 B: 实现完整的 ICorDebug 调试器 (高级)

**需要的步骤**:

1. **引入 ICorDebug API**
   ```xml
   <!-- 需要添加 COM 互操作引用 -->
   <ItemGroup>
     <COMReference Include="ICorDebug">
       <Guid>{3D6F5F61-7538-11D3-8D5B-00104B35E7EF}</Guid>
       <VersionMajor>1</VersionMajor>
       <VersionMinor>0</VersionMinor>
       <Lcid>0</Lcid>
       <WrapperTool>tlbimp</WrapperTool>
       <Isolated>False</Isolated>
       <EmbedInteropTypes>True</EmbedInteropTypes>
     </COMReference>
   </ItemGroup>
   ```

2. **创建调试管理器**
   ```csharp
   ICorDebug debugger;
   ICorDebugProcess process;

   // 启动并附加
   debugger.CreateProcess(...);

   // 设置断点
   process.GetFunctionFromToken(...).CreateBreakpoint(...);

   // 监听事件
   debugger.SetManagedHandler(new MyDebugEventHandler());
   ```

3. **处理调试事件**
   ```csharp
   class MyDebugEventHandler : ICorDebugManagedCallback
   {
       public void Breakpoint(ICorDebugAppDomain pAppDomain,
                             ICorDebugThread pThread,
                             ICorDebugBreakpoint pBreakpoint)
       {
           // 断点命中
           var frame = pThread.GetActiveFrame();
           var locals = frame.EnumerateLocalVariables();
           // ...
       }
   }
   ```

**工作量估算**: 2-3 周全职开发

**推荐的 NuGet 包**:
- `Microsoft.Diagnostics.Runtime` (ClrMD) - 已包含
- 手动引入 `mscordbi.dll` 的 COM 互操作

---

## 实际建议

### 短期 (1-2 天)
✅ **使用 PDB 增强现有的插桩方案**
- 修改 DebuggerServiceV3.cs
- 集成 PdbReaderService
- 只在 PDB 标记的可执行行插桩

### 中期 (1-2 周)
如果需要更专业的调试体验:
- 学习 ICorDebug API
- 实现基本的断点和单步功能
- 集成到现有架构

### 长期 (1-2 月)
企业级调试器:
- 完整的 ICorDebug 集成
- 支持条件断点、数据断点
- Watch 窗口、即时窗口
- 异常断点、编辑并继续

---

## 示例: PDB 增强的插桩代码

```csharp
// Services/DebuggerServiceV3Enhanced.cs

public async Task<bool> StartDebuggingAsync(
    Dictionary<string, string> codeFiles,
    RoslynCompilerService compiler,
    string mainFilePath)
{
    // 1. 先编译生成 PDB
    var tempResult = compiler.CompileMultiple(codeFiles);
    if (!tempResult.Success) return false;

    // 2. 读取 PDB 获取可执行行
    var pdbReader = new PdbReaderService();
    var pdbData = ExtractPdbFromAssembly(tempResult.Assembly);
    pdbReader.LoadFromBytes(pdbData);
    var executableLines = pdbReader.GetAllExecutableLines();

    // 3. 智能插桩 - 只在可执行行插入回调
    var instrumentedFiles = new Dictionary<string, string>();
    foreach (var kvp in codeFiles)
    {
        if (IsMainFile(kvp.Key, mainFilePath))
        {
            var instrumented = InstrumentOnlyExecutableLines(
                kvp.Value,
                executableLines
            );
            instrumentedFiles[kvp.Key] = instrumented;
        }
        else
        {
            instrumentedFiles[kvp.Key] = kvp.Value;
        }
    }

    // 4. 重新编译插桩后的代码
    var finalResult = compiler.CompileMultiple(instrumentedFiles);
    if (!finalResult.Success) return false;

    // 5. 执行 (同当前逻辑)
    _debugAssembly = finalResult.Assembly;
    _ = Task.Run(() => ExecuteWorkflowAsync());

    return true;
}

private string InstrumentOnlyExecutableLines(
    string code,
    List<int> executableLines)
{
    var tree = CSharpSyntaxTree.ParseText(code);
    var root = tree.GetRoot();

    var rewriter = new SmartInstrumentationRewriter(executableLines);
    var newRoot = rewriter.Visit(root);

    return newRoot.ToFullString();
}

private class SmartInstrumentationRewriter : CSharpSyntaxRewriter
{
    private HashSet<int> _executableLines;

    public SmartInstrumentationRewriter(List<int> executableLines)
    {
        _executableLines = new HashSet<int>(executableLines);
    }

    public override SyntaxNode VisitBlock(BlockSyntax node)
    {
        var newStatements = new List<StatementSyntax>();

        foreach (var statement in node.Statements)
        {
            var lineNumber = statement.GetLocation()
                .GetLineSpan()
                .StartLinePosition.Line + 1;

            // 只在 PDB 标记的可执行行插入回调
            if (_executableLines.Contains(lineNumber))
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
```

---

## 总结

✅ **已实现**: PDB 读取服务、WorkflowRunner、ClrMD 控制器框架

⚠️ **局限**: ClrMD 无法实现实时断点和单步调试

🎯 **推荐方案**:
1. **短期**: PDB 增强插桩 (性价比最高)
2. **长期**: 如需专业级调试,投入 ICorDebug 开发

📝 **下一步**:
- 选择方案 A (增强插桩) 或方案 B (ICorDebug)
- 我可以立即实现方案 A 的代码
- 方案 B 需要更多时间和 COM 互操作知识

需要我实现哪个方案?
