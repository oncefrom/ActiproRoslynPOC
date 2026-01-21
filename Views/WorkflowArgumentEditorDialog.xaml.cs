using ActiproRoslynPOC.Models;
using ActiproRoslynPOC.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace ActiproRoslynPOC.Views
{
    /// <summary>
    /// 工作流参数编辑对话框 - 支持通过反射读取工作流参数定义
    /// </summary>
    public partial class WorkflowArgumentEditorDialog : Window
    {
        public ObservableCollection<WorkflowArgumentItem> Arguments { get; set; }
        public bool IsOk { get; private set; }

        private string _workflowFilePath;
        private Type _workflowType;

        public WorkflowArgumentEditorDialog()
        {
            InitializeComponent();
            Arguments = new ObservableCollection<WorkflowArgumentItem>();
            ArgumentsDataGrid.ItemsSource = Arguments;
        }

        /// <summary>
        /// 设置工作流文件路径（不自动分析，保留已有参数）
        /// </summary>
        public void SetWorkflowPath(string workflowFilePath)
        {
            _workflowFilePath = workflowFilePath;
            WorkflowPathTextBox.Text = workflowFilePath;

            // 只有在没有参数时才自动分析
            // 如果已经有参数（从 VB 表达式加载的），保留它们
            if (Arguments.Count == 0 && !string.IsNullOrWhiteSpace(workflowFilePath))
            {
                AnalyzeWorkflowArguments();
            }
        }

        /// <summary>
        /// 设置工作流文件路径并强制分析参数（清空已有参数）
        /// </summary>
        public void SetWorkflowPathAndAnalyze(string workflowFilePath)
        {
            _workflowFilePath = workflowFilePath;
            WorkflowPathTextBox.Text = workflowFilePath;

            if (!string.IsNullOrWhiteSpace(workflowFilePath))
            {
                AnalyzeWorkflowArguments();
            }
        }

        /// <summary>
        /// 从 Dictionary 加载已有的参数值
        /// </summary>
        public void LoadArgumentValues(Dictionary<string, object> existingArguments)
        {
            if (existingArguments == null) return;

            foreach (var argItem in Arguments)
            {
                if (existingArguments.ContainsKey(argItem.Name))
                {
                    argItem.Value = existingArguments[argItem.Name]?.ToString() ?? "";
                }
            }
        }

        /// <summary>
        /// 从 VB 表达式加载参数值（用于重新打开对话框时还原）
        /// </summary>
        public void LoadFromVBExpression(string vbExpression)
        {
            if (string.IsNullOrWhiteSpace(vbExpression) || vbExpression == "Nothing")
                return;

            try
            {
                // 简单解析 VB 表达式
                // 格式: New Dictionary(Of String, Object) From {{"key1", value1}, {"key2", value2}}

                var startIndex = vbExpression.IndexOf("From {");
                if (startIndex < 0)
                    return;

                var content = vbExpression.Substring(startIndex + 6); // 跳过 "From {"
                var endIndex = content.LastIndexOf("}");
                if (endIndex < 0)
                    return;

                content = content.Substring(0, endIndex).Trim();

                // 分割键值对
                var pairs = SplitDictionaryPairs(content);

                foreach (var pair in pairs)
                {
                    var parts = SplitKeyValue(pair);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim().Trim('"');
                        var value = parts[1].Trim();

                        // 查找对应的参数项，如果不存在则创建
                        var argItem = Arguments.FirstOrDefault(a => a.Name == key);
                        if (argItem == null)
                        {
                            // 创建新的参数项
                            argItem = new WorkflowArgumentItem
                            {
                                Name = key,
                                TypeName = "Object",
                                Type = typeof(object),
                                Direction = WorkflowWorkflowArgumentDirection.In,
                                IsReadOnly = false
                            };
                            Arguments.Add(argItem);
                        }

                        // 还原值
                        if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length > 1)
                        {
                            // 字符串字面量：保留引号,这样用户看到的就是他们输入的
                            var innerValue = value.Substring(1, value.Length - 2).Replace("\"\"", "\"");
                            argItem.Value = "\"" + innerValue + "\"";
                        }
                        else
                        {
                            // 变量引用或数字,直接还原
                            argItem.Value = value;
                        }
                    }
                }
            }
            catch
            {
                // 解析失败，忽略
            }
        }

        /// <summary>
        /// 分割字典键值对（处理嵌套的花括号）
        /// </summary>
        private List<string> SplitDictionaryPairs(string content)
        {
            var pairs = new List<string>();
            int braceLevel = 0;
            int startIndex = 0;

            for (int i = 0; i < content.Length; i++)
            {
                if (content[i] == '{')
                {
                    braceLevel++;
                }
                else if (content[i] == '}')
                {
                    braceLevel--;
                    if (braceLevel == 0)
                    {
                        // 找到一个完整的键值对
                        pairs.Add(content.Substring(startIndex, i - startIndex + 1));
                        startIndex = i + 1;
                        // 跳过逗号和空格
                        while (startIndex < content.Length && (content[startIndex] == ',' || content[startIndex] == ' '))
                            startIndex++;
                        i = startIndex - 1;
                    }
                }
            }

            return pairs;
        }

        /// <summary>
        /// 分割键值对中的键和值
        /// </summary>
        private string[] SplitKeyValue(string pair)
        {
            // 移除外层的花括号
            pair = pair.Trim('{', '}').Trim();

            // 查找第一个逗号（键值分隔符）
            bool inQuotes = false;
            for (int i = 0; i < pair.Length; i++)
            {
                if (pair[i] == '"')
                {
                    // 检查是否是转义的引号
                    if (i + 1 < pair.Length && pair[i + 1] == '"')
                    {
                        i++; // 跳过转义的引号
                        continue;
                    }
                    inQuotes = !inQuotes;
                }
                else if (pair[i] == ',' && !inQuotes)
                {
                    // 找到分隔符
                    return new string[]
                    {
                        pair.Substring(0, i),
                        pair.Substring(i + 1)
                    };
                }
            }

            return new string[] { pair };
        }

        /// <summary>
        /// 转换为 Dictionary
        /// </summary>
        public Dictionary<string, object> ToDictionary()
        {
            var result = new Dictionary<string, object>();

            foreach (var item in Arguments)
            {
                if (string.IsNullOrWhiteSpace(item.Value))
                {
                    result[item.Name] = null;
                    continue;
                }

                // 根据类型转换值
                object value = ConvertValue(item.Value, item.Type);
                result[item.Name] = value;
            }

            return result;
        }

        /// <summary>
        /// 生成 VB.NET 表达式
        /// </summary>
        public string ToVBExpression()
        {
            if (Arguments.Count == 0)
            {
                return "Nothing";
            }

            var items = new List<string>();
            foreach (var argItem in Arguments)
            {
                if (string.IsNullOrWhiteSpace(argItem.Value))
                {
                    // 空值跳过
                    continue;
                }

                string valueStr = FormatValueForVB(argItem.Value, argItem.Type);
                // 使用字符串拼接而不是插值，避免引号转义问题
                items.Add("{\"" + argItem.Name + "\", " + valueStr + "}");
            }

            if (items.Count == 0)
            {
                return "Nothing";
            }

            // 使用字符串拼接构建最终表达式
            return "New Dictionary(Of String, Object) From {" + string.Join(", ", items) + "}";
        }

        /// <summary>
        /// 格式化值为 VB.NET 表达式
        /// </summary>
        private string FormatValueForVB(string valueStr, Type targetType)
        {
            if (string.IsNullOrWhiteSpace(valueStr))
            {
                return "Nothing";
            }

            // 用户输入什么就是什么，直接返回
            // 如果用户想输入字符串 "hello"，他们可以输入：hello 或 "hello"
            // 如果用户想引用变量 InputMessage，他们应该输入：InputMessage
            // 如果用户想输入数字 123，他们应该输入：123

            // 检查用户是否已经输入了引号（字符串字面量）
            if (valueStr.StartsWith("\"") && valueStr.EndsWith("\"") && valueStr.Length >= 2)
            {
                // 用户已经加了引号，处理内部的引号转义
                var innerValue = valueStr.Substring(1, valueStr.Length - 2);
                // VB.NET 中内部引号要双写
                return "\"" + innerValue.Replace("\"", "\"\"") + "\"";
            }

            // 检查是否是变量引用（不包含引号,且符合变量命名规则）
            if (IsVariableReference(valueStr))
            {
                // 直接作为变量引用，不加引号
                return valueStr;
            }

            // 检查是否是数字
            if (double.TryParse(valueStr, out _))
            {
                return valueStr;
            }

            // 检查是否是布尔值
            if (bool.TryParse(valueStr, out bool boolValue))
            {
                return boolValue ? "True" : "False";
            }

            // 其他情况作为字符串字面量，加引号（VB.NET 中内部引号要双写）
            return "\"" + valueStr.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// 检查是否是变量引用
        /// 规则：以字母或下划线开头，只包含字母、数字、下划线
        /// </summary>
        private bool IsVariableReference(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            // 检查是否以字母或下划线开头
            if (!char.IsLetter(value[0]) && value[0] != '_')
                return false;

            // 检查是否只包含字母、数字、下划线
            for (int i = 1; i < value.Length; i++)
            {
                if (!char.IsLetterOrDigit(value[i]) && value[i] != '_')
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 分析工作流参数
        /// </summary>
        private void AnalyzeWorkflowArguments()
        {
            try
            {
                Arguments.Clear();
                WorkflowClassTextBox.Text = "";

                if (string.IsNullOrWhiteSpace(_workflowFilePath))
                {
                    MessageBox.Show("请先指定工作流文件路径", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 解析完整路径
                var fullPath = ResolveWorkflowPath(_workflowFilePath);

                if (!File.Exists(fullPath))
                {
                    // 尝试显示详细的搜索路径信息
                    var searchPaths = GetSearchPaths(_workflowFilePath);
                    var searchInfo = string.Join("\n", searchPaths.Select(p => $"  - {p}"));
                    MessageBox.Show($"文件不存在: {_workflowFilePath}\n\n已尝试以下路径:\n{searchInfo}",
                        "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var extension = Path.GetExtension(fullPath).ToLowerInvariant();

                if (extension == ".cs")
                {
                    AnalyzeCSharpWorkflow(fullPath);
                }
                else if (extension == ".xaml")
                {
                    AnalyzeXamlWorkflow(fullPath);
                }
                else
                {
                    MessageBox.Show($"不支持的文件类型: {extension}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"分析参数失败: {ex.Message}\n\n{ex.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 分析 C# 工作流参数
        /// </summary>
        private void AnalyzeCSharpWorkflow(string csFilePath)
        {
            var workflowDirectory = Path.GetDirectoryName(csFilePath);
            var compiler = new RoslynCompilerService();

            // 获取所有 .cs 文件并编译
            var codeFiles = new Dictionary<string, string>();
            var csFiles = Directory.GetFiles(workflowDirectory, "*.cs", SearchOption.AllDirectories);

            foreach (var filePath in csFiles)
            {
                var fileName = Path.GetFileName(filePath);
                codeFiles[fileName] = File.ReadAllText(filePath);
            }

            var compileResult = compiler.CompileMultiple(codeFiles);
            if (!compileResult.Success)
            {
                MessageBox.Show($"编译失败: {compileResult.ErrorSummary}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 查找工作流类型
            var targetFileName = Path.GetFileNameWithoutExtension(csFilePath);
            _workflowType = compileResult.Assembly.GetTypes()
                .FirstOrDefault(t => t.Name == targetFileName &&
                                    t.IsSubclassOf(typeof(CodedWorkflowBase)) &&
                                    !t.IsAbstract);

            if (_workflowType == null)
            {
                MessageBox.Show($"找不到工作流类: {targetFileName}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            WorkflowClassTextBox.Text = _workflowType.FullName;

            // 查找 Execute 方法的参数
            var executeMethod = _workflowType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "Execute" || m.GetCustomAttribute<WorkflowAttribute>() != null);

            if (executeMethod != null)
            {
                // 解析输入参数
                var parameters = executeMethod.GetParameters();
                foreach (var param in parameters)
                {
                    // 检查是否是 out 或 ref 参数
                    var direction = WorkflowWorkflowArgumentDirection.In;
                    if (param.IsOut)
                    {
                        direction = WorkflowWorkflowArgumentDirection.Out;
                    }
                    else if (param.ParameterType.IsByRef)
                    {
                        direction = WorkflowWorkflowArgumentDirection.InOut;
                    }

                    Arguments.Add(new WorkflowArgumentItem
                    {
                        Name = param.Name,
                        Type = param.ParameterType,
                        TypeName = GetFriendlyTypeName(param.ParameterType),
                        Direction = direction,
                        Value = "",
                        IsReadOnly = true
                    });
                }

                // 解析返回值（支持元组作为输出参数）
                var returnType = executeMethod.ReturnType;
                if (returnType != typeof(void))
                {
                    // 检查是否是 ValueTuple（命名元组）
                    if (returnType.IsGenericType && returnType.FullName?.StartsWith("System.ValueTuple") == true)
                    {
                        // 获取元组元素的名称（通过 TupleElementNames 属性）
                        var tupleNames = executeMethod.ReturnTypeCustomAttributes
                            .GetCustomAttributes(typeof(System.Runtime.CompilerServices.TupleElementNamesAttribute), false)
                            .Cast<System.Runtime.CompilerServices.TupleElementNamesAttribute>()
                            .FirstOrDefault()?.TransformNames;

                        var tupleTypes = returnType.GetGenericArguments();

                        for (int i = 0; i < tupleTypes.Length; i++)
                        {
                            var elementName = tupleNames != null && i < tupleNames.Count ? tupleNames[i] : $"Item{i + 1}";
                            var elementType = tupleTypes[i];

                            Arguments.Add(new WorkflowArgumentItem
                            {
                                Name = elementName ?? $"Item{i + 1}",
                                Type = elementType,
                                TypeName = GetFriendlyTypeName(elementType),
                                Direction = WorkflowWorkflowArgumentDirection.Out,
                                Value = "",
                                IsReadOnly = true
                            });
                        }
                    }
                    else
                    {
                        // 非元组返回值，作为单个输出参数
                        Arguments.Add(new WorkflowArgumentItem
                        {
                            Name = "Result",
                            Type = returnType,
                            TypeName = GetFriendlyTypeName(returnType),
                            Direction = WorkflowWorkflowArgumentDirection.Out,
                            Value = "",
                            IsReadOnly = true
                        });
                    }
                }
            }

            // 如果没有找到参数，尝试通过 Arguments 属性访问
            if (Arguments.Count == 0)
            {
                MessageBox.Show("此工作流没有定义显式参数。\n\n提示：可以通过 Arguments 字典访问参数。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// 分析 XAML 工作流参数
        /// </summary>
        private void AnalyzeXamlWorkflow(string xamlFilePath)
        {
            var xamlService = new XamlWorkflowService();
            var argsInfo = xamlService.GetWorkflowArguments(xamlFilePath);

            WorkflowClassTextBox.Text = $"XAML Workflow ({argsInfo.Arguments.Count} 个参数)";

            foreach (var arg in argsInfo.Arguments)
            {
                // 转换 Services.ArgumentDirection 到 Views.WorkflowWorkflowArgumentDirection
                var direction = WorkflowWorkflowArgumentDirection.In;
                if (arg.Direction == Services.ArgumentDirection.Out)
                    direction = WorkflowWorkflowArgumentDirection.Out;
                else if (arg.Direction == Services.ArgumentDirection.InOut)
                    direction = WorkflowWorkflowArgumentDirection.InOut;

                Arguments.Add(new WorkflowArgumentItem
                {
                    Name = arg.Name,
                    Type = arg.Type,
                    TypeName = GetFriendlyTypeName(arg.Type),
                    Direction = direction,
                    Value = "",
                    IsReadOnly = true
                });
            }
        }

        /// <summary>
        /// 获取友好的类型名称
        /// </summary>
        private string GetFriendlyTypeName(Type type)
        {
            if (type == typeof(int)) return "Int32";
            if (type == typeof(long)) return "Int64";
            if (type == typeof(double)) return "Double";
            if (type == typeof(decimal)) return "Decimal";
            if (type == typeof(bool)) return "Boolean";
            if (type == typeof(string)) return "String";
            if (type == typeof(DateTime)) return "DateTime";

            if (type.IsGenericType)
            {
                var genericType = type.GetGenericTypeDefinition();
                var genericArgs = type.GetGenericArguments();
                var argNames = string.Join(", ", genericArgs.Select(GetFriendlyTypeName));
                return $"{genericType.Name.Split('`')[0]}<{argNames}>";
            }

            return type.Name;
        }

        /// <summary>
        /// 转换值类型
        /// </summary>
        private object ConvertValue(string valueStr, Type targetType)
        {
            if (string.IsNullOrWhiteSpace(valueStr))
                return null;

            try
            {
                if (targetType == typeof(string))
                    return valueStr;

                if (targetType == typeof(int))
                    return int.Parse(valueStr);

                if (targetType == typeof(long))
                    return long.Parse(valueStr);

                if (targetType == typeof(double))
                    return double.Parse(valueStr);

                if (targetType == typeof(decimal))
                    return decimal.Parse(valueStr);

                if (targetType == typeof(bool))
                    return bool.Parse(valueStr);

                if (targetType == typeof(DateTime))
                    return DateTime.Parse(valueStr);

                // 默认返回字符串
                return valueStr;
            }
            catch
            {
                // 转换失败时返回字符串
                return valueStr;
            }
        }

        private void OnAnalyzeClick(object sender, RoutedEventArgs e)
        {
            // 从文本框更新路径并强制重新分析（会清空已有参数）
            SetWorkflowPathAndAnalyze(WorkflowPathTextBox.Text);
        }

        private void OnWorkflowPathChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _workflowFilePath = WorkflowPathTextBox.Text;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            IsOk = true;
            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            IsOk = false;
            Close();
        }

        /// <summary>
        /// 在当前行后添加参数
        /// </summary>
        private void OnAddArgumentClick(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            var item = button?.DataContext as WorkflowArgumentItem;

            int index = Arguments.IndexOf(item);
            if (index >= 0)
            {
                Arguments.Insert(index + 1, new WorkflowArgumentItem
                {
                    Name = $"arg{Arguments.Count + 1}",
                    TypeName = "String",
                    Type = typeof(string),
                    Direction = WorkflowWorkflowArgumentDirection.In,
                    Value = "",
                    IsReadOnly = false
                });
            }
        }

        /// <summary>
        /// 删除参数
        /// </summary>
        private void OnDeleteArgumentClick(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            var item = button?.DataContext as WorkflowArgumentItem;

            if (item != null && Arguments.Contains(item))
            {
                Arguments.Remove(item);
            }
        }

        /// <summary>
        /// 添加新参数
        /// </summary>
        private void OnAddNewArgumentClick(object sender, RoutedEventArgs e)
        {
            Arguments.Add(new WorkflowArgumentItem
            {
                Name = $"arg{Arguments.Count + 1}",
                TypeName = "String",
                Type = typeof(string),
                Direction = WorkflowWorkflowArgumentDirection.In,
                Value = "",
                IsReadOnly = false
            });
        }

        /// <summary>
        /// 获取所有 In 参数的 VB 表达式（用于 Arguments 属性）
        /// </summary>
        public string GetInArgumentsVBExpression()
        {
            var inArgs = Arguments.Where(a => a.Direction == WorkflowWorkflowArgumentDirection.In || a.Direction == WorkflowWorkflowArgumentDirection.InOut).ToList();

            if (inArgs.Count == 0)
            {
                return "Nothing";
            }

            var items = new List<string>();
            foreach (var argItem in inArgs)
            {
                if (string.IsNullOrWhiteSpace(argItem.Value))
                {
                    continue;
                }

                string valueStr = FormatValueForVB(argItem.Value, argItem.Type);
                items.Add("{\"" + argItem.Name + "\", " + valueStr + "}");
            }

            if (items.Count == 0)
            {
                return "Nothing";
            }

            return "New Dictionary(Of String, Object) From {" + string.Join(", ", items) + "}";
        }

        /// <summary>
        /// 获取所有 Out 参数（用于绑定输出）
        /// </summary>
        public List<WorkflowArgumentItem> GetOutArguments()
        {
            return Arguments.Where(a => a.Direction == WorkflowWorkflowArgumentDirection.Out || a.Direction == WorkflowWorkflowArgumentDirection.InOut).ToList();
        }

        /// <summary>
        /// 获取所有 Out 参数的 VB 表达式（用于 OutputBindings 属性）
        /// 格式: New Dictionary(Of String, String) From {{"a", "variable2"}, {"b", "variable3"}}
        /// </summary>
        public string GetOutArgumentsVBExpression()
        {
            var outArgs = Arguments.Where(a =>
                (a.Direction == WorkflowWorkflowArgumentDirection.Out || a.Direction == WorkflowWorkflowArgumentDirection.InOut)
                && !string.IsNullOrWhiteSpace(a.Value)).ToList();

            if (outArgs.Count == 0)
            {
                return "Nothing";
            }

            var items = new List<string>();
            foreach (var argItem in outArgs)
            {
                // Out 参数的 Value 是变量名，直接作为字符串保存
                items.Add("{\"" + argItem.Name + "\", \"" + argItem.Value + "\"}");
            }

            if (items.Count == 0)
            {
                return "Nothing";
            }

            return "New Dictionary(Of String, String) From {" + string.Join(", ", items) + "}";
        }

        /// <summary>
        /// 从 Out 参数的 VB 表达式加载（用于还原已保存的绑定）
        /// </summary>
        public void LoadOutArgumentsFromVBExpression(string vbExpression)
        {
            if (string.IsNullOrWhiteSpace(vbExpression) || vbExpression == "Nothing")
                return;

            try
            {
                // 解析 VB 表达式: New Dictionary(Of String, String) From {{"a", "variable2"}, {"b", "variable3"}}
                var startIndex = vbExpression.IndexOf("From {");
                if (startIndex < 0) return;

                var content = vbExpression.Substring(startIndex + 6);
                var endIndex = content.LastIndexOf("}");
                if (endIndex < 0) return;

                content = content.Substring(0, endIndex).Trim();

                // 分割键值对
                var pairs = SplitDictionaryPairs(content);

                foreach (var pair in pairs)
                {
                    var parts = SplitKeyValue(pair);
                    if (parts.Length == 2)
                    {
                        var argName = parts[0].Trim().Trim('"');
                        var varName = parts[1].Trim().Trim('"');

                        // 查找对应的 Out 参数并设置值
                        var argItem = Arguments.FirstOrDefault(a => a.Name == argName &&
                            (a.Direction == WorkflowWorkflowArgumentDirection.Out || a.Direction == WorkflowWorkflowArgumentDirection.InOut));
                        if (argItem != null)
                        {
                            argItem.Value = varName;
                        }
                    }
                }
            }
            catch
            {
                // 解析失败，忽略
            }
        }

        /// <summary>
        /// 解析工作流路径（支持相对路径和绝对路径）
        /// </summary>
        private string ResolveWorkflowPath(string workflowPath)
        {
            // 如果是绝对路径，直接返回
            if (Path.IsPathRooted(workflowPath))
            {
                return workflowPath;
            }

            // 尝试多个可能的基础路径
            var searchPaths = GetSearchPaths(workflowPath);

            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            // 都找不到，返回第一个尝试的路径（用于错误提示）
            return searchPaths.FirstOrDefault() ?? workflowPath;
        }

        /// <summary>
        /// 获取可能的搜索路径列表
        /// </summary>
        private List<string> GetSearchPaths(string workflowPath)
        {
            var paths = new List<string>();
            var currentDir = Directory.GetCurrentDirectory();

            // 1. 当前工作目录（通常是 bin\Debug）
            paths.Add(Path.Combine(currentDir, workflowPath));

            // 2. 项目根目录（向上查找，通常在 bin\Debug 的上两级）
            var projectRoot = FindProjectRoot(currentDir);
            if (projectRoot != null)
            {
                paths.Add(Path.Combine(projectRoot, workflowPath));
            }

            // 3. 解决方案目录（查找 .sln 文件）
            var solutionRoot = FindSolutionRoot(currentDir);
            if (solutionRoot != null && solutionRoot != projectRoot)
            {
                paths.Add(Path.Combine(solutionRoot, workflowPath));
            }

            // 4. 相对于 bin\Debug 的上级目录
            var binParent = Directory.GetParent(currentDir)?.Parent?.FullName;
            if (binParent != null)
            {
                paths.Add(Path.Combine(binParent, workflowPath));
            }

            return paths.Distinct().ToList();
        }

        /// <summary>
        /// 查找项目根目录（包含 .csproj 文件的目录）
        /// </summary>
        private string FindProjectRoot(string startPath)
        {
            var dir = new DirectoryInfo(startPath);

            while (dir != null)
            {
                // 检查是否包含 .csproj 文件
                if (dir.GetFiles("*.csproj").Length > 0)
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            return null;
        }

        /// <summary>
        /// 查找解决方案根目录（包含 .sln 文件的目录）
        /// </summary>
        private string FindSolutionRoot(string startPath)
        {
            var dir = new DirectoryInfo(startPath);

            while (dir != null)
            {
                // 检查是否包含 .sln 文件
                if (dir.GetFiles("*.sln").Length > 0)
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            return null;
        }
    }

    /// <summary>
    /// 工作流参数方向枚举
    /// </summary>
    public enum WorkflowWorkflowArgumentDirection
    {
        In,
        Out,
        InOut
    }

    /// <summary>
    /// 工作流参数项
    /// </summary>
    public class WorkflowArgumentItem : INotifyPropertyChanged
    {
        private string _name;
        private Type _type;
        private string _typeName;
        private string _value;
        private WorkflowWorkflowArgumentDirection _direction;
        private bool _isReadOnly;

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public Type Type
        {
            get => _type;
            set
            {
                _type = value;
                OnPropertyChanged(nameof(Type));
            }
        }

        public string TypeName
        {
            get => _typeName;
            set
            {
                _typeName = value;
                OnPropertyChanged(nameof(TypeName));
            }
        }

        public string Value
        {
            get => _value;
            set
            {
                _value = value;
                OnPropertyChanged(nameof(Value));
            }
        }

        public WorkflowWorkflowArgumentDirection Direction
        {
            get => _direction;
            set
            {
                _direction = value;
                OnPropertyChanged(nameof(Direction));
            }
        }

        /// <summary>
        /// 参数名是否只读（从工作流定义读取的参数名不可修改）
        /// </summary>
        public bool IsReadOnly
        {
            get => _isReadOnly;
            set
            {
                _isReadOnly = value;
                OnPropertyChanged(nameof(IsReadOnly));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
