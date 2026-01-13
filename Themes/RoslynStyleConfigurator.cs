using ActiproSoftware.Text;
using ActiproSoftware.Text.Implementation;
using ActiproSoftware.Text.Languages.DotNet.Implementation;
using ActiproSoftware.Windows.Controls.SyntaxEditor;
using ActiproSoftware.Windows.Controls.SyntaxEditor.Highlighting;
using ActiproSoftware.Windows.Controls.SyntaxEditor.Highlighting.Implementation;
using System.Windows.Media;
using System.IO;
using System;

namespace ActiproRoslynPOC.Themes
{
    /// <summary>
    /// Roslyn 样式配置器 - 使用 Actipro 官方推荐的方式配置语法高亮
    /// </summary>
    public static class RoslynStyleConfigurator
    {
        private static ThemeAwareColorCache _colorCache = new ThemeAwareColorCache();
        private static bool _isInitialized = false;

        // 自定义分类类型
        private static IClassificationType _usedMethodName;
        private static IClassificationType _classObjectReference;
        private static IClassificationType _interfaceObjectReference;
        private static IClassificationType _structureObjectReference;
        private static IClassificationType _parametersObjectReference;

        /// <summary>
        /// 初始化语法高亮配置（只初始化一次）
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized)
                return;

            System.Diagnostics.Debug.WriteLine("[RoslynStyleConfigurator] 开始初始化");

            // 创建自定义分类类型（使用简单的字符串键）
            _usedMethodName = new ClassificationType("Method Name", "Method Name");
            _classObjectReference = new ClassificationType("Class Name", "Class Name");
            _interfaceObjectReference = new ClassificationType("Interface Name", "Interface Name");
            _structureObjectReference = new ClassificationType("Struct Name", "Struct Name");
            _parametersObjectReference = new ClassificationType("Parameter Name", "Parameter Name");

            // 使用 AmbientHighlightingStyleRegistry
            var registry = AmbientHighlightingStyleRegistry.Instance;

            // 【调试】先让 DotNetClassificationTypeProvider 注册所有内置分类类型
            var dotNetProvider = new DotNetClassificationTypeProvider();
            dotNetProvider.RegisterAll();

            // 【调试】打印所有已注册的分类类型
            System.Diagnostics.Debug.WriteLine("[RoslynStyleConfigurator] ===== 已注册的分类类型 =====");
            foreach (var ct in registry.ClassificationTypes)
            {
                System.Diagnostics.Debug.WriteLine($"[RoslynStyleConfigurator] Key: '{ct.Key}', Description: '{ct.Description}'");
            }
            System.Diagnostics.Debug.WriteLine("[RoslynStyleConfigurator] ===== 分类类型列表结束 =====");

            // 只注册浅色主题颜色（默认）
            RegisterLightTheme(registry);

            // 注意：不要在初始化时同时注册深色主题，会导致颜色冲突
            // 主题切换应该通过 EditorThemeManager.ToggleTheme() 手动触发

            // 【关键】让 SyntaxEditorThemeManager 自动管理主题切换
            SyntaxEditorThemeManager.Manage(registry);

            _isInitialized = true;

            System.Diagnostics.Debug.WriteLine("[RoslynStyleConfigurator] 初始化完成（浅色主题），已启用 SyntaxEditorThemeManager");
        }


        /// <summary>
        /// 注册浅色主题
        /// </summary>
        private static void RegisterLightTheme(IHighlightingStyleRegistry registry)
        {
            var lightColors = _colorCache.GetLightThemeColors();

            // 基础分类类型
            registry.Register(ClassificationTypes.Keyword, new HighlightingStyle(lightColors["Keyword"]));
            registry.Register(ClassificationTypes.String, new HighlightingStyle(lightColors["String"]));
            registry.Register(ClassificationTypes.Comment, new HighlightingStyle(lightColors["Comment"]));
            registry.Register(ClassificationTypes.Number, new HighlightingStyle(lightColors["Number"]));
            //registry.Register(ClassificationTypes.Identifier, new HighlightingStyle(lightColors["Identifier"]));
            registry.Register(ClassificationTypes.Operator, new HighlightingStyle(lightColors["Operator"]));
            registry.Register(ClassificationTypes.PreprocessorKeyword, new HighlightingStyle(lightColors["PreprocessorKeyword"]));
            registry.Register(CustomClassificationTypes.UnnecessaryCode, new HighlightingStyle(lightColors["UnnecessaryCode"]));

            // Roslyn 语义分类类型 - 使用 Actipro 的内置 Roslyn 分类类型
            // 这些是 Roslyn 提供的语义分类，我们只需要为它们注册颜色
            var methodClassification = new ClassificationType("Method", "Method Name");
            var classClassification = new ClassificationType("Class", "Class Name");
            var interfaceClassification = new ClassificationType("Interface Name", "Interface Name");
            var structClassification = new ClassificationType("Struct Name", "Struct Name");
            var paramClassification = new ClassificationType("Parameter Name", "Parameter Name");
            var localVarClassification = new ClassificationType("Local Name", "Local Variable");
            
            registry.Register(methodClassification, new HighlightingStyle(lightColors["Identifier"]) { Bold = true });
            registry.Register(classClassification, new HighlightingStyle(lightColors["Identifier"]));
            registry.Register(interfaceClassification, new HighlightingStyle(lightColors["InterfaceObjectReference"]));
            registry.Register(structClassification, new HighlightingStyle(lightColors["StructureObjectReference"]));
            registry.Register(paramClassification, new HighlightingStyle(lightColors["ParametersObjectReference"]) { Bold = true });
            registry.Register(localVarClassification, new HighlightingStyle(lightColors["Identifier"]));

            // 自定义分类类型（供 SemanticClassificationTagger 使用）
            registry.Register(_usedMethodName, new HighlightingStyle(lightColors["UsedMethodName"]) { Bold = true });
            registry.Register(_classObjectReference, new HighlightingStyle(lightColors["ClassObjectReference"]));
            registry.Register(_interfaceObjectReference, new HighlightingStyle(lightColors["InterfaceObjectReference"]));
            registry.Register(_structureObjectReference, new HighlightingStyle(lightColors["StructureObjectReference"]));
            registry.Register(_parametersObjectReference, new HighlightingStyle(lightColors["ParametersObjectReference"]) { Bold = true });

            System.Diagnostics.Debug.WriteLine("[RoslynStyleConfigurator] 浅色主题已注册（包含 Roslyn 语义分类）");
        }

        /// <summary>
        /// 注册深色主题
        /// </summary>
        private static void RegisterDarkTheme(IHighlightingStyleRegistry registry)
        {
            var darkColors = _colorCache.GetDarkThemeColors();

            // 切换到深色调色板
            //registry.CurrentColorPalette = "Dark";

            // 基础分类类型
            registry.Register(ClassificationTypes.Keyword, new HighlightingStyle(darkColors["Keyword"]));
            registry.Register(ClassificationTypes.String, new HighlightingStyle(darkColors["String"]));
            registry.Register(ClassificationTypes.Comment, new HighlightingStyle(darkColors["Comment"]));
            registry.Register(ClassificationTypes.Number, new HighlightingStyle(darkColors["Number"]));
            registry.Register(ClassificationTypes.Identifier, new HighlightingStyle(darkColors["Identifier"]));
            registry.Register(ClassificationTypes.Operator, new HighlightingStyle(darkColors["Operator"]));
            registry.Register(ClassificationTypes.PreprocessorKeyword, new HighlightingStyle(darkColors["PreprocessorKeyword"]));
            registry.Register(CustomClassificationTypes.UnnecessaryCode, new HighlightingStyle(darkColors["UnnecessaryCode"]));

            // Roslyn 语义分类类型 - 使用 Actipro 的内置 Roslyn 分类类型
            var methodClassification = new ClassificationType("method name", "Method Name");
            var classClassification = new ClassificationType("class name", "Class Name");
            var interfaceClassification = new ClassificationType("interface name", "Interface Name");
            var structClassification = new ClassificationType("struct name", "Struct Name");
            var paramClassification = new ClassificationType("parameter name", "Parameter Name");
            var localVarClassification = new ClassificationType("local name", "Local Variable");

            registry.Register(methodClassification, new HighlightingStyle(darkColors["UsedMethodName"]) { Bold = true });
            registry.Register(classClassification, new HighlightingStyle(darkColors["ClassObjectReference"]));
            registry.Register(interfaceClassification, new HighlightingStyle(darkColors["InterfaceObjectReference"]));
            registry.Register(structClassification, new HighlightingStyle(darkColors["StructureObjectReference"]));
            registry.Register(paramClassification, new HighlightingStyle(darkColors["ParametersObjectReference"]) { Bold = true });
            registry.Register(localVarClassification, new HighlightingStyle(darkColors["Identifier"]));

            // 自定义分类类型（供 SemanticClassificationTagger 使用）
            registry.Register(_usedMethodName, new HighlightingStyle(darkColors["UsedMethodName"]) { Bold = true });
            registry.Register(_classObjectReference, new HighlightingStyle(darkColors["ClassObjectReference"]));
            registry.Register(_interfaceObjectReference, new HighlightingStyle(darkColors["InterfaceObjectReference"]));
            registry.Register(_structureObjectReference, new HighlightingStyle(darkColors["StructureObjectReference"]));
            registry.Register(_parametersObjectReference, new HighlightingStyle(darkColors["ParametersObjectReference"]) { Bold = true });

            // 切换回浅色调色板（默认）
            //registry.CurrentColorPalette = "Light";

            System.Diagnostics.Debug.WriteLine("[RoslynStyleConfigurator] 深色主题已注册（包含 Roslyn 语义分类）");
        }

        /// <summary>
        /// 切换到深色主题（供 EditorThemeManager 调用）
        /// </summary>
        public static void SwitchToDarkTheme()
        {
            var registry = AmbientHighlightingStyleRegistry.Instance;
            RegisterDarkTheme(registry);
            System.Diagnostics.Debug.WriteLine("[RoslynStyleConfigurator] 已切换到深色主题");
        }

        /// <summary>
        /// 切换到浅色主题（供 EditorThemeManager 调用）
        /// </summary>
        public static void SwitchToLightTheme()
        {
            var registry = AmbientHighlightingStyleRegistry.Instance;
            RegisterLightTheme(registry);
            System.Diagnostics.Debug.WriteLine("[RoslynStyleConfigurator] 已切换到浅色主题");
        }

        /// <summary>
        /// 获取自定义分类类型（供 tagger 使用）
        /// </summary>
        public static IClassificationType UsedMethodName => _usedMethodName;
        public static IClassificationType ClassObjectReference => _classObjectReference;
        public static IClassificationType InterfaceObjectReference => _interfaceObjectReference;
        public static IClassificationType StructureObjectReference => _structureObjectReference;
        public static IClassificationType ParametersObjectReference => _parametersObjectReference;
    }
}
