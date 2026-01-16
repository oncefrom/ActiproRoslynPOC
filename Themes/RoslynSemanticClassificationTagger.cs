using ActiproSoftware.Text;
using ActiproSoftware.Text.Tagging;
using ActiproSoftware.Text.Tagging.Implementation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ActiproRoslynPOC.Themes
{
    /// <summary>
    /// 基于 Roslyn SemanticModel 的语义分类 Tagger
    /// 参考 UiPath ExtendedCSharpTokenTagger 实现
    /// 提供类名、接口名、方法名、参数、局部变量等的精确语义高亮
    /// </summary>
    public class RoslynSemanticClassificationTagger : TaggerBase<IClassificationTag>
    {
        private ICodeDocument _document;
        private bool _isUpdateScheduled;
        private List<TagSnapshotRange<IClassificationTag>> _cachedTags;
        private string _cachedText;
        private int _cachedTextHash;

        // Roslyn 编译环境（懒加载）
        private static CSharpCompilation _baseCompilation;
        private static readonly object _compilationLock = new object();

        // 分析超时控制
        private CancellationTokenSource _cts;
        private readonly object _analysisLock = new object();

        public RoslynSemanticClassificationTagger(ICodeDocument document)
            : base("RoslynSemanticClassificationTagger", null, document, true)
        {
            _document = document;
            _cachedTags = new List<TagSnapshotRange<IClassificationTag>>();
            _cachedText = string.Empty;
            _cachedTextHash = 0;

            if (document != null)
            {
                document.TextChanged += OnDocumentTextChanged;
            }

            // 确保基础编译环境已初始化
            EnsureBaseCompilation();
        }

        /// <summary>
        /// 确保基础编译环境已初始化（包含常用引用）
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

                    // 添加核心程序集引用
                    var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
                    if (!string.IsNullOrEmpty(trustedAssemblies))
                    {
                        var paths = trustedAssemblies.Split(Path.PathSeparator);
                        foreach (var path in paths)
                        {
                            var fileName = Path.GetFileName(path);
                            // 只添加核心程序集
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

                    // 如果没有通过 TRUSTED_PLATFORM_ASSEMBLIES 获取，使用备用方法
                    if (references.Count == 0)
                    {
                        // 从当前 AppDomain 加载
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

                    System.Diagnostics.Debug.WriteLine($"[RoslynSemanticTagger] 基础编译环境已初始化，引用数: {references.Count}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RoslynSemanticTagger] 初始化编译环境失败: {ex.Message}");
                }
            }
        }

        private void OnDocumentTextChanged(object sender, TextSnapshotChangedEventArgs e)
        {
            if (!_isUpdateScheduled)
            {
                _isUpdateScheduled = true;

                // 延迟更新，避免频繁重新计算
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        _isUpdateScheduled = false;
                        UpdateTags();
                    }),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void UpdateTags()
        {
            if (_document == null)
                return;

            var snapshot = _document.CurrentSnapshot;
            if (snapshot != null)
            {
                OnTagsChanged(new TagsChangedEventArgs(new TextSnapshotRange(snapshot, snapshot.TextRange)));
            }
        }

        public override IEnumerable<TagSnapshotRange<IClassificationTag>> GetTags(
            NormalizedTextSnapshotRangeCollection snapshotRanges, object parameter)
        {
            // 获取要返回的标签列表
            var tagsToReturn = GetTagsInternal(snapshotRanges);

            System.Diagnostics.Debug.WriteLine($"[RoslynSemanticTagger] GetTags 返回 {tagsToReturn.Count} 个标签");

            foreach (var tag in tagsToReturn)
            {
                yield return tag;
            }
        }

        /// <summary>
        /// 内部方法：获取标签（支持 try-catch）
        /// </summary>
        private List<TagSnapshotRange<IClassificationTag>> GetTagsInternal(
            NormalizedTextSnapshotRangeCollection snapshotRanges)
        {
            if (snapshotRanges == null || snapshotRanges.Count == 0)
                return new List<TagSnapshotRange<IClassificationTag>>();

            var snapshot = snapshotRanges[0].Snapshot;
            if (snapshot == null)
                return new List<TagSnapshotRange<IClassificationTag>>();

            string code = snapshot.Text;
            int currentHash = code.GetHashCode();

            // 使用缓存
            if (_cachedTextHash == currentHash && _cachedTags.Count > 0)
            {
                return _cachedTags;
            }

            // 取消之前的分析任务
            lock (_analysisLock)
            {
                _cts?.Cancel();
                _cts = new CancellationTokenSource();
            }

            var cancellationToken = _cts.Token;

            try
            {
                // 执行语义分析
                var tags = PerformSemanticAnalysis(code, snapshot, cancellationToken);

                if (!cancellationToken.IsCancellationRequested)
                {
                    _cachedText = code;
                    _cachedTextHash = currentHash;
                    _cachedTags = tags;
                    return _cachedTags;
                }
            }
            catch (OperationCanceledException)
            {
                // 分析被取消，返回缓存的标签
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RoslynSemanticTagger] 语义分析错误: {ex.Message}");
            }

            return _cachedTags;
        }

        /// <summary>
        /// 执行语义分析
        /// </summary>
        private List<TagSnapshotRange<IClassificationTag>> PerformSemanticAnalysis(
            string code, ITextSnapshot snapshot, CancellationToken cancellationToken)
        {
            var tags = new List<TagSnapshotRange<IClassificationTag>>();

            if (_baseCompilation == null)
            {
                System.Diagnostics.Debug.WriteLine("[RoslynSemanticTagger] 基础编译环境未初始化");
                return tags;
            }

            try
            {
                // 解析语法树
                var syntaxTree = CSharpSyntaxTree.ParseText(code, cancellationToken: cancellationToken);
                var root = syntaxTree.GetRoot(cancellationToken);

                // 创建编译单元
                var compilation = _baseCompilation.AddSyntaxTrees(syntaxTree);
                var semanticModel = compilation.GetSemanticModel(syntaxTree);

                // 使用语义访问器遍历
                var walker = new SemanticSyntaxWalker(snapshot, semanticModel, cancellationToken);
                walker.Visit(root);

                tags.AddRange(walker.Tags);

                System.Diagnostics.Debug.WriteLine($"[RoslynSemanticTagger] 语义分析完成，标签数: {tags.Count}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RoslynSemanticTagger] 分析异常: {ex.Message}");
            }

            return tags;
        }

        /// <summary>
        /// 语义语法树访问器 - 简化版，只使用 VisitIdentifierName 统一处理
        /// </summary>
        private class SemanticSyntaxWalker : CSharpSyntaxWalker
        {
            private readonly ITextSnapshot _snapshot;
            private readonly SemanticModel _semanticModel;
            private readonly CancellationToken _cancellationToken;
            private readonly List<TagSnapshotRange<IClassificationTag>> _tags;
            private readonly HashSet<int> _processedSpans; // 防止重复标记

            public List<TagSnapshotRange<IClassificationTag>> Tags => _tags;

            public SemanticSyntaxWalker(ITextSnapshot snapshot, SemanticModel semanticModel, CancellationToken cancellationToken)
            {
                _snapshot = snapshot;
                _semanticModel = semanticModel;
                _cancellationToken = cancellationToken;
                _tags = new List<TagSnapshotRange<IClassificationTag>>();
                _processedSpans = new HashSet<int>();
            }

            public override void Visit(SyntaxNode node)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                base.Visit(node);
            }

            /// <summary>
            /// 统一处理所有标识符 - 核心方法
            /// </summary>
            public override void VisitIdentifierName(IdentifierNameSyntax node)
            {
                ClassifyIdentifier(node);
                base.VisitIdentifierName(node);
            }

            /// <summary>
            /// 处理泛型名称
            /// </summary>
            public override void VisitGenericName(GenericNameSyntax node)
            {
                ClassifyGenericName(node);
                base.VisitGenericName(node);
            }

            private void ClassifyIdentifier(IdentifierNameSyntax node)
            {
                // 防止重复处理
                var spanKey = node.Identifier.Span.Start;
                if (_processedSpans.Contains(spanKey))
                    return;

                try
                {
                    // 获取符号信息
                    var symbolInfo = _semanticModel.GetSymbolInfo(node, _cancellationToken);
                    var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

                    if (symbol != null)
                    {
                        var classificationType = GetClassificationForSymbol(symbol);
                        if (classificationType != null)
                        {
                            AddTag(node.Identifier.Span, classificationType);
                            _processedSpans.Add(spanKey);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // 符号解析失败，忽略
                }
            }

            private void ClassifyGenericName(GenericNameSyntax node)
            {
                // 防止重复处理
                var spanKey = node.Identifier.Span.Start;
                if (_processedSpans.Contains(spanKey))
                    return;

                try
                {
                    var symbolInfo = _semanticModel.GetSymbolInfo(node, _cancellationToken);
                    var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

                    if (symbol != null)
                    {
                        var classificationType = GetClassificationForSymbol(symbol);
                        if (classificationType != null)
                        {
                            AddTag(node.Identifier.Span, classificationType);
                            _processedSpans.Add(spanKey);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // 忽略
                }
            }

            /// <summary>
            /// 根据符号获取分类类型
            /// </summary>
            private IClassificationType GetClassificationForSymbol(ISymbol symbol)
            {
                switch (symbol.Kind)
                {
                    case SymbolKind.NamedType:
                        var namedType = symbol as INamedTypeSymbol;
                        return GetClassificationForType(namedType);

                    case SymbolKind.Method:
                        return RoslynStyleConfigurator.UsedMethodName;

                    case SymbolKind.Parameter:
                        return RoslynStyleConfigurator.ParametersObjectReference;

                    case SymbolKind.Local:
                        return RoslynStyleConfigurator.ParametersObjectReference;

                    case SymbolKind.TypeParameter:
                        return RoslynStyleConfigurator.InterfaceObjectReference;

                    // 以下类型使用默认颜色
                    case SymbolKind.Field:
                    case SymbolKind.Property:
                    case SymbolKind.Namespace:
                    case SymbolKind.Event:
                    default:
                        return null;
                }
            }

            /// <summary>
            /// 根据类型获取分类类型
            /// </summary>
            private IClassificationType GetClassificationForType(INamedTypeSymbol typeSymbol)
            {
                if (typeSymbol == null)
                    return null;

                switch (typeSymbol.TypeKind)
                {
                    case TypeKind.Class:
                    case TypeKind.Delegate:
                        return RoslynStyleConfigurator.ClassObjectReference;

                    case TypeKind.Interface:
                        return RoslynStyleConfigurator.InterfaceObjectReference;

                    case TypeKind.Struct:
                    case TypeKind.Enum:
                        return RoslynStyleConfigurator.StructureObjectReference;

                    case TypeKind.TypeParameter:
                        return RoslynStyleConfigurator.InterfaceObjectReference;

                    default:
                        return null;
                }
            }

            private void AddTag(Microsoft.CodeAnalysis.Text.TextSpan span, IClassificationType classificationType)
            {
                if (classificationType == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SemanticWalker] AddTag: classificationType is null");
                    return;
                }

                var start = span.Start;
                var length = span.Length;

                if (start >= 0 && start + length <= _snapshot.Length && length > 0)
                {
                    var textRange = new ActiproSoftware.Text.TextRange(start, start + length);
                    var snapshotRange = new TextSnapshotRange(_snapshot, textRange);
                    var tag = new ClassificationTag(classificationType);

                    _tags.Add(new TagSnapshotRange<IClassificationTag>(snapshotRange, tag));

                    // 调试输出 - 验证分类类型和样式
                    try
                    {
                        var text = _snapshot.Text.Substring(start, length);
                        var registry = ActiproSoftware.Windows.Controls.SyntaxEditor.Highlighting.AmbientHighlightingStyleRegistry.Instance;
                        var style = registry[classificationType];
                        var hasStyle = style != null && style.Foreground.HasValue;
                        System.Diagnostics.Debug.WriteLine($"[SemanticWalker] AddTag: '{text}' -> Key='{classificationType.Key}', HasStyle={hasStyle}, FG={(hasStyle ? style.Foreground.Value.ToString() : "null")}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SemanticWalker] AddTag: ({start}, {length}) -> {classificationType.Key}, Error: {ex.Message}");
                    }
                }
            }
        }
    }
}
