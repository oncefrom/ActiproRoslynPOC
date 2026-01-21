using ActiproRoslynPOC.Views;
using System;
using System.Activities;
using System.Activities.Presentation;
using System.Activities.Presentation.Model;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace ActiproRoslynPOC.Activities
{
    /// <summary>
    /// InvokeCodedWorkflow 的自定义设计器
    /// 提供友好的参数输入界面（类似 UiPath 风格）
    /// </summary>
    public partial class InvokeCodedWorkflowDesigner : ActivityDesigner
    {
        // 保存当前参数列表，用于在设计器中预览
        private List<WorkflowArgumentItem> _currentArguments = new List<WorkflowArgumentItem>();

        public InvokeCodedWorkflowDesigner()
        {
            InitializeComponent();
            Loaded += OnDesignerLoaded;
        }

        private void OnDesignerLoaded(object sender, RoutedEventArgs e)
        {
            UpdateArgumentsPreview();
        }

        /// <summary>
        /// 更新参数预览显示
        /// </summary>
        private void UpdateArgumentsPreview()
        {
            try
            {
                if (_currentArguments.Count == 0)
                {
                    // 尝试从已保存的 VB 表达式中加载参数
                    LoadArgumentsFromExpression();
                }

                if (_currentArguments.Count > 0)
                {
                    var sb = new StringBuilder();
                    foreach (var arg in _currentArguments)
                    {
                        var directionStr = arg.Direction == WorkflowWorkflowArgumentDirection.In ? "→" :
                                          arg.Direction == WorkflowWorkflowArgumentDirection.Out ? "←" : "↔";
                        sb.AppendLine($"{directionStr} {arg.Name} ({arg.TypeName}): {arg.Value}");
                    }
                    ArgumentsPreviewTextBlock.Text = sb.ToString().TrimEnd();
                    ArgumentsPreviewTextBlock.Foreground = System.Windows.Media.Brushes.Black;
                }
                else
                {
                    ArgumentsPreviewTextBlock.Text = "(点击上方按钮编辑参数)";
                    ArgumentsPreviewTextBlock.Foreground = System.Windows.Media.Brushes.Gray;
                }
            }
            catch
            {
                // 忽略预览错误
            }
        }

        /// <summary>
        /// 从已保存的 VB 表达式加载参数（用于重新打开设计器时恢复）
        /// </summary>
        private void LoadArgumentsFromExpression()
        {
            try
            {
                var argumentsProperty = this.ModelItem.Properties["Arguments"];
                if (argumentsProperty?.Value != null)
                {
                    var modelItem = argumentsProperty.Value;
                    var expressionProperty = modelItem.Properties["Expression"];

                    if (expressionProperty?.Value != null)
                    {
                        var expression = expressionProperty.Value;
                        var exprTextProperty = expression.Properties["ExpressionText"];
                        if (exprTextProperty?.ComputedValue != null)
                        {
                            var vbExpression = exprTextProperty.ComputedValue.ToString();
                            if (!string.IsNullOrWhiteSpace(vbExpression) && vbExpression != "Nothing")
                            {
                                // 简单解析以显示预览
                                ParseVBExpressionForPreview(vbExpression);
                            }
                        }
                    }
                }
            }
            catch
            {
                // 忽略加载错误
            }
        }

        /// <summary>
        /// 简单解析 VB 表达式用于预览显示
        /// </summary>
        private void ParseVBExpressionForPreview(string vbExpression)
        {
            _currentArguments.Clear();

            try
            {
                var startIndex = vbExpression.IndexOf("From {");
                if (startIndex < 0) return;

                var content = vbExpression.Substring(startIndex + 6);
                var endIndex = content.LastIndexOf("}");
                if (endIndex < 0) return;

                content = content.Substring(0, endIndex).Trim();

                // 简单的键值对解析
                int braceLevel = 0;
                int pairStart = 0;

                for (int i = 0; i < content.Length; i++)
                {
                    if (content[i] == '{')
                    {
                        if (braceLevel == 0) pairStart = i;
                        braceLevel++;
                    }
                    else if (content[i] == '}')
                    {
                        braceLevel--;
                        if (braceLevel == 0)
                        {
                            var pair = content.Substring(pairStart + 1, i - pairStart - 1).Trim();
                            var commaIndex = pair.IndexOf(',');
                            if (commaIndex > 0)
                            {
                                var key = pair.Substring(0, commaIndex).Trim().Trim('"');
                                var value = pair.Substring(commaIndex + 1).Trim();

                                _currentArguments.Add(new WorkflowArgumentItem
                                {
                                    Name = key,
                                    Value = value,
                                    TypeName = "Object",
                                    Direction = WorkflowWorkflowArgumentDirection.In
                                });
                            }
                        }
                    }
                }
            }
            catch
            {
                // 解析失败则忽略
            }
        }

        /// <summary>
        /// 打开参数编辑对话框
        /// </summary>
        private void OnEditArgumentsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // 获取工作流文件路径
                string workflowPath = GetWorkflowFilePath();

                // 创建对话框
                var dialog = new WorkflowArgumentEditorDialog
                {
                    Owner = Window.GetWindow(this)
                };

                // 先设置工作流路径并自动分析参数定义
                if (!string.IsNullOrWhiteSpace(workflowPath))
                {
                    dialog.SetWorkflowPath(workflowPath);
                }

                // 再加载已有参数值（这会把保存的值填充到已分析的参数中）
                LoadExistingArguments(dialog);

                // 显示对话框
                if (dialog.ShowDialog() == true && dialog.IsOk)
                {
                    // 保存参数到 Activity
                    SaveArguments(dialog);

                    // 更新预览
                    _currentArguments = dialog.Arguments.ToList();
                    UpdateArgumentsPreview();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开参数编辑器失败: {ex.Message}\n\n{ex.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 获取工作流文件路径
        /// </summary>
        private string GetWorkflowFilePath()
        {
            try
            {
                var pathProperty = this.ModelItem.Properties["WorkflowFilePath"];
                if (pathProperty?.Value != null)
                {
                    // 获取 InArgument 的 Expression 属性的 ComputedValue
                    var exprModelProperty = pathProperty.Value.Properties["Expression"];
                    var text = exprModelProperty?.ComputedValue?.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim('"');
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// 加载已有的参数值到对话框
        /// </summary>
        private void LoadExistingArguments(WorkflowArgumentEditorDialog dialog)
        {
            // 加载 In 参数
            try
            {
                var argumentsProperty = this.ModelItem.Properties["Arguments"];
                if (argumentsProperty?.Value != null)
                {
                    var modelItem = argumentsProperty.Value;
                    var expressionProperty = modelItem.Properties["Expression"];

                    if (expressionProperty?.Value != null)
                    {
                        var expression = expressionProperty.Value;
                        var exprTextProperty = expression.Properties["ExpressionText"];
                        if (exprTextProperty?.ComputedValue != null)
                        {
                            var vbExpression = exprTextProperty.ComputedValue.ToString();
                            if (!string.IsNullOrWhiteSpace(vbExpression))
                            {
                                dialog.LoadFromVBExpression(vbExpression);
                            }
                        }
                    }
                }
            }
            catch { }

            // 加载 Out 参数绑定
            try
            {
                var outputBindingsProperty = this.ModelItem.Properties["OutputBindings"];
                if (outputBindingsProperty?.Value != null)
                {
                    var modelItem = outputBindingsProperty.Value;
                    var expressionProperty = modelItem.Properties["Expression"];

                    if (expressionProperty?.Value != null)
                    {
                        var expression = expressionProperty.Value;
                        var exprTextProperty = expression.Properties["ExpressionText"];
                        if (exprTextProperty?.ComputedValue != null)
                        {
                            var vbExpression = exprTextProperty.ComputedValue.ToString();
                            if (!string.IsNullOrWhiteSpace(vbExpression))
                            {
                                dialog.LoadOutArgumentsFromVBExpression(vbExpression);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 保存参数到 Activity
        /// </summary>
        private void SaveArguments(WorkflowArgumentEditorDialog dialog)
        {
            // 获取 In 参数的 VB 表达式
            string inArgsExpression = dialog.GetInArgumentsVBExpression();
            // 获取 Out 参数绑定的 VB 表达式
            string outArgsExpression = dialog.GetOutArgumentsVBExpression();

            using (ModelEditingScope scope = this.ModelItem.BeginEdit("Edit Arguments"))
            {
                // 保存 In 参数
                if (inArgsExpression == "Nothing" || string.IsNullOrWhiteSpace(inArgsExpression))
                {
                    this.ModelItem.Properties["Arguments"].SetValue(null);
                }
                else
                {
                    var vbValue = new Microsoft.VisualBasic.Activities.VisualBasicValue<Dictionary<string, object>>(inArgsExpression);
                    var inArgument = new InArgument<Dictionary<string, object>>(vbValue);
                    this.ModelItem.Properties["Arguments"].SetValue(inArgument);
                }

                // 保存 Out 参数绑定
                if (outArgsExpression == "Nothing" || string.IsNullOrWhiteSpace(outArgsExpression))
                {
                    this.ModelItem.Properties["OutputBindings"].SetValue(null);
                }
                else
                {
                    var vbValue = new Microsoft.VisualBasic.Activities.VisualBasicValue<Dictionary<string, string>>(outArgsExpression);
                    var inArgument = new InArgument<Dictionary<string, string>>(vbValue);
                    this.ModelItem.Properties["OutputBindings"].SetValue(inArgument);
                }

                scope.Complete();
            }
        }
    }
}
