using ActiproSoftware.Text;
using ActiproSoftware.Text.Languages.CSharp.Implementation;
using ActiproSoftware.Text.Lexing;
using ActiproSoftware.Text.Tagging;
using ActiproSoftware.Text.Tagging.Implementation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ActiproRoslynPOC.Themes
{
    /// <summary>
    /// 基于 Roslyn 的扩展 Token Tagger
    /// 继承 Actipro 的 TokenTagger，在 token 级别提供语义分类
    /// 参考 UiPath ExtendedCSharpTokenTagger 实现
    /// </summary>
    public class RoslynTokenTagger : TokenTagger
    {
        // Roslyn 编译环境（静态共享）
        private static CSharpCompilation _baseCompilation;
        private static readonly object _compilationLock = new object();

        // 语义分析缓存
        private SemanticModel _cachedSemanticModel;
        private SyntaxTree _cachedSyntaxTree;
        private string _cachedText;
        private int _cachedTextHash;
        // 使用行/列位置作为 key（参考 UiPath 的 GetKey 方法）
        private Dictionary<long, SemanticHighlightInfo> _tokenClassifications;

        public RoslynTokenTagger(ICodeDocument document)
            : base(document)
        {
            _tokenClassifications = new Dictionary<long, SemanticHighlightInfo>();
            EnsureBaseCompilation();

            // 订阅文本变化事件
            if (document != null)
            {
                document.TextChanged += OnDocumentTextChanged;
            }

            System.Diagnostics.Debug.WriteLine("[RoslynTokenTagger] TokenTagger 已创建");
        }

        private void OnDocumentTextChanged(object sender, TextSnapshotChangedEventArgs e)
        {
            // 清除缓存，下次 ClassifyToken 时会重新分析
            _cachedSemanticModel = null;
            _cachedSyntaxTree = null;
            _tokenClassifications.Clear();
        }

        /// <summary>
        /// 确保基础编译环境已初始化
        /// </summary>
        private static void EnsureBaseCompilation()
        {
            if (_baseCompilation != null)
                return;

            lock (_compilationLock)
            {
                if (_baseCompilation != null)
                    return;

                try
                {
                    var references = new List<MetadataReference>();

                    var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
                    if (!string.IsNullOrEmpty(trustedAssemblies))
                    {
                        var paths = trustedAssemblies.Split(Path.PathSeparator);
                        foreach (var path in paths)
                        {
                            var fileName = Path.GetFileName(path);
                            if (fileName.StartsWith("System.") ||
                                fileName.StartsWith("Microsoft.") ||
                                fileName == "mscorlib.dll" ||
                                fileName == "netstandard.dll")
                            {
                                try
                                {
                                    references.Add(MetadataReference.CreateFromFile(path));
                                }
                                catch { }
                            }
                        }
                    }

                    if (references.Count == 0)
                    {
                        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location))
                                continue;
                            try
                            {
                                references.Add(MetadataReference.CreateFromFile(asm.Location));
                            }
                            catch { }
                        }
                    }

                    _baseCompilation = CSharpCompilation.Create(
                        "SemanticAnalysis",
                        references: references,
                        options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                            .WithAllowUnsafe(true)
                            .WithOptimizationLevel(OptimizationLevel.Debug));

                    System.Diagnostics.Debug.WriteLine($"[RoslynTokenTagger] 基础编译环境已初始化，引用数: {references.Count}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RoslynTokenTagger] 初始化编译环境失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 确保语义模型已准备好
        /// </summary>
        private void EnsureSemanticModel()
        {
            if (Document == null)
                return;

            string code = Document.CurrentSnapshot.Text;
            int currentHash = code.GetHashCode();

            if (_cachedTextHash == currentHash && _cachedSemanticModel != null)
                return;

            try
            {
                _cachedSyntaxTree = CSharpSyntaxTree.ParseText(code);
                var compilation = _baseCompilation.AddSyntaxTrees(_cachedSyntaxTree);
                _cachedSemanticModel = compilation.GetSemanticModel(_cachedSyntaxTree);
                _cachedText = code;
                _cachedTextHash = currentHash;
                _tokenClassifications.Clear();

                // 预先分析所有标识符
                AnalyzeAllIdentifiers();

                System.Diagnostics.Debug.WriteLine($"[RoslynTokenTagger] 语义模型已更新，分类数: {_tokenClassifications.Count}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RoslynTokenTagger] 创建语义模型失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 预先分析所有标识符
        /// </summary>
        private void AnalyzeAllIdentifiers()
        {
            if (_cachedSyntaxTree == null || _cachedSemanticModel == null)
                return;

            var root = _cachedSyntaxTree.GetRoot();

            // 遍历所有标识符名称节点
            var identifiers = root.DescendantNodes().OfType<IdentifierNameSyntax>();
            foreach (var identifier in identifiers)
            {
                var span = identifier.Identifier.Span;
                var identifierText = identifier.Identifier.Text;

                // 获取行列位置（Roslyn 的 LinePosition）
                var lineSpan = _cachedSyntaxTree.GetLineSpan(span);
                var startLine = lineSpan.StartLinePosition.Line;
                var startColumn = lineSpan.StartLinePosition.Character;
                var endLine = lineSpan.EndLinePosition.Line;
                var endColumn = lineSpan.EndLinePosition.Character;

                // 使用与 UiPath 相同的 GetKey 算法
                var key = GetPositionKey(startLine, startColumn, endLine, endColumn);

                if (_tokenClassifications.ContainsKey(key))
                    continue;

                try
                {
                    var symbolInfo = _cachedSemanticModel.GetSymbolInfo(identifier);
                    var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

                    if (symbol != null)
                    {
                        var classification = GetClassificationForSymbol(symbol);
                        if (classification != SemanticHighlightInfo.None)
                        {
                            _tokenClassifications[key] = classification;
                            // 调试：打印保存的分类信息
                            System.Diagnostics.Debug.WriteLine($"[RoslynTokenTagger] 分析: '{identifierText}' Line={startLine},Col={startColumn} -> {classification}");
                        }
                    }
                }
                catch { }
            }

            // 遍历泛型名称
            var genericNames = root.DescendantNodes().OfType<GenericNameSyntax>();
            foreach (var genericName in genericNames)
            {
                var span = genericName.Identifier.Span;
                var identifierText = genericName.Identifier.Text;

                // 获取行列位置
                var lineSpan = _cachedSyntaxTree.GetLineSpan(span);
                var startLine = lineSpan.StartLinePosition.Line;
                var startColumn = lineSpan.StartLinePosition.Character;
                var endLine = lineSpan.EndLinePosition.Line;
                var endColumn = lineSpan.EndLinePosition.Character;

                var key = GetPositionKey(startLine, startColumn, endLine, endColumn);

                if (_tokenClassifications.ContainsKey(key))
                    continue;

                try
                {
                    var symbolInfo = _cachedSemanticModel.GetSymbolInfo(genericName);
                    var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

                    if (symbol != null)
                    {
                        var classification = GetClassificationForSymbol(symbol);
                        if (classification != SemanticHighlightInfo.None)
                        {
                            _tokenClassifications[key] = classification;
                            System.Diagnostics.Debug.WriteLine($"[RoslynTokenTagger] 分析: '{identifierText}' Line={startLine},Col={startColumn} -> {classification}");
                        }
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// 根据行列位置生成唯一键（参考 UiPath 的 GetKey 实现）
        /// </summary>
        private static long GetPositionKey(int startLine, int startColumn, int endLine, int endColumn)
        {
            return (long)(((ulong)endColumn & 0x7FFuL) |
                         (ulong)((long)(startColumn & 0x7FF) << 11) |
                         (ulong)((long)(endLine & 0x1FFFFF) << 22)) |
                   ((long)(startLine & 0x1FFFFF) << 43);
        }

        /// <summary>
        /// 根据符号获取分类信息
        /// </summary>
        private SemanticHighlightInfo GetClassificationForSymbol(ISymbol symbol)
        {
            switch (symbol.Kind)
            {
                case SymbolKind.NamedType:
                    var namedType = symbol as INamedTypeSymbol;
                    return GetClassificationForType(namedType);

                case SymbolKind.Method:
                    return SemanticHighlightInfo.MethodName;

                case SymbolKind.Parameter:
                    return SemanticHighlightInfo.ParameterName;

                case SymbolKind.Local:
                    return SemanticHighlightInfo.LocalName;

                case SymbolKind.TypeParameter:
                    return SemanticHighlightInfo.TypeParameterName;

                default:
                    return SemanticHighlightInfo.None;
            }
        }

        /// <summary>
        /// 根据类型获取分类信息
        /// </summary>
        private SemanticHighlightInfo GetClassificationForType(INamedTypeSymbol typeSymbol)
        {
            if (typeSymbol == null)
                return SemanticHighlightInfo.None;

            switch (typeSymbol.TypeKind)
            {
                case TypeKind.Class:
                case TypeKind.Delegate:
                    return SemanticHighlightInfo.ClassName;

                case TypeKind.Interface:
                    return SemanticHighlightInfo.InterfaceName;

                case TypeKind.Struct:
                    return SemanticHighlightInfo.StructName;

                case TypeKind.Enum:
                    return SemanticHighlightInfo.EnumName;

                case TypeKind.TypeParameter:
                    return SemanticHighlightInfo.TypeParameterName;

                default:
                    return SemanticHighlightInfo.None;
            }
        }

        /// <summary>
        /// 重写 ClassifyToken - 核心方法
        /// 在 token 级别提供语义分类
        /// </summary>
        public override IClassificationType ClassifyToken(IToken token)
        {
            // 确保语义模型已准备好
            EnsureSemanticModel();

            // 如果是标识符类型的 token，尝试获取语义分类
            if (CSharpTokenId.IsIdentifierClassificationType(token.Id))
            {
                // 使用 token 的行列位置（与 Roslyn 分析时使用相同的坐标系）
                var startLine = token.StartPosition.Line;
                var startColumn = token.StartPosition.Character;
                var endLine = token.EndPosition.Line;
                var endColumn = token.EndPosition.Character;

                // 使用与 Roslyn 分析相同的 key 算法
                var key = GetPositionKey(startLine, startColumn, endLine, endColumn);

                var tokenText = Document.CurrentSnapshot.GetSubstring(
                    new ActiproSoftware.Text.TextRange(token.StartOffset, token.EndOffset));

                if (_tokenClassifications.TryGetValue(key, out var classification))
                {
                    var classificationType = GetClassificationTypeForInfo(classification);
                    if (classificationType != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[RoslynTokenTagger] ClassifyToken 匹配: '{tokenText}' Line={startLine},Col={startColumn} -> {classification}");
                        return classificationType;
                    }
                }
                else
                {
                    // 调试：显示未匹配的 token（减少日志量，只显示关键信息）
                    // System.Diagnostics.Debug.WriteLine($"[RoslynTokenTagger] ClassifyToken: '{tokenText}' Line={startLine},Col={startColumn} -> 未找到分类");
                }
            }

            // 默认使用基类的分类
            return base.ClassifyToken(token);
        }

        /// <summary>
        /// 将语义分类信息转换为 IClassificationType
        /// </summary>
        private IClassificationType GetClassificationTypeForInfo(SemanticHighlightInfo info)
        {
            switch (info)
            {
                case SemanticHighlightInfo.ClassName:
                    return RoslynStyleConfigurator.ClassObjectReference;

                case SemanticHighlightInfo.InterfaceName:
                case SemanticHighlightInfo.TypeParameterName:
                    return RoslynStyleConfigurator.InterfaceObjectReference;

                case SemanticHighlightInfo.StructName:
                case SemanticHighlightInfo.EnumName:
                    return RoslynStyleConfigurator.StructureObjectReference;

                case SemanticHighlightInfo.MethodName:
                    return RoslynStyleConfigurator.UsedMethodName;

                case SemanticHighlightInfo.ParameterName:
                case SemanticHighlightInfo.LocalName:
                    return RoslynStyleConfigurator.ParametersObjectReference;

                default:
                    return null;
            }
        }

        /// <summary>
        /// 语义高亮分类信息
        /// </summary>
        private enum SemanticHighlightInfo
        {
            None,
            ClassName,
            InterfaceName,
            StructName,
            EnumName,
            TypeParameterName,
            MethodName,
            ParameterName,
            LocalName
        }
    }
}
