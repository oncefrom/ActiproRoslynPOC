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

            // 使用 AmbientHighlightingStyleRegistry
            var registry = AmbientHighlightingStyleRegistry.Instance;

            // 【调试】先让 DotNetClassificationTypeProvider 注册所有内置分类类型
            var dotNetProvider = new DotNetClassificationTypeProvider();
            dotNetProvider.RegisterAll();

            // 确保分类类型已创建（在 DotNetClassificationTypeProvider 之后）
            // 这样我们的自定义分类类型不会被覆盖
            EnsureClassificationTypesCreated();

            // 【调试】打印所有已注册的分类类型
            System.Diagnostics.Debug.WriteLine("[RoslynStyleConfigurator] ===== 已注册的分类类型 =====");
            foreach (var ct in registry.ClassificationTypes)
            {
                System.Diagnostics.Debug.WriteLine($"[RoslynStyleConfigurator] Key: '{ct.Key}', Description: '{ct.Description}'");
            }
            System.Diagnostics.Debug.WriteLine("[RoslynStyleConfigurator] ===== 分类类型列表结束 =====");

            // 注册浅色主题颜色（重新注册确保生效）
            RegisterLightTheme(registry);

            // 注意：不要在初始化时同时注册深色主题，会导致颜色冲突
            // 主题切换应该通过 EditorThemeManager.ToggleTheme() 手动触发

            // 【关键】让 SyntaxEditorThemeManager 自动管理主题切换
            SyntaxEditorThemeManager.Manage(registry);

            _isInitialized = true;

            // 验证我们的自定义分类类型样式
            System.Diagnostics.Debug.WriteLine("[RoslynStyleConfigurator] ===== 验证自定义分类类型样式 =====");
            VerifyCustomStyles(registry);
            System.Diagnostics.Debug.WriteLine("[RoslynStyleConfigurator] 初始化完成（浅色主题），已启用 SyntaxEditorThemeManager");
        }

        /// <summary>
        /// 验证自定义分类类型的样式是否正确注册
        /// </summary>
        private static void VerifyCustomStyles(IHighlightingStyleRegistry registry)
        {
            var types = new[] { _usedMethodName, _classObjectReference, _interfaceObjectReference, _structureObjectReference, _parametersObjectReference };
            var names = new[] { "UsedMethodName", "ClassObjectReference", "InterfaceObjectReference", "StructureObjectReference", "ParametersObjectReference" };

            for (int i = 0; i < types.Length; i++)
            {
                var ct = types[i];
                if (ct == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[RoslynStyleConfigurator] {names[i]}: 分类类型为 null!");
                    continue;
                }

                var style = registry[ct];
                if (style == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[RoslynStyleConfigurator] {names[i]} (Key='{ct.Key}'): 样式未找到!");
                }
                else
                {
                    var fg = style.Foreground.HasValue ? style.Foreground.Value.ToString() : "null";
                    var bold = style.Bold.HasValue ? style.Bold.Value.ToString() : "null";
                    System.Diagnostics.Debug.WriteLine($"[RoslynStyleConfigurator] {names[i]} (Key='{ct.Key}'): FG={fg}, Bold={bold}");
                }
            }
        }


        /// <summary>
        /// 注册浅色主题
        /// </summary>
        private static void RegisterLightTheme(IHighlightingStyleRegistry registry)
        {
            var lightColors = _colorCache.GetLightThemeColors();

            // 基础分类类型 - 使用 overwriteExisting: true 确保覆盖
            registry.Register(ClassificationTypes.Keyword, new HighlightingStyle(lightColors["Keyword"]), true);
            registry.Register(ClassificationTypes.String, new HighlightingStyle(lightColors["String"]), true);
            registry.Register(ClassificationTypes.Comment, new HighlightingStyle(lightColors["Comment"]), true);
            registry.Register(ClassificationTypes.Number, new HighlightingStyle(lightColors["Number"]), true);
            registry.Register(ClassificationTypes.Identifier, new HighlightingStyle(lightColors["Identifier"]), true);
            registry.Register(ClassificationTypes.Operator, new HighlightingStyle(lightColors["Operator"]), true);
            registry.Register(ClassificationTypes.PreprocessorKeyword, new HighlightingStyle(lightColors["PreprocessorKeyword"]), true);
            registry.Register(CustomClassificationTypes.UnnecessaryCode, new HighlightingStyle(lightColors["UnnecessaryCode"]), true);

            // 高亮样式 - 使用背景色
            registry.Register(CustomClassificationTypes.SelectionMatchHighlight,
                new HighlightingStyle() { Background = lightColors["SelectionMatchHighlight"] }, true);
            registry.Register(CustomClassificationTypes.ReferenceHighlight,
                new HighlightingStyle() { Background = lightColors["ReferenceHighlight"] }, true);

            // 自定义分类类型（供 RoslynSemanticClassificationTagger 使用）
            // 使用正确的颜色配置，使用 overwriteExisting: true 确保覆盖
            registry.Register(_usedMethodName, new HighlightingStyle(lightColors["UsedMethodName"]) { Bold = true }, true);
            registry.Register(_classObjectReference, new HighlightingStyle(lightColors["ClassObjectReference"]), true);
            registry.Register(_interfaceObjectReference, new HighlightingStyle(lightColors["InterfaceObjectReference"]), true);
            registry.Register(_structureObjectReference, new HighlightingStyle(lightColors["StructureObjectReference"]), true);
            registry.Register(_parametersObjectReference, new HighlightingStyle(lightColors["ParametersObjectReference"]) { Bold = true }, true);

            System.Diagnostics.Debug.WriteLine("[RoslynStyleConfigurator] 浅色主题已注册（包含 Roslyn 语义分类）");
        }

        /// <summary>
        /// 注册深色主题
        /// </summary>
        private static void RegisterDarkTheme(IHighlightingStyleRegistry registry)
        {
            var darkColors = _colorCache.GetDarkThemeColors();

            // 基础分类类型 - 使用 overwriteExisting: true 确保覆盖
            registry.Register(ClassificationTypes.Keyword, new HighlightingStyle(darkColors["Keyword"]), true);
            registry.Register(ClassificationTypes.String, new HighlightingStyle(darkColors["String"]), true);
            registry.Register(ClassificationTypes.Comment, new HighlightingStyle(darkColors["Comment"]), true);
            registry.Register(ClassificationTypes.Number, new HighlightingStyle(darkColors["Number"]), true);
            registry.Register(ClassificationTypes.Identifier, new HighlightingStyle(darkColors["Identifier"]), true);
            registry.Register(ClassificationTypes.Operator, new HighlightingStyle(darkColors["Operator"]), true);
            registry.Register(ClassificationTypes.PreprocessorKeyword, new HighlightingStyle(darkColors["PreprocessorKeyword"]), true);
            registry.Register(CustomClassificationTypes.UnnecessaryCode, new HighlightingStyle(darkColors["UnnecessaryCode"]), true);

            // 高亮样式 - 使用背景色
            registry.Register(CustomClassificationTypes.SelectionMatchHighlight,
                new HighlightingStyle() { Background = darkColors["SelectionMatchHighlight"] }, true);
            registry.Register(CustomClassificationTypes.ReferenceHighlight,
                new HighlightingStyle() { Background = darkColors["ReferenceHighlight"] }, true);

            // 自定义分类类型（供 RoslynSemanticClassificationTagger 使用）
            registry.Register(_usedMethodName, new HighlightingStyle(darkColors["UsedMethodName"]) { Bold = true }, true);
            registry.Register(_classObjectReference, new HighlightingStyle(darkColors["ClassObjectReference"]), true);
            registry.Register(_interfaceObjectReference, new HighlightingStyle(darkColors["InterfaceObjectReference"]), true);
            registry.Register(_structureObjectReference, new HighlightingStyle(darkColors["StructureObjectReference"]), true);
            registry.Register(_parametersObjectReference, new HighlightingStyle(darkColors["ParametersObjectReference"]) { Bold = true }, true);

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
        /// 使用延迟初始化确保分类类型始终可用
        /// </summary>
        public static IClassificationType UsedMethodName
        {
            get
            {
                EnsureClassificationTypesCreated();
                return _usedMethodName;
            }
        }

        public static IClassificationType ClassObjectReference
        {
            get
            {
                EnsureClassificationTypesCreated();
                return _classObjectReference;
            }
        }

        public static IClassificationType InterfaceObjectReference
        {
            get
            {
                EnsureClassificationTypesCreated();
                return _interfaceObjectReference;
            }
        }

        public static IClassificationType StructureObjectReference
        {
            get
            {
                EnsureClassificationTypesCreated();
                return _structureObjectReference;
            }
        }

        public static IClassificationType ParametersObjectReference
        {
            get
            {
                EnsureClassificationTypesCreated();
                return _parametersObjectReference;
            }
        }

        /// <summary>
        /// 确保分类类型已创建（延迟初始化）
        /// 并注册到 AmbientHighlightingStyleRegistry
        /// </summary>
        private static void EnsureClassificationTypesCreated()
        {
            if (_usedMethodName != null)
                return; // 已初始化

            var colorCache = new ThemeAwareColorCache();
            var lightColors = colorCache.GetLightThemeColors();
            var registry = AmbientHighlightingStyleRegistry.Instance;

            // 创建分类类型
            _usedMethodName = new ClassificationType("Method Name", "Method Name");
            _classObjectReference = new ClassificationType("Class Name", "Class Name");
            _interfaceObjectReference = new ClassificationType("Interface Name", "Interface Name");
            _structureObjectReference = new ClassificationType("Struct Name", "Struct Name");
            _parametersObjectReference = new ClassificationType("Parameter Name", "Parameter Name");

            // 立即注册样式（确保在 Tagger 使用前就已注册）
            var methodStyle = new HighlightingStyle(lightColors["UsedMethodName"]) { Bold = true };
            var classStyle = new HighlightingStyle(lightColors["ClassObjectReference"]);
            var interfaceStyle = new HighlightingStyle(lightColors["InterfaceObjectReference"]);
            var structStyle = new HighlightingStyle(lightColors["StructureObjectReference"]);
            var paramStyle = new HighlightingStyle(lightColors["ParametersObjectReference"]) { Bold = true };

            registry.Register(_usedMethodName, methodStyle, true);
            registry.Register(_classObjectReference, classStyle, true);
            registry.Register(_interfaceObjectReference, interfaceStyle, true);
            registry.Register(_structureObjectReference, structStyle, true);
            registry.Register(_parametersObjectReference, paramStyle, true);

            // 验证注册结果
            System.Diagnostics.Debug.WriteLine($"[RoslynStyleConfigurator] 样式注册验证:");
            System.Diagnostics.Debug.WriteLine($"  - Method Name: FG={methodStyle.Foreground}, Bold={methodStyle.Bold}");
            System.Diagnostics.Debug.WriteLine($"  - Class Name: FG={classStyle.Foreground}");
            System.Diagnostics.Debug.WriteLine($"  - Interface Name: FG={interfaceStyle.Foreground}");
            System.Diagnostics.Debug.WriteLine($"  - Struct Name: FG={structStyle.Foreground}");
            System.Diagnostics.Debug.WriteLine($"  - Parameter Name: FG={paramStyle.Foreground}, Bold={paramStyle.Bold}");

            // 验证从 registry 能否取回样式
            var retrievedStyle = registry[_usedMethodName];
            System.Diagnostics.Debug.WriteLine($"  - 从 Registry 取回 Method Name 样式: {(retrievedStyle != null ? $"FG={retrievedStyle.Foreground}" : "null")}");

            System.Diagnostics.Debug.WriteLine("[RoslynStyleConfigurator] 自定义分类类型已创建并注册样式");
        }
    }
}
