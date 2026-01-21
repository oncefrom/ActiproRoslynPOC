using System.Activities.Presentation;
using System.Activities.Presentation.Toolbox;
using System.Activities.Statements;
using System.Windows;
using System.Windows.Controls;

namespace ActiproRoslynPOC
{
    /// <summary>
    /// 工作流设计器增强辅助类
    /// 提供属性面板、变量面板等功能
    /// </summary>
    public static class WorkflowDesignerHelper
    {
        /// <summary>
        /// 创建增强的工作流设计器布局，包含工具箱、设计器视图、属性面板
        /// </summary>
        /// <param name="designer">WorkflowDesigner 实例</param>
        /// <returns>包含完整布局的 UI 控件</returns>
        public static UIElement CreateEnhancedDesignerLayout(WorkflowDesigner designer)
        {
            // 创建工具箱
            var toolboxControl = CreateToolbox();

            // 使用 Grid 进行布局 (左侧工具箱，中间设计器，右侧属性面板)
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });  // 工具箱
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // 设计器
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });  // 属性面板

            // 添加工具箱到第一列
            Grid.SetColumn(toolboxControl, 0);
            grid.Children.Add(toolboxControl);

            // 添加设计器视图到第二列
            Grid.SetColumn(designer.View, 1);
            grid.Children.Add(designer.View);

            // 添加属性面板到第三列 (WorkflowDesigner 内置的 PropertyInspectorView)
            var propertyInspector = designer.PropertyInspectorView;
            Grid.SetColumn(propertyInspector, 2);
            grid.Children.Add(propertyInspector);

            return grid;
        }

        /// <summary>
        /// 创建一个新的 Sequence 作为根活动
        /// </summary>
        /// <returns>Sequence 活动</returns>
        public static System.Activities.Activity CreateNewSequenceWorkflow()
        {
            return new Sequence();
        }

        /// <summary>
        /// 创建工具箱控件
        /// </summary>
        private static ToolboxControl CreateToolbox()
        {
            var toolbox = new ToolboxControl();

            // 控制流分类
            var controlFlowCategory = new ToolboxCategory("控制流");
            controlFlowCategory.Add(new ToolboxItemWrapper(typeof(Sequence), "Sequence"));
            controlFlowCategory.Add(new ToolboxItemWrapper(typeof(Flowchart), "Flowchart"));
            controlFlowCategory.Add(new ToolboxItemWrapper(typeof(If), "If"));
            controlFlowCategory.Add(new ToolboxItemWrapper(typeof(While), "While"));
            controlFlowCategory.Add(new ToolboxItemWrapper(typeof(ForEach<>), "ForEach<T>"));
            controlFlowCategory.Add(new ToolboxItemWrapper(typeof(Parallel), "Parallel"));
            controlFlowCategory.Add(new ToolboxItemWrapper(typeof(Pick), "Pick"));
            toolbox.Categories.Add(controlFlowCategory);

            // 基本活动分类
            var primitivesCategory = new ToolboxCategory("基本活动");
            primitivesCategory.Add(new ToolboxItemWrapper(typeof(Assign<>), "Assign<T>"));
            primitivesCategory.Add(new ToolboxItemWrapper(typeof(Delay), "Delay"));
            primitivesCategory.Add(new ToolboxItemWrapper(typeof(WriteLine), "WriteLine"));
            primitivesCategory.Add(new ToolboxItemWrapper(typeof(InvokeMethod), "InvokeMethod"));
            toolbox.Categories.Add(primitivesCategory);

            // 自定义工作流活动分类
            var customCategory = new ToolboxCategory("工作流调用");
            customCategory.Add(new ToolboxItemWrapper(
                typeof(Activities.InvokeCodedWorkflow),
                "InvokeCodedWorkflow",
                "调用 C# 工作流"));
            customCategory.Add(new ToolboxItemWrapper(
                typeof(Activities.InvokeWorkflow),
                "InvokeWorkflow",
                "调用 XAML 工作流"));
            toolbox.Categories.Add(customCategory);

            return toolbox;
        }
    }
}
