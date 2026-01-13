using ActiproSoftware.Text;
using ActiproSoftware.Text.Implementation;
using ActiproSoftware.Text.Languages.DotNet.Implementation;
using ActiproSoftware.Windows.Controls.SyntaxEditor;
using ActiproSoftware.Windows.Controls.SyntaxEditor.Highlighting;
using ActiproSoftware.Windows.Controls.SyntaxEditor.Highlighting.Implementation;
using System.Windows.Media;

namespace ActiproRoslynPOC.Themes
{
    /// <summary>
    /// 扩展的 .NET 分类类型提供器 - 提供更丰富的语法高亮
    /// </summary>
    public class ExtendedDotNetClassificationTypeProvider : DotNetClassificationTypeProvider
    {
        private static ThemeAwareColorCache _colorCache = new ThemeAwareColorCache();
        private static IHighlightingStyleRegistry _customRegistry;

        // 自定义分类类型
        private static IClassificationType _keywordControl;
        private static IClassificationType _usedMethodName;
        private static IClassificationType _classObjectReference;
        private static IClassificationType _interfaceObjectReference;
        private static IClassificationType _structureObjectReference;
        private static IClassificationType _parametersObjectReference;
        private static IClassificationType _identifierCustom;

        // 带装饰的分类类型 (下划线)
        private static IClassificationType _keywordControlWithDecoration;
        private static IClassificationType _usedMethodNameWithDecoration;
        private static IClassificationType _classObjectReferenceWithDecoration;
        private static IClassificationType _interfaceObjectReferenceWithDecoration;
        private static IClassificationType _structureObjectReferenceWithDecoration;
        private static IClassificationType _parametersObjectReferenceWithDecoration;
        private static IClassificationType _identifierWithDecoration;

        public ExtendedDotNetClassificationTypeProvider() : base()
        {
            // 只初始化一次（因为是 static）
            if (_customRegistry == null)
            {
                InitializeCustomRegistry();
            }

            System.Diagnostics.Debug.WriteLine("[ExtendedDotNetClassificationTypeProvider] 构造函数完成");
        }

        /// <summary>
        /// 初始化自定义的 HighlightingStyleRegistry（使用 Actipro 官方推荐方式）
        /// </summary>
        private static void InitializeCustomRegistry()
        {
            System.Diagnostics.Debug.WriteLine("[ExtendedDotNetClassificationTypeProvider] 开始初始化自定义 Registry");

            // 使用 AmbientHighlightingStyleRegistry 而不是创建自定义 registry
            _customRegistry = AmbientHighlightingStyleRegistry.Instance;

            // 初始化自定义分类类型
            InitializeCustomClassificationTypes();

            // 注册浅色主题的颜色（light color palette）
            RegisterLightThemeColors();

            // 注册深色主题的颜色（dark color palette）
            RegisterDarkThemeColors();

            // 【关键】让 SyntaxEditorThemeManager 自动管理主题切换
            SyntaxEditorThemeManager.Manage(_customRegistry);

            System.Diagnostics.Debug.WriteLine("[ExtendedDotNetClassificationTypeProvider] 自定义 Registry 初始化完成，已启用 SyntaxEditorThemeManager");
        }

        /// <summary>
        /// 初始化自定义分类类型
        /// </summary>
        private void InitializeCustomClassificationTypes()
        {
            // 只初始化一次（因为是 static 字段）
            if (_keywordControl != null)
                return;

            // 创建自定义分类类型
            _keywordControl = new ClassificationType("KeywordControl", "Keyword Control");
            _usedMethodName = new ClassificationType("UsedMethodName", "Method Name");
            _classObjectReference = new ClassificationType("ClassObjectReference", "Class Reference");
            _interfaceObjectReference = new ClassificationType("InterfaceObjectReference", "Interface Reference");
            _structureObjectReference = new ClassificationType("StructureObjectReference", "Structure Reference");
            _parametersObjectReference = new ClassificationType("ParametersObjectReference", "Parameter Reference");
            _identifierCustom = new ClassificationType("IdentifierCustom", "Custom Identifier");

            // 创建带装饰的分类类型
            _keywordControlWithDecoration = new ClassificationType("KeywordControlWithDecoration", "Keyword Control (Decorated)");
            _usedMethodNameWithDecoration = new ClassificationType("UsedMethodNameWithDecoration", "Method Name (Decorated)");
            _classObjectReferenceWithDecoration = new ClassificationType("ClassObjectReferenceWithDecoration", "Class Reference (Decorated)");
            _interfaceObjectReferenceWithDecoration = new ClassificationType("InterfaceObjectReferenceWithDecoration", "Interface Reference (Decorated)");
            _structureObjectReferenceWithDecoration = new ClassificationType("StructureObjectReferenceWithDecoration", "Structure Reference (Decorated)");
            _parametersObjectReferenceWithDecoration = new ClassificationType("ParametersObjectReferenceWithDecoration", "Parameter Reference (Decorated)");
            _identifierWithDecoration = new ClassificationType("IdentifierWithDecoration", "Identifier (Decorated)");

            System.Diagnostics.Debug.WriteLine("[ExtendedDotNetClassificationTypeProvider] 自定义分类类型已初始化");
        }

        /// <summary>
        /// 注册所有分类类型
        /// </summary>
        private void RegisterClassificationTypes()
        {
            // 注册到分类类型注册表
            var registry = AmbientHighlightingStyleRegistry.Instance;

            // 这里先注册空样式，稍后通过 ApplyCurrentTheme 应用颜色
            registry.Register(_keywordControl, new HighlightingStyle());
            registry.Register(_usedMethodName, new HighlightingStyle());
            registry.Register(_classObjectReference, new HighlightingStyle());
            registry.Register(_interfaceObjectReference, new HighlightingStyle());
            registry.Register(_structureObjectReference, new HighlightingStyle());
            registry.Register(_parametersObjectReference, new HighlightingStyle());
            registry.Register(_identifierCustom, new HighlightingStyle());

            registry.Register(_keywordControlWithDecoration, new HighlightingStyle());
            registry.Register(_usedMethodNameWithDecoration, new HighlightingStyle());
            registry.Register(_classObjectReferenceWithDecoration, new HighlightingStyle());
            registry.Register(_interfaceObjectReferenceWithDecoration, new HighlightingStyle());
            registry.Register(_structureObjectReferenceWithDecoration, new HighlightingStyle());
            registry.Register(_parametersObjectReferenceWithDecoration, new HighlightingStyle());
            registry.Register(_identifierWithDecoration, new HighlightingStyle());
        }

        /// <summary>
        /// 切换主题
        /// </summary>
        public static void ToggleTheme()
        {
            _isDarkTheme = !_isDarkTheme;
            ApplyCurrentTheme();
        }

        /// <summary>
        /// 应用当前主题颜色
        /// </summary>
        public static void ApplyCurrentTheme()
        {
            var registry = AmbientHighlightingStyleRegistry.Instance;

            System.Diagnostics.Debug.WriteLine($"[ExtendedDotNetClassificationTypeProvider] 应用主题: {(_isDarkTheme ? "深色" : "浅色")}");

            // 应用基础分类类型的颜色
            ApplyBasicClassificationColors(registry);

            // 应用自定义分类类型的颜色
            ApplyCustomClassificationColors(registry);

            System.Diagnostics.Debug.WriteLine("[ExtendedDotNetClassificationTypeProvider] 主题颜色已应用");
        }

        /// <summary>
        /// 应用基础分类类型颜色 (Keyword, String, Comment, etc.)
        /// </summary>
        private static void ApplyBasicClassificationColors(IHighlightingStyleRegistry registry)
        {
            // 关键字
            registry.Register(ClassificationTypes.Keyword,
                new HighlightingStyle(_colorCache.GetColor("Keyword", _isDarkTheme)));

            // 字符串
            registry.Register(ClassificationTypes.String,
                new HighlightingStyle(_colorCache.GetColor("String", _isDarkTheme)));

            // 注释
            registry.Register(ClassificationTypes.Comment,
                new HighlightingStyle(_colorCache.GetColor("Comment", _isDarkTheme)));

            // 数字
            registry.Register(ClassificationTypes.Number,
                new HighlightingStyle(_colorCache.GetColor("Number", _isDarkTheme)));

            // 标识符
            registry.Register(ClassificationTypes.Identifier,
                new HighlightingStyle(_colorCache.GetColor("Identifier", _isDarkTheme)));

            // 操作符
            registry.Register(ClassificationTypes.Operator,
                new HighlightingStyle(_colorCache.GetColor("Operator", _isDarkTheme)));

            // 预处理指令
            registry.Register(ClassificationTypes.PreprocessorKeyword,
                new HighlightingStyle(_colorCache.GetColor("PreprocessorKeyword", _isDarkTheme)));

            // 未使用的代码
            registry.Register(CustomClassificationTypes.UnnecessaryCode,
                new HighlightingStyle(_colorCache.GetColor("UnnecessaryCode", _isDarkTheme)));
        }

        /// <summary>
        /// 应用自定义分类类型颜色
        /// </summary>
        private static void ApplyCustomClassificationColors(IHighlightingStyleRegistry registry)
        {
            // 控制关键字
            registry.Register(_keywordControl,
                new HighlightingStyle(_colorCache.GetColor("KeywordControl", _isDarkTheme)));

            // 方法名 (粗体)
            var methodColor = _colorCache.GetColor("UsedMethodName", _isDarkTheme);
            registry.Register(_usedMethodName,
                new HighlightingStyle(methodColor) { Bold = true });

            // 类引用
            registry.Register(_classObjectReference,
                new HighlightingStyle(_colorCache.GetColor("ClassObjectReference", _isDarkTheme)));

            // 接口引用
            registry.Register(_interfaceObjectReference,
                new HighlightingStyle(_colorCache.GetColor("InterfaceObjectReference", _isDarkTheme)));

            // 结构体引用
            registry.Register(_structureObjectReference,
                new HighlightingStyle(_colorCache.GetColor("StructureObjectReference", _isDarkTheme)));

            // 参数 (粗体)
            var paramColor = _colorCache.GetColor("ParametersObjectReference", _isDarkTheme);
            registry.Register(_parametersObjectReference,
                new HighlightingStyle(paramColor) { Bold = true });

            // 自定义标识符
            registry.Register(_identifierCustom,
                new HighlightingStyle(_colorCache.GetColor("Identifier", _isDarkTheme)));

            // 带装饰的版本 (带下划线) - 注意: Actipro 可能不支持 LineKind.Dot，这里使用普通颜色
            registry.Register(_keywordControlWithDecoration,
                new HighlightingStyle(_colorCache.GetColor("KeywordControl", _isDarkTheme)));

            registry.Register(_usedMethodNameWithDecoration,
                new HighlightingStyle(methodColor) { Bold = true });

            registry.Register(_classObjectReferenceWithDecoration,
                new HighlightingStyle(_colorCache.GetColor("ClassObjectReference", _isDarkTheme)));

            registry.Register(_interfaceObjectReferenceWithDecoration,
                new HighlightingStyle(_colorCache.GetColor("InterfaceObjectReference", _isDarkTheme)));

            registry.Register(_structureObjectReferenceWithDecoration,
                new HighlightingStyle(_colorCache.GetColor("StructureObjectReference", _isDarkTheme)));

            registry.Register(_parametersObjectReferenceWithDecoration,
                new HighlightingStyle(paramColor) { Bold = true });

            registry.Register(_identifierWithDecoration,
                new HighlightingStyle(_colorCache.GetColor("Identifier", _isDarkTheme)));
        }

        /// <summary>
        /// 获取当前主题状态
        /// </summary>
        public static bool IsDarkTheme => _isDarkTheme;

        // 公开属性以供外部访问自定义分类类型
        public IClassificationType KeywordControl => _keywordControl;
        public IClassificationType UsedMethodName => _usedMethodName;
        public IClassificationType ClassObjectReference => _classObjectReference;
        public IClassificationType InterfaceObjectReference => _interfaceObjectReference;
        public IClassificationType StructureObjectReference => _structureObjectReference;
        public IClassificationType ParametersObjectReference => _parametersObjectReference;
        public IClassificationType IdentifierCustom => _identifierCustom;
    }
}
