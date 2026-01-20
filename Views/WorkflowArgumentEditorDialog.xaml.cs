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
        /// 设置工作流文件路径并分析参数
        /// </summary>
        public void SetWorkflowPath(string workflowFilePath)
        {
            _workflowFilePath = workflowFilePath;
            WorkflowPathTextBox.Text = workflowFilePath;

            // 如果路径有效，自动分析参数
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

            // 调试：确认方法被调用
            //MessageBox.Show($"LoadFromVBExpression 被调用!\n\n表达式: {vbExpression}\n\nArguments 数量: {Arguments.Count}",
            //    "调试 - 方法入口", MessageBoxButton.OK, MessageBoxImage.Information);

            try
            {
                // 简单解析 VB 表达式
                // 格式: New Dictionary(Of String, Object) From {{"key1", value1}, {"key2", value2}}

                System.Diagnostics.Debug.WriteLine($"[LoadFromVBExpression] 原始表达式: {vbExpression}");
                System.Diagnostics.Debug.WriteLine($"[LoadFromVBExpression] Arguments.Count: {Arguments.Count}");

                var startIndex = vbExpression.IndexOf("From {");
                if (startIndex < 0)
                {
                    System.Diagnostics.Debug.WriteLine("[LoadFromVBExpression] 找不到 'From {' ");
                    return;
                }

                var content = vbExpression.Substring(startIndex + 6); // 跳过 "From {"
                var endIndex = content.LastIndexOf("}");
                if (endIndex < 0)
                {
                    System.Diagnostics.Debug.WriteLine("[LoadFromVBExpression] 找不到结束 '}' ");
                    return;
                }

                content = content.Substring(0, endIndex).Trim();
                System.Diagnostics.Debug.WriteLine($"[LoadFromVBExpression] 提取内容: {content}");

                // 分割键值对
                var pairs = SplitDictionaryPairs(content);
                System.Diagnostics.Debug.WriteLine($"[LoadFromVBExpression] 分割后的键值对数量: {pairs.Count}");

                int matchedCount = 0;
                foreach (var pair in pairs)
                {
                    var parts = SplitKeyValue(pair);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim().Trim('"');
                        var value = parts[1].Trim();

                        System.Diagnostics.Debug.WriteLine($"[LoadFromVBExpression] 处理键值对: key='{key}', value='{value}'");

                        // 查找对应的参数项
                        var argItem = Arguments.FirstOrDefault(a => a.Name == key);
                        if (argItem != null)
                        {
                            // 还原值
                            if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length > 1)
                            {
                                // 字符串字面量：保留引号,这样用户看到的就是他们输入的
                                // 例如用户输入 "123"，保存为 "123"，还原后显示 "123"
                                var innerValue = value.Substring(1, value.Length - 2).Replace("\"\"", "\"");
                                argItem.Value = "\"" + innerValue + "\"";
                                System.Diagnostics.Debug.WriteLine($"[LoadFromVBExpression] 设置字符串值: '{argItem.Value}'");
                            }
                            else
                            {
                                // 变量引用或数字,直接还原
                                argItem.Value = value;
                                System.Diagnostics.Debug.WriteLine($"[LoadFromVBExpression] 设置变量/数字值: '{argItem.Value}'");
                            }
                            matchedCount++;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[LoadFromVBExpression] 未找到匹配的参数项: '{key}'");
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[LoadFromVBExpression] 成功匹配并还原 {matchedCount} 个参数值");

                // 如果没有匹配任何参数，显示调试信息
                if (matchedCount == 0 && pairs.Count > 0)
                {
                    var availableNames = string.Join(", ", Arguments.Select(a => $"'{a.Name}'"));
                    MessageBox.Show($"VB 表达式解析失败 - 没有匹配到任何参数:\n\n" +
                        $"表达式: {vbExpression}\n\n" +
                        $"提取内容: {content}\n\n" +
                        $"键值对数量: {pairs.Count}\n" +
                        $"Arguments 数量: {Arguments.Count}\n" +
                        $"可用参数名: {availableNames}\n\n" +
                        $"解析的键值对:\n{string.Join("\n", pairs.Select((p, i) => $"  [{i}] {p}"))}",
                        "调试信息", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                // 显示错误信息以便调试
                MessageBox.Show($"解析 VB 表达式时发生错误:\n\n{ex.Message}\n\n{ex.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"[LoadFromVBExpression] 异常: {ex.Message}");
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
                var parameters = executeMethod.GetParameters();
                foreach (var param in parameters)
                {
                    Arguments.Add(new WorkflowArgumentItem
                    {
                        Name = param.Name,
                        Type = param.ParameterType,
                        TypeName = GetFriendlyTypeName(param.ParameterType),
                        Value = ""
                    });
                }
            }

            // 如果没有找到参数，尝试通过 Arguments 属性访问
            // CodedWorkflowBase 有一个 Arguments 字典属性
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
                Arguments.Add(new WorkflowArgumentItem
                {
                    Name = arg.Name,
                    Type = arg.Type,
                    TypeName = GetFriendlyTypeName(arg.Type),
                    Value = ""
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
            AnalyzeWorkflowArguments();
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
    /// 工作流参数项
    /// </summary>
    public class WorkflowArgumentItem : INotifyPropertyChanged
    {
        private string _name;
        private Type _type;
        private string _typeName;
        private string _value;

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

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
