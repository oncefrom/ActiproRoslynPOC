# 自定义 Workflow Activities

本目录包含用于 XAML 工作流的自定义 Activity 组件。

## InvokeCodedWorkflow

**功能**: 在 XAML 工作流中调用 C# 编写的工作流 (CodedWorkflow)

**属性**:
- `WorkflowFilePath` (输入, 必需): C# 工作流文件路径,支持相对路径或绝对路径
- `Arguments` (输入, 可选): 传递给工作流的参数字典 `Dictionary<string, object>`
- `Result` (输出, 可选): 工作流的执行结果

**使用示例**:

```xml
<Sequence>
    <Sequence.Variables>
        <Variable x:TypeArguments="x:String" Name="csWorkflowPath" Default="Workflows/MyWorkflow.cs" />
        <Variable x:TypeArguments="scg:Dictionary(x:String, x:Object)" Name="args" />
        <Variable x:TypeArguments="x:Object" Name="result" />
    </Sequence.Variables>

    <!-- 调用 C# 工作流 -->
    <local:InvokeCodedWorkflow
        WorkflowFilePath="[csWorkflowPath]"
        Arguments="[args]"
        Result="[result]" />

    <!-- 输出结果 -->
    <WriteLine Text="[result.ToString()]" />
</Sequence>
```

**C# 工作流示例** (MyWorkflow.cs):
```csharp
using System;
using ActiproRoslynPOC.Models;

public class MyWorkflow : CodedWorkflowBase
{
    [Workflow(Name = "MyWorkflow", Description = "示例工作流")]
    public override void Execute()
    {
        // 获取输入参数
        var input = GetArgument<string>("input", "默认值");

        Log($"接收到参数: {input}");

        // 执行业务逻辑
        var output = $"处理完成: {input}";

        // 设置返回结果
        Result = output;
    }
}
```

---

## InvokeWorkflow

**功能**: 在 XAML 工作流中调用其他 XAML 工作流

**属性**:
- `WorkflowFilePath` (输入, 必需): XAML 工作流文件路径,支持相对路径或绝对路径
- `Arguments` (输入, 可选): 传递给工作流的参数字典 `Dictionary<string, object>`
- `Result` (输出, 可选): 工作流的执行结果

**使用示例**:

```xml
<Sequence>
    <Sequence.Variables>
        <Variable x:TypeArguments="x:String" Name="xamlWorkflowPath" Default="Workflows/SubWorkflow.xaml" />
        <Variable x:TypeArguments="scg:Dictionary(x:String, x:Object)" Name="args" />
        <Variable x:TypeArguments="x:Object" Name="result" />
    </Sequence.Variables>

    <!-- 初始化参数字典 -->
    <Assign>
        <Assign.To>
            <OutArgument x:TypeArguments="scg:Dictionary(x:String, x:Object)">[args]</OutArgument>
        </Assign.To>
        <Assign.Value>
            <InArgument x:TypeArguments="scg:Dictionary(x:String, x:Object)">
                [new Dictionary(Of String, Object) From {
                    {"param1", "值1"},
                    {"param2", 123}
                }]
            </InArgument>
        </Assign.Value>
    </Assign>

    <!-- 调用 XAML 工作流 -->
    <local:InvokeWorkflow
        WorkflowFilePath="[xamlWorkflowPath]"
        Arguments="[args]"
        Result="[result]" />

    <!-- 输出结果 -->
    <WriteLine Text="[result.ToString()]" />
</Sequence>
```

**子工作流示例** (SubWorkflow.xaml):
```xml
<Activity x:Class="SubWorkflow"
 xmlns="http://schemas.microsoft.com/netfx/2009/xaml/activities"
 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Sequence>
        <Sequence.Variables>
            <Variable x:TypeArguments="x:String" Name="param1" />
            <Variable x:TypeArguments="x:Int32" Name="param2" />
        </Sequence.Variables>

        <WriteLine Text="[String.Format(&quot;参数1: {0}, 参数2: {1}&quot;, param1, param2)]" />

        <!-- 可以在这里添加更多业务逻辑 -->
    </Sequence>
</Activity>
```

---

## 命名空间引用

在 XAML 工作流中使用这些 Activity 时,需要添加以下命名空间引用:

```xml
<Activity
    xmlns="http://schemas.microsoft.com/netfx/2009/xaml/activities"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="clr-namespace:ActiproRoslynPOC.Activities;assembly=ActiproRoslynPOC"
    xmlns:scg="clr-namespace:System.Collections.Generic;assembly=mscorlib">

    <!-- 您的工作流内容 -->

</Activity>
```

---

## 注意事项

1. **文件路径解析**:
   - 相对路径会基于当前工作目录解析
   - 建议使用相对于项目根目录的路径,如 `"Workflows/MyWorkflow.cs"`
   - 绝对路径也受支持

2. **参数传递**:
   - 参数通过 `Dictionary<string, object>` 传递
   - C# 工作流可以通过 `GetArgument<T>(key, defaultValue)` 获取参数
   - XAML 工作流会自动将参数映射到对应的输入变量

3. **错误处理**:
   - 文件不存在会抛出 `FileNotFoundException`
   - 编译错误会抛出 `InvalidOperationException` 并包含详细的错误信息
   - 执行错误会传播到调用方

4. **性能考虑**:
   - C# 工作流会在首次调用时编译,后续调用会复用已编译的程序集
   - XAML 工作流每次调用都会重新加载和执行

---

## 集成到工作流设计器

1. 打开 XAML 工作流文件 (双击 .xaml 文件)
2. 工作流设计器会自动打开
3. 从工具箱中拖拽 `InvokeCodedWorkflow` 或 `InvokeWorkflow` 活动到设计器
4. 在属性窗口中配置活动的属性
5. 保存工作流 (Ctrl+S)

如果工具箱中没有显示这些活动,请确保项目已正确编译。
