# 工作流设计器使用指南

欢迎使用 ActiproRoslynPOC 简化版 UiPath Studio! 本指南将帮助您快速开始使用可视化工作流设计器。

---

## 快速开始

### 1. 创建新项目

1. 点击工具栏的 **"新建项目"** 按钮 (📦➕)
2. 选择项目保存位置
3. 输入项目名称
4. 项目会自动创建以下结构:
   ```
   MyProject/
   ├── project.json       # 项目配置文件
   ├── Workflows/         # 工作流文件夹
   └── Dependencies/      # 依赖文件夹
   ```

### 2. 创建 XAML 工作流

1. 在项目资源管理器中,右键点击文件夹
2. 选择 **"新建 XAML 工作流"**
3. 输入工作流名称 (例如: `MainWorkflow`)
4. 双击新创建的 `.xaml` 文件打开工作流设计器

### 3. 使用工作流设计器

#### 设计器布局

打开 XAML 工作流后,您会看到:
- **左侧**: 工具箱 (包含所有可用的 Activity)
- **右侧**: 工作流设计画布

#### 工具箱分类

1. **控制流**
   - Sequence (顺序执行)
   - Flowchart (流程图)
   - If (条件判断)
   - While / DoWhile (循环)
   - ForEach (集合遍历)
   - Switch (分支选择)
   - Parallel (并行执行)

2. **基本活动**
   - Assign (赋值)
   - Delay (延时)
   - WriteLine (输出文本)
   - InvokeMethod (调用方法)

3. **集合操作**
   - AddToCollection (添加到集合)
   - RemoveFromCollection (从集合移除)
   - ExistsInCollection (检查是否存在)
   - ClearCollection (清空集合)

4. **错误处理**
   - TryCatch (异常捕获)
   - Throw (抛出异常)
   - Rethrow (重新抛出)

5. **工作流调用** ⭐ (自定义)
   - **InvokeCodedWorkflow** - 调用 C# 工作流
   - **InvokeWorkflow** - 调用其他 XAML 工作流

#### 添加 Activity

1. 从工具箱拖拽 Activity 到画布
2. 在属性窗口配置 Activity 参数
3. 使用 VB.NET 表达式语法设置属性值

### 4. 创建 C# 工作流

1. 右键点击文件夹 → **"新建 CS 工作流"**
2. 输入工作流名称 (例如: `DataProcessing`)
3. 双击打开代码编辑器
4. 编写工作流逻辑:

```csharp
using System;
using ActiproRoslynPOC.Models;

public class DataProcessing : CodedWorkflowBase
{
    [Workflow(Name = "DataProcessing", Description = "数据处理工作流")]
    public override void Execute()
    {
        // 获取输入参数
        var input = GetArgument<string>("data", "");

        Log($"开始处理数据: {input}");

        // 执行业务逻辑
        var processed = input.ToUpper();

        // 设置返回结果
        Result = processed;

        Log($"处理完成: {processed}");
    }
}
```

### 5. 在 XAML 中调用 C# 工作流

1. 在 XAML 工作流设计器中,从工具箱拖拽 **InvokeCodedWorkflow**
2. 设置属性:
   - **WorkflowFilePath**: `"Workflows/DataProcessing.cs"`
   - **Arguments**: 创建参数字典
   - **Result**: 接收返回值的变量

示例 XAML:
```xml
<Sequence>
    <Sequence.Variables>
        <Variable x:TypeArguments="scg:Dictionary(x:String, x:Object)" Name="args" />
        <Variable x:TypeArguments="x:Object" Name="result" />
    </Sequence.Variables>

    <!-- 初始化参数 -->
    <Assign>
        <Assign.To>
            <OutArgument x:TypeArguments="scg:Dictionary(x:String, x:Object)">[args]</OutArgument>
        </Assign.To>
        <Assign.Value>
            <InArgument x:TypeArguments="scg:Dictionary(x:String, x:Object)">
                [new Dictionary(Of String, Object) From {{"data", "hello world"}}]
            </InArgument>
        </Assign.Value>
    </Assign>

    <!-- 调用 C# 工作流 -->
    <local:InvokeCodedWorkflow
        WorkflowFilePath="Workflows/DataProcessing.cs"
        Arguments="[args]"
        Result="[result]" />

    <!-- 输出结果 -->
    <WriteLine Text="[result.ToString()]" />
</Sequence>
```

### 6. 运行工作流

#### 运行 XAML 工作流
1. 打开 XAML 工作流文件
2. 点击工具栏的 **"运行工作流"** 按钮 (▶ 蓝色)
3. 或按 **F6** 快捷键
4. 查看输出窗口的执行结果

#### 运行 C# 代码
1. 打开 C# 文件
2. 点击工具栏的 **"运行代码"** 按钮 (▶ 绿色)
3. 或按 **F5** 快捷键

### 7. 保存工作流

- 点击 **"保存"** 按钮 (💾)
- 或按 **Ctrl+S**
- 工作流会自动保存为 XAML 格式

---

## 常见任务示例

### 示例 1: 简单的顺序工作流

```xml
<Sequence DisplayName="Hello World">
    <WriteLine Text="Hello, World!" />
    <WriteLine Text="This is a workflow." />
</Sequence>
```

### 示例 2: 带变量的工作流

```xml
<Sequence>
    <Sequence.Variables>
        <Variable x:TypeArguments="x:String" Name="name" Default="User" />
        <Variable x:TypeArguments="x:String" Name="greeting" />
    </Sequence.Variables>

    <Assign>
        <Assign.To>
            <OutArgument x:TypeArguments="x:String">[greeting]</OutArgument>
        </Assign.To>
        <Assign.Value>
            <InArgument x:TypeArguments="x:String">["Hello, " + name + "!"]</InArgument>
        </Assign.Value>
    </Assign>

    <WriteLine Text="[greeting]" />
</Sequence>
```

### 示例 3: 条件判断

```xml
<Sequence>
    <Sequence.Variables>
        <Variable x:TypeArguments="x:Int32" Name="age" Default="18" />
    </Sequence.Variables>

    <If Condition="[age &gt;= 18]">
        <If.Then>
            <WriteLine Text="Adult" />
        </If.Then>
        <If.Else>
            <WriteLine Text="Minor" />
        </If.Else>
    </If>
</Sequence>
```

### 示例 4: 循环处理

```xml
<Sequence>
    <Sequence.Variables>
        <Variable x:TypeArguments="x:Int32" Name="counter" Default="0" />
    </Sequence.Variables>

    <While Condition="[counter &lt; 5]">
        <Sequence>
            <WriteLine Text="[&quot;Count: &quot; + counter.ToString()]" />
            <Assign>
                <Assign.To>
                    <OutArgument x:TypeArguments="x:Int32">[counter]</OutArgument>
                </Assign.To>
                <Assign.Value>
                    <InArgument x:TypeArguments="x:Int32">[counter + 1]</InArgument>
                </Assign.Value>
            </Assign>
        </Sequence>
    </While>
</Sequence>
```

---

## 最佳实践

### 1. 工作流组织
- 将相关的工作流放在同一个文件夹
- 使用有意义的文件名 (如: `ProcessOrder.xaml`, `ValidateData.cs`)
- 为工作流添加描述性注释

### 2. 变量命名
- 使用 camelCase 命名变量 (如: `userName`, `orderCount`)
- 使用描述性名称,避免缩写
- 为变量设置合理的默认值

### 3. 错误处理
- 对可能出错的操作使用 TryCatch
- 记录详细的错误信息
- 提供友好的错误提示

### 4. 代码复用
- 将通用逻辑封装为独立的 C# 工作流
- 使用 InvokeCodedWorkflow 在多个地方调用
- 避免重复代码

### 5. 性能优化
- 避免在循环中进行耗时操作
- 合理使用 Parallel 活动并行处理
- 及时释放不再使用的资源

---

## 快捷键

| 快捷键 | 功能 |
|--------|------|
| **Ctrl+S** | 保存当前文件 |
| **F5** | 运行 C# 代码 |
| **F6** | 运行 XAML 工作流 |
| **F9** | 切换断点 (C# 代码) |
| **F10** | 单步执行 (调试) |

---

## 故障排除

### 问题 1: 工具箱中看不到自定义 Activity

**解决方案**:
1. 确保项目已成功编译
2. 重新打开 XAML 工作流文件
3. 检查 InvokeCodedWorkflow 和 InvokeWorkflow 是否在 "工作流调用" 分类中

### 问题 2: 运行工作流时出错

**解决方案**:
1. 检查输出窗口的错误信息
2. 确认所有必需的参数都已设置
3. 验证文件路径是否正确
4. 确保引用的 C# 工作流已存在

### 问题 3: C# 工作流编译失败

**解决方案**:
1. 检查代码语法错误
2. 确认继承自 `CodedWorkflowBase`
3. 添加 `[Workflow]` 特性
4. 检查输出窗口的详细编译错误

---

## 更多资源

- [Activities 使用文档](Activities/README.md)
- [项目示例](../TestWorkflows/)
- [API 参考](https://docs.microsoft.com/en-us/dotnet/framework/windows-workflow-foundation/)

---

**祝您使用愉快! 🎉**

如有问题或建议,请联系开发团队。
