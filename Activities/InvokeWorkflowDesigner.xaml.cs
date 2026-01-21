using ActiproRoslynPOC.Views;
using System;
using System.Activities;
using System.Activities.Presentation;
using System.Activities.Presentation.Model;
using System.Collections.Generic;
using System.Windows;

namespace ActiproRoslynPOC.Activities
{
    /// <summary>
    /// InvokeWorkflow 的自定义设计器
    /// 提供友好的参数输入界面
    /// </summary>
    public partial class InvokeWorkflowDesigner : ActivityDesigner
    {
        public InvokeWorkflowDesigner()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 打开参数编辑对话框
        /// </summary>
        private void OnEditArgumentsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // 获取工作流文件路径
                string workflowPath = null;

                try
                {
                    // 方法1: 尝试从 ComputedValue 获取
                    var pathValue = this.ModelItem.Properties["WorkflowFilePath"].ComputedValue;
                    if (pathValue != null)
                    {
                        workflowPath = pathValue.ToString();
                    }
                }
                catch
                {
                    // 方法2: 尝试从 Value 的表达式文本获取
                    try
                    {
                        var pathProperty = this.ModelItem.Properties["WorkflowFilePath"];
                        if (pathProperty?.Value != null)
                        {
                            var propValue = pathProperty.Value;
                            // 检查是否是 VisualBasicValue
                            var exprTextProp = propValue.GetType().GetProperty("ExpressionText");
                            if (exprTextProp != null)
                            {
                                var exprText = exprTextProp.GetValue(propValue)?.ToString();
                                if (!string.IsNullOrWhiteSpace(exprText))
                                {
                                    // 移除引号
                                    workflowPath = exprText.Trim('"');
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (string.IsNullOrWhiteSpace(workflowPath))
                {
                    MessageBox.Show("请先在 '工作流文件' 字段中输入工作流文件路径", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 创建对话框
                var dialog = new WorkflowArgumentEditorDialog
                {
                    Owner = Window.GetWindow(this)
                };

                // 设置工作流路径并分析参数
                if (!string.IsNullOrWhiteSpace(workflowPath))
                {
                    dialog.SetWorkflowPath(workflowPath);
                }

                // 尝试从 VB 表达式加载已有参数值
                try
                {
                    var argumentsProperty = this.ModelItem.Properties["Arguments"];
                    if (argumentsProperty?.Value != null)
                    {
                        // argumentsProperty.Value 是 ModelItem,需要获取其 Properties["Expression"]
                        var modelItem = argumentsProperty.Value;
                        var expressionProperty = modelItem.Properties["Expression"];

                        if (expressionProperty?.Value != null)
                        {
                            // 现在 expressionProperty.Value 应该是 VisualBasicValue<Dictionary<string, object>>
                            var expression = expressionProperty.Value;

                            // 从 ModelItem 中获取 ExpressionText 属性
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
                catch (Exception ex)
                {
                    // 如果上面的方法失败，尝试从 ComputedValue 加载
                    try
                    {
                        var currentArguments = this.ModelItem.Properties["Arguments"].ComputedValue as Dictionary<string, object>;
                        dialog.LoadArgumentValues(currentArguments);
                    }
                    catch { }
                }

                // 显示对话框
                if (dialog.ShowDialog() == true && dialog.IsOk)
                {
                    // 生成 VB.NET 表达式并设置
                    string vbExpression = dialog.ToVBExpression();

                    using (ModelEditingScope scope = this.ModelItem.BeginEdit("Edit Arguments"))
                    {
                        // 创建 InArgument 包装 VisualBasicValue
                        var vbValue = new Microsoft.VisualBasic.Activities.VisualBasicValue<Dictionary<string, object>>(vbExpression);
                        var inArgument = new InArgument<Dictionary<string, object>>(vbValue);

                        this.ModelItem.Properties["Arguments"].SetValue(inArgument);
                        scope.Complete();
                    }
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"打开参数编辑器失败: {ex.Message}\n\n{ex.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
