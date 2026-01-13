# 技术细节深度解析

## 目录
1. [架构概览](#架构概览)
2. [Roslyn 编译器服务](#roslyn-编译器服务)
3. [工作流基类设计](#工作流基类设计)
4. [代码执行流程](#代码执行流程)
5. [多文件编译机制](#多文件编译机制)
6. [反射和动态加载](#反射和动态加载)
7. [UI 线程同步](#ui-线程同步)
8. [控制台输出重定向](#控制台输出重定向)

---

## 架构概览

### 整体架构图

```
┌─────────────────────────────────────────────────────────┐
│                      MainWindow.xaml                     │
│  ┌─────────────┐  ┌──────────────┐  ┌───────────────┐  │
│  │  编辑器区域  │  │   输出窗口   │  │   变量窗口    │  │
│  │  (Actipro)  │  │  (TextBox)   │  │  (TextBox)    │  │
│  └─────────────┘  └──────────────┘  └───────────────┘  │
└─────────────────────────────────────────────────────────┘
                           ↓↑ 绑定
┌─────────────────────────────────────────────────────────┐
│                    MainViewModel.cs                      │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │ Code (string)│  │Output (string)│  │Variables     │  │
│  └──────────────┘  └──────────────┘  └──────────────┘  │
│         ↓                  ↑                 ↑           │
│  ┌──────────────────────────────────────────────────┐  │
│  │              命令处理层                           │  │
│  │  ExecuteRun() / ExecuteStartDebugAsync()         │  │
│  └──────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────┐
│                       服务层                             │
│  ┌──────────────────┐  ┌──────────────────────────┐    │
│  │RoslynCompiler    │  │DebuggerServiceV3         │    │
│  │Service           │  │ (代码插桩 + 实时暂停)     │    │
│  └──────────────────┘  └──────────────────────────┘    │
│  ┌──────────────────┐  ┌──────────────────────────┐    │
│  │CodeExecution     │  │ConsoleRedirectWriter     │    │
│  │Service           │  │ (控制台输出重定向)        │    │
│  └──────────────────┘  └──────────────────────────┘    │
└─────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────┐
│                    Roslyn 编译器                         │
│  Microsoft.CodeAnalysis.CSharp                          │
└─────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────┐
│                   动态程序集                             │
│  CodedWorkflowBase 子类实例                             │
└─────────────────────────────────────────────────────────┘
```

---

## Roslyn 编译器服务

### RoslynCompilerService 核心职责

**文件位置**: `Services/RoslynCompilerService.cs`

#### 1. 单文件编译

```csharp
public CompilationResult Compile(string code)
{
    // 1. 解析代码生成语法树
    var syntaxTree = CSharpSyntaxTree.ParseText(code);

    // 2. 创建编译单元
    var compilation = CSharpCompilation.Create(
        assemblyName: "DynamicAssembly_" + Guid.NewGuid(),
        syntaxTrees: new[] { syntaxTree },
        references: GetMetadataReferences(),  // 添加引用
        options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
    );

    // 3. 编译到内存流
    using var ms = new MemoryStream();
    var emitResult = compilation.Emit(ms);

    // 4. 检查编译结果
    if (!emitResult.Success)
    {
        return new CompilationResult
        {
            Success = false,
            Diagnostics = GetDiagnostics(emitResult)
        };
    }

    // 5. 从内存加载程序集
    ms.Seek(0, SeekOrigin.Begin);
    var assembly = Assembly.Load(ms.ToArray());

    return new CompilationResult
    {
        Success = true,
        Assembly = assembly
    };
}
```

**关键技术点**:
- **语法树 (SyntaxTree)**: Roslyn 的代码表示，可以遍历和修改
- **编译单元 (Compilation)**: 包含所有编译信息和引用
- **内存编译 (Emit to MemoryStream)**: 不生成 DLL 文件，直接加载到内存
- **程序集加载 (Assembly.Load)**: 动态加载编译后的程序集

#### 2. 多文件编译

```csharp
public CompilationResult CompileMultiple(Dictionary<string, string> codeFiles)
{
    // 1. 为每个文件生成语法树
    var syntaxTrees = codeFiles.Select(kvp =>
    {
        var tree = CSharpSyntaxTree.ParseText(kvp.Value);
        return tree.WithFilePath(kvp.Key);  // 设置文件路径（用于错误报告）
    }).ToArray();

    // 2. 创建编译单元（包含所有语法树）
    var compilation = CSharpCompilation.Create(
        assemblyName: "MultiFileAssembly_" + Guid.NewGuid(),
        syntaxTrees: syntaxTrees,  // 多个语法树
        references: GetMetadataReferences(),
        options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
    );

    // 3. 编译（同单文件）
    // ...
}
```

**关键技术点**:
- **多个语法树**: 每个文件一个语法树
- **文件路径**: 用于诊断信息中显示错误所在文件
- **统一编译**: 所有文件编译到同一个程序集

#### 3. 元数据引用 (MetadataReferences)

```csharp
private IEnumerable<MetadataReference> GetMetadataReferences()
{
    var references = new List<MetadataReference>();

    // 1. 基础运行时引用
    references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));           // System.Private.CoreLib
    references.Add(MetadataReference.CreateFromFile(typeof(Console).Assembly.Location));          // System.Console
    references.Add(MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location));       // System.Linq

    // 2. 当前应用程序引用（包含 CodedWorkflowBase）
    references.Add(MetadataReference.CreateFromFile(Assembly.GetExecutingAssembly().Location));

    // 3. 其他必要的程序集
    var runtimeAssemblies = Directory.GetFiles(
        Path.GetDirectoryName(typeof(object).Assembly.Location),
        "System.*.dll"
    );

    foreach (var dll in runtimeAssemblies)
    {
        references.Add(MetadataReference.CreateFromFile(dll));
    }

    return references;
}
```

**为什么需要引用**:
- C# 代码需要知道基础类型的定义（如 `object`, `string`, `Console`）
- 需要引用 `CodedWorkflowBase` 所在的程序集
- 需要 LINQ、集合等常用命名空间

---

## 工作流基类设计

### CodedWorkflowBase

**文件位置**: `Models/CodedWorkflowBase.cs`

```csharp
public abstract class CodedWorkflowBase
{
    // 1. 日志事件（输出到界面）
    public event EventHandler<string> LogEvent;

    // 2. 执行结果
    public object Result { get; protected set; }

    // 3. 抽象执行方法（子类必须实现）
    public abstract void Execute();

    // 4. 辅助方法：输出日志
    protected void Log(string message)
    {
        LogEvent?.Invoke(this, message);
        Console.WriteLine(message);  // 同时输出到控制台
    }
}
```

**设计要点**:

1. **抽象基类**: 强制所有工作流实现 `Execute()` 方法
2. **事件机制**: 通过 `LogEvent` 将日志传递到 UI
3. **Result 属性**: 存储执行结果
4. **Log 辅助方法**: 简化子类的日志输出

**用户代码示例**:

```csharp
public class MainWorkflow : CodedWorkflowBase
{
    public override void Execute()
    {
        Log("工作流开始");

        int result = Calculate(10, 20);
        Result = result;

        Log($"计算结果: {result}");
    }

    private int Calculate(int a, int b)
    {
        return a + b;
    }
}
```

---

## 代码执行流程

### 运行模式执行流程

#### 步骤 1: 编译

```csharp
// MainViewModel.cs: ExecuteSingleFile()
var result = _compiler.Compile(Code);

if (!result.Success)
{
    // 显示编译错误
    foreach (var diag in result.Diagnostics)
        Diagnostics.Add(diag);
    return;
}
```

**发生了什么**:
1. 用户代码被解析为语法树
2. 添加必要的程序集引用
3. 编译为 IL 代码（中间语言）
4. 加载到内存中的程序集

#### 步骤 2: 查找工作流类型

```csharp
// 从编译后的程序集中查找 CodedWorkflowBase 的子类
var type = result.Assembly.GetTypes()
    .FirstOrDefault(t => t.IsSubclassOf(typeof(CodedWorkflowBase)) && !t.IsAbstract);

if (type == null)
{
    AppendOutput("[错误] 找不到 CodedWorkflowBase 的子类");
    return;
}
```

**技术细节**:
- **反射 (Reflection)**: 遍历程序集中的所有类型
- **类型检查**: `IsSubclassOf()` 判断继承关系
- **抽象类过滤**: 排除抽象类

#### 步骤 3: 创建实例

```csharp
// 方式 1: 使用辅助方法
var workflow = _compiler.CreateInstance<CodedWorkflowBase>(result.Assembly, type.Name);

// 方式 2: 直接使用 Activator
var workflow = Activator.CreateInstance(type) as CodedWorkflowBase;
```

**底层原理**:
```csharp
public T CreateInstance<T>(Assembly assembly, string typeName)
{
    // 1. 查找类型
    var type = assembly.GetType(typeName);

    // 2. 调用无参构造函数创建实例
    var instance = Activator.CreateInstance(type);

    // 3. 类型转换
    return (T)instance;
}
```

**Activator.CreateInstance 工作原理**:
1. 使用反射查找类型的构造函数
2. 调用构造函数创建对象
3. 返回 `object` 类型（需要转换）

#### 步骤 4: 订阅事件

```csharp
workflow.LogEvent += (s, msg) => AppendOutput(msg);
```

**事件流**:
```
用户代码调用 Log("消息")
        ↓
LogEvent 事件触发
        ↓
MainViewModel 的 Lambda 接收消息
        ↓
AppendOutput(msg)
        ↓
Output 属性更新
        ↓
UI 显示输出
```

#### 步骤 5: 执行

```csharp
workflow.Execute();
```

**执行栈**:
```
MainViewModel.ExecuteSingleFile()
    → workflow.Execute()
        → 用户的 MainWorkflow.Execute()
            → 用户代码逻辑
                → Log("消息") 触发事件
```

#### 步骤 6: 获取结果

```csharp
if (workflow.Result != null)
    AppendOutput($"返回结果: {workflow.Result}");
```

---

## 多文件编译机制

### 文件加载

```csharp
// 获取项目目录中的所有 .cs 文件
var codeFiles = new Dictionary<string, string>();
var csFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories);

foreach (var filePath in csFiles)
{
    var fileName = Path.GetFileName(filePath);      // "MainWorkflow.cs"
    var code = File.ReadAllText(filePath);          // 读取文件内容
    codeFiles[fileName] = code;                     // 添加到字典
}
```

**数据结构**:
```csharp
Dictionary<string, string> codeFiles = new()
{
    { "MainWorkflow.cs", "public class MainWorkflow : CodedWorkflowBase { ... }" },
    { "Helper.cs", "public class Helper { public void DoSomething() { ... } }" },
    { "Config.cs", "public static class Config { public const string Name = \"App\"; }" }
};
```

### 编译到单个程序集

```csharp
// 所有文件编译到同一个程序集
var compileResult = _compiler.CompileMultiple(codeFiles);
```

**程序集结构**:
```
DynamicAssembly_12345678.dll (内存中)
├── MainWorkflow (class)
├── Helper (class)
└── Config (class)
```

**为什么可以互相引用**:
- 所有类在同一个程序集中
- 编译时可以解析类型引用
- 运行时可以直接访问

### 类型查找和匹配

```csharp
// 1. 从当前文件名推断目标类名
var targetTypeName = Path.GetFileNameWithoutExtension(CurrentFilePath);
// "E:\...\MainWorkflow.cs" → "MainWorkflow"

// 2. 查找所有工作流类型
var workflowTypes = assembly.GetTypes()
    .Where(t => t.IsSubclassOf(typeof(CodedWorkflowBase)) && !t.IsAbstract)
    .ToList();
// 可能找到: [MainWorkflow, TestWorkflow, HelperWorkflow]

// 3. 优先匹配文件名
var matchedType = workflowTypes.FirstOrDefault(
    t => t.Name.Equals(targetTypeName, StringComparison.OrdinalIgnoreCase)
);
// 找到: MainWorkflow (因为文件名是 MainWorkflow.cs)
```

**匹配策略**:
1. ✅ **优先**: 类名 == 文件名 (不区分大小写)
2. ✅ **Fallback 1**: 如果只有一个工作流类，使用它
3. ⚠️ **Fallback 2**: 使用第一个工作流类（显示警告）

---

## 反射和动态加载

### 反射 API 使用

#### 1. 获取程序集中的所有类型

```csharp
Type[] allTypes = assembly.GetTypes();
// 返回程序集中的所有 public/internal 类型
```

#### 2. 类型过滤

```csharp
// 方法 1: LINQ
var workflowTypes = allTypes
    .Where(t => t.IsSubclassOf(typeof(CodedWorkflowBase)))
    .Where(t => !t.IsAbstract)
    .ToList();

// 方法 2: 循环
var workflowTypes = new List<Type>();
foreach (var type in allTypes)
{
    if (type.IsSubclassOf(typeof(CodedWorkflowBase)) && !type.IsAbstract)
        workflowTypes.Add(type);
}
```

#### 3. 类型属性查询

```csharp
Type type = typeof(MainWorkflow);

// 基本信息
string name = type.Name;                    // "MainWorkflow"
string fullName = type.FullName;            // "Namespace.MainWorkflow"
bool isAbstract = type.IsAbstract;          // false
bool isClass = type.IsClass;                // true

// 继承关系
Type baseType = type.BaseType;              // typeof(CodedWorkflowBase)
bool isSubclass = type.IsSubclassOf(typeof(CodedWorkflowBase)); // true

// 成员查询
MethodInfo[] methods = type.GetMethods();
PropertyInfo[] properties = type.GetProperties();
FieldInfo[] fields = type.GetFields();
```

#### 4. 动态调用方法

```csharp
// 查找方法
var executeMethod = type.GetMethod("Execute");

// 调用方法
object instance = Activator.CreateInstance(type);
executeMethod.Invoke(instance, null);  // null = 无参数
```

### 动态加载的生命周期

```
1. 编译代码
   ↓
2. 生成内存程序集 (Assembly)
   ↓
3. 程序集加载到 AppDomain
   ↓
4. 使用反射查询类型
   ↓
5. 创建实例
   ↓
6. 调用方法
   ↓
7. [注意] 程序集无法卸载（直到应用退出）
```

**重要限制**:
- **无法卸载**: .NET Framework 和 .NET Core 默认无法卸载程序集
- **内存累积**: 每次编译都会加载新程序集到内存
- **解决方案**: 使用 `AssemblyLoadContext` (仅 .NET Core 3.0+)

---

## UI 线程同步

### 问题: 跨线程访问 UI

**错误示例**:
```csharp
// 在后台线程中直接修改 UI 属性 - 会抛出异常
Task.Run(() =>
{
    Output = "更新输出";  // ❌ InvalidOperationException
});
```

**错误信息**:
```
System.InvalidOperationException:
The calling thread cannot access this object because a different thread owns it.
```

### 解决方案 1: SynchronizationContext

```csharp
// 捕获 UI 线程的 SynchronizationContext
private SynchronizationContext _uiContext;

public void Initialize()
{
    _uiContext = SynchronizationContext.Current;  // 在 UI 线程调用
}

// 在后台线程中使用
Task.Run(() =>
{
    // 切换到 UI 线程执行
    _uiContext.Post(_ =>
    {
        Output = "更新输出";  // ✅ 在 UI 线程执行
    }, null);
});
```

**工作原理**:
1. `SynchronizationContext.Current` 捕获当前线程的同步上下文
2. WPF 的 UI 线程有特殊的同步上下文
3. `Post()` 将委托发送到 UI 线程的消息队列
4. UI 线程处理消息时执行委托

### 解决方案 2: Dispatcher

```csharp
// WPF 特有的方式
Application.Current.Dispatcher.Invoke(() =>
{
    Output = "更新输出";  // ✅ 在 UI 线程执行
});

// 或异步版本
await Application.Current.Dispatcher.InvokeAsync(() =>
{
    Output = "更新输出";
});
```

### 在代码中的应用

#### DebuggerServiceV3

```csharp
public async Task<bool> StartDebuggingAsync(...)
{
    // 捕获 UI 线程上下文
    _uiContext = SynchronizationContext.Current;

    // 启动后台线程
    Task.Run(() => ExecuteWorkflowAsync());
}

private void OnLineExecuting(int lineNumber)
{
    // 在后台线程中被调用

    // 使用 Post 切换到 UI 线程
    if (_uiContext != null)
    {
        _uiContext.Post(_ =>
        {
            CurrentLineChanged?.Invoke(lineNumber);  // ✅ 安全
        }, null);
    }
}
```

---

## 控制台输出重定向

### 问题: Console.WriteLine 不显示

用户代码:
```csharp
public override void Execute()
{
    Console.WriteLine("Hello World");  // 输出到哪里？
}
```

**默认行为**:
- 在 WPF 应用中，`Console.WriteLine` 输出到调试输出窗口（Visual Studio 的 Output）
- 用户看不到输出

### 解决方案: 重定向控制台输出

#### ConsoleRedirectWriter

```csharp
public class ConsoleRedirectWriter : TextWriter
{
    private readonly Action<string> _writeAction;

    public ConsoleRedirectWriter(Action<string> writeAction)
    {
        _writeAction = writeAction;
    }

    public override void Write(char value)
    {
        _writeAction?.Invoke(value.ToString());
    }

    public override void Write(string value)
    {
        _writeAction?.Invoke(value);
    }

    public override void WriteLine(string value)
    {
        _writeAction?.Invoke(value + Environment.NewLine);
    }

    public override Encoding Encoding => Encoding.UTF8;
}
```

**工作原理**:
1. 继承 `TextWriter` 基类
2. 重写 `Write` 和 `WriteLine` 方法
3. 调用自定义的 `Action<string>` 委托

#### 在 MainViewModel 中设置

```csharp
public MainViewModel()
{
    // 创建重定向写入器
    var consoleWriter = new ConsoleRedirectWriter(msg => AppendOutput(msg));

    // 替换默认的控制台输出
    Console.SetOut(consoleWriter);
}
```

**效果**:
```csharp
// 用户代码
Console.WriteLine("测试输出");

// 实际执行流程
Console.WriteLine("测试输出")
    ↓
ConsoleRedirectWriter.WriteLine("测试输出")
    ↓
_writeAction.Invoke("测试输出")
    ↓
AppendOutput("测试输出")
    ↓
Output 属性更新
    ↓
UI 显示输出
```

### 完整的输出路径

#### 方式 1: 使用 Log 方法

```csharp
public class MainWorkflow : CodedWorkflowBase
{
    public override void Execute()
    {
        Log("使用 Log 方法");  // 推荐
    }
}
```

**流程**:
```
Log("消息")
    ↓
LogEvent?.Invoke(this, "消息")
    ↓
MainViewModel 的事件处理器
    ↓
AppendOutput("消息")
    ↓
UI 更新
```

#### 方式 2: 使用 Console.WriteLine

```csharp
public class MainWorkflow : CodedWorkflowBase
{
    public override void Execute()
    {
        Console.WriteLine("使用 Console");
    }
}
```

**流程**:
```
Console.WriteLine("消息")
    ↓
ConsoleRedirectWriter.WriteLine("消息")
    ↓
AppendOutput("消息")
    ↓
UI 更新
```

---

## 调试模式的代码插桩技术

### 什么是代码插桩

**原始代码**:
```csharp
public override void Execute()
{
    int x = 10;           // 第 8 行
    int y = 20;           // 第 9 行
    int z = x + y;        // 第 10 行
    Console.WriteLine(z); // 第 11 行
}
```

**插桩后的代码**:
```csharp
public static Action<int> __debugCallback;  // 添加回调字段

public override void Execute()
{
    __debugCallback?.Invoke(8);   // 插入回调
    int x = 10;

    __debugCallback?.Invoke(9);   // 插入回调
    int y = 20;

    __debugCallback?.Invoke(10);  // 插入回调
    int z = x + y;

    __debugCallback?.Invoke(11);  // 插入回调
    Console.WriteLine(z);
}
```

### Roslyn 语法重写器

```csharp
private class DebugInstrumentationRewriter : CSharpSyntaxRewriter
{
    // 访问类声明 - 添加回调字段
    public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        // 创建字段声明: public static Action<int> __debugCallback;
        var callbackField = SyntaxFactory.FieldDeclaration(
            SyntaxFactory.VariableDeclaration(
                SyntaxFactory.IdentifierName("Action<int>"))
            .WithVariables(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.VariableDeclarator("__debugCallback"))))
        .WithModifiers(SyntaxFactory.TokenList(
            SyntaxFactory.Token(SyntaxKind.PublicKeyword),
            SyntaxFactory.Token(SyntaxKind.StaticKeyword)));

        // 添加字段到类中
        node = node.AddMembers(callbackField);

        return base.VisitClassDeclaration(node);
    }

    // 访问方法块 - 在每个语句前插入回调
    public override SyntaxNode VisitBlock(BlockSyntax node)
    {
        var newStatements = new List<StatementSyntax>();

        foreach (var statement in node.Statements)
        {
            // 获取语句的行号
            var lineSpan = statement.GetLocation().GetLineSpan();
            int lineNumber = lineSpan.StartLinePosition.Line + 1;

            // 创建回调语句: __debugCallback?.Invoke(lineNumber);
            var callbackStatement = SyntaxFactory.ParseStatement(
                $"__debugCallback?.Invoke({lineNumber});\r\n");

            // 先添加回调，再添加原始语句
            newStatements.Add(callbackStatement);
            newStatements.Add(statement);
        }

        return node.WithStatements(SyntaxFactory.List(newStatements));
    }
}
```

### 设置回调委托

```csharp
// 编译后，找到类型
var workflowType = assembly.GetType("MainWorkflow");

// 获取回调字段
var callbackField = workflowType.GetField("__debugCallback",
    BindingFlags.Public | BindingFlags.Static);

// 设置回调方法
Action<int> callback = (lineNumber) =>
{
    Console.WriteLine($"执行到第 {lineNumber} 行");
    // 可以在这里暂停、更新 UI 等
};

callbackField.SetValue(null, callback);  // null 因为是静态字段

// 执行
var instance = Activator.CreateInstance(workflowType);
var executeMethod = workflowType.GetMethod("Execute");
executeMethod.Invoke(instance, null);
```

### 行号映射

**问题**: 插桩后行号会偏移

**原始代码**:
```csharp
public override void Execute()  // 第 7 行
{                               // 第 8 行
    int x = 10;                 // 第 9 行
    int y = 20;                 // 第 10 行
}
```

**插桩后**:
```csharp
public static Action<int> __debugCallback;  // 第 2 行（新增）

public override void Execute()              // 第 7 行（不变）
{                                           // 第 8 行（不变）
    __debugCallback?.Invoke(9);             // 第 9 行（新增）
    int x = 10;                             // 第 10 行（偏移 +1）
    __debugCallback?.Invoke(10);            // 第 11 行（新增）
    int y = 20;                             // 第 12 行（偏移 +2）
}
```

**解决方案**: 行号映射字典

```csharp
private Dictionary<int, int> _lineMapping = new Dictionary<int, int>();
// 插桩后行号 -> 原始行号

// 插桩时记录映射
int insertedLines = 0;
foreach (var statement in node.Statements)
{
    int originalLineNumber = statement.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    // 回调行号 = 原始行号 + 已插入的行数
    int callbackLineNumber = originalLineNumber + insertedLines;

    // 记录映射
    _lineMapping[callbackLineNumber] = originalLineNumber;

    insertedLines++;  // 每插入一个回调，偏移 +1
}

// 使用时映射回原始行号
private void OnLineExecuting(int instrumentedLineNumber)
{
    int originalLineNumber = _lineMapping.ContainsKey(instrumentedLineNumber)
        ? _lineMapping[instrumentedLineNumber]
        : instrumentedLineNumber;

    // 使用 originalLineNumber 更新 UI
}
```

---

## 性能考虑

### 编译性能

**编译时间**:
- 单文件（~100 行）: ~100-200ms
- 多文件（3 个文件）: ~300-500ms

**优化方法**:
1. 缓存编译结果（如果代码未变）
2. 使用增量编译（仅重新编译修改的文件）
3. 延迟加载引用程序集

### 内存占用

**问题**: 每次编译都会创建新程序集

```csharp
DynamicAssembly_12345678 (无法卸载)
DynamicAssembly_12345679 (无法卸载)
DynamicAssembly_12345680 (无法卸载)
...
```

**改进方案** (.NET Core 3.0+):
```csharp
using System.Runtime.Loader;

var alc = new AssemblyLoadContext("DynamicContext", isCollectible: true);
var assembly = alc.LoadFromStream(ms);

// 使用完后卸载
alc.Unload();
```

### 执行性能

**运行模式**:
- 直接执行编译后的 IL 代码
- 性能接近原生 .NET 代码

**调试模式**:
- 每行前有回调开销（~1-5ms/行）
- TaskCompletionSource 等待开销
- UI 更新开销

---

## 总结

### 关键技术栈

1. **Roslyn**: 编译器即服务
2. **反射 (Reflection)**: 动态类型查询和调用
3. **程序集加载**: 动态加载编译后的代码
4. **语法树重写**: 代码插桩
5. **SynchronizationContext**: 跨线程同步
6. **TextWriter 重定向**: 控制台输出捕获
7. **MVVM**: WPF 数据绑定

### 数据流

```
用户编写代码
    ↓
Actipro 编辑器
    ↓
MainViewModel.Code (string)
    ↓
RoslynCompilerService.Compile()
    ↓
内存程序集 (Assembly)
    ↓
反射查找类型
    ↓
Activator.CreateInstance()
    ↓
workflow.Execute()
    ↓
用户代码执行
    ↓
Console.WriteLine / Log()
    ↓
事件/重定向
    ↓
MainViewModel.AppendOutput()
    ↓
Output 属性更新
    ↓
UI 数据绑定
    ↓
TextBox 显示输出
```

---

**最后更新**: 2026-01-13
**适用版本**: 当前 ActiproRoslynPOC 项目
