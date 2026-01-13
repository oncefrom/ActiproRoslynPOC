using ActiproSoftware.Text;
using ActiproSoftware.Text.Tagging;
using ActiproSoftware.Text.Tagging.Implementation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ActiproRoslynPOC.Themes
{
    /// <summary>
    /// 语义分类 Tagger - 为方法名、类名等提供更细粒度的语法高亮
    /// </summary>
    public class SemanticClassificationTagger : TaggerBase<IClassificationTag>
    {
        private ICodeDocument _document;
        private bool _isUpdateScheduled;
        private List<TagSnapshotRange<IClassificationTag>> _cachedTags;
        private string _cachedText;

        public SemanticClassificationTagger(ICodeDocument document)
            : base("SemanticClassificationTagger", null, document, true)
        {
            _document = document;
            _cachedTags = new List<TagSnapshotRange<IClassificationTag>>();
            _cachedText = string.Empty;

            // 监听文档变化
            if (document != null)
            {
                document.TextChanged += OnDocumentTextChanged;
            }
        }

        /// <summary>
        /// 文档内容变化时
        /// </summary>
        private void OnDocumentTextChanged(object sender, TextSnapshotChangedEventArgs e)
        {
            if (!_isUpdateScheduled)
            {
                _isUpdateScheduled = true;

                // 延迟更新，避免频繁重新计算
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    new System.Action(() =>
                    {
                        _isUpdateScheduled = false;
                        UpdateTags();
                    }),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// 更新标签
        /// </summary>
        private void UpdateTags()
        {
            if (_document == null)
                return;

            var snapshot = _document.CurrentSnapshot;
            if (snapshot != null)
            {
                this.OnTagsChanged(new TagsChangedEventArgs(new TextSnapshotRange(snapshot, snapshot.TextRange)));
            }
        }

        /// <summary>
        /// 获取指定范围内的标签
        /// </summary>
        public override IEnumerable<TagSnapshotRange<IClassificationTag>> GetTags(
            NormalizedTextSnapshotRangeCollection snapshotRanges, object parameter)
        {
            if (snapshotRanges == null || snapshotRanges.Count == 0)
                yield break;

            var snapshot = snapshotRanges[0].Snapshot;
            if (snapshot == null)
                yield break;

            string code = snapshot.Text;

            // 使用缓存，避免频繁重新解析
            if (_cachedText != code || _cachedTags.Count == 0)
            {
                _cachedText = code;
                _cachedTags.Clear();

                try
                {
                    // 使用简单的语法分析（不需要完整语义模型）
                    var syntaxTree = CSharpSyntaxTree.ParseText(code);
                    var root = syntaxTree.GetRoot();

                    // 使用简单的语法访问器
                    var walker = new SimpleSyntaxWalker(snapshot);
                    walker.Visit(root);

                    _cachedTags.AddRange(walker.Tags);
                }
                catch (Exception ex)
                {
                    // 忽略解析错误，避免崩溃
                    System.Diagnostics.Debug.WriteLine($"[SemanticClassificationTagger] 解析错误: {ex.Message}");
                }
            }

            foreach (var tag in _cachedTags)
            {
                yield return tag;
            }
        }

        /// <summary>
        /// 简单语法树访问器 - 只使用语法分析，不需要语义模型
        /// </summary>
        private class SimpleSyntaxWalker : CSharpSyntaxWalker
        {
            private readonly ITextSnapshot _snapshot;
            private readonly List<TagSnapshotRange<IClassificationTag>> _tags;

            public List<TagSnapshotRange<IClassificationTag>> Tags => _tags;

            public SimpleSyntaxWalker(ITextSnapshot snapshot)
            {
                _snapshot = snapshot;
                _tags = new List<TagSnapshotRange<IClassificationTag>>();
            }

            public override void Visit(SyntaxNode node)
            {
                // 根据语法节点类型判断
                switch (node.Kind())
                {
                    case Microsoft.CodeAnalysis.CSharp.SyntaxKind.InvocationExpression:
                        // 方法调用 - 只标记方法名部分（排除 new 表达式）
                        var invocation = node as Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax;
                        if (invocation?.Expression != null)
                        {
                            // 排除 ObjectCreationExpression 中的调用（那是构造函数）
                            if (!(invocation.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax))
                            {
                                var methodName = GetMethodNameToken(invocation.Expression);
                                if (methodName.HasValue && !IsBuiltInMethod(methodName.Value.Text))
                                {
                                    AddTag(methodName.Value.Span, RoslynStyleConfigurator.UsedMethodName);
                                }
                            }
                        }
                        break;

                    case Microsoft.CodeAnalysis.CSharp.SyntaxKind.MethodDeclaration:
                        // 方法声明
                        var methodDecl = node as Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax;
                        if (methodDecl != null)
                        {
                            AddTag(methodDecl.Identifier.Span, RoslynStyleConfigurator.UsedMethodName);
                        }
                        break;

                    case Microsoft.CodeAnalysis.CSharp.SyntaxKind.ObjectCreationExpression:
                        // new 表达式中的类型 - 标记为类名
                        var objCreation = node as Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax;
                        if (objCreation?.Type != null)
                        {
                            var typeIdentifier = GetTypeIdentifier(objCreation.Type);
                            if (typeIdentifier.HasValue && !IsBuiltInType(typeIdentifier.Value.Text))
                            {
                                AddTag(typeIdentifier.Value.Span, RoslynStyleConfigurator.ClassObjectReference);
                            }
                        }
                        break;

                    case Microsoft.CodeAnalysis.CSharp.SyntaxKind.ClassDeclaration:
                        // 类声明
                        var classDecl = node as Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax;
                        if (classDecl != null)
                        {
                            AddTag(classDecl.Identifier.Span, RoslynStyleConfigurator.ClassObjectReference);
                        }
                        break;

                    case Microsoft.CodeAnalysis.CSharp.SyntaxKind.SimpleBaseType:
                        // 基类/接口引用（如 : CodedWorkflowBase）
                        var baseType = node as Microsoft.CodeAnalysis.CSharp.Syntax.SimpleBaseTypeSyntax;
                        if (baseType?.Type != null)
                        {
                            var typeIdentifier = GetTypeIdentifier(baseType.Type);
                            if (typeIdentifier.HasValue && !IsBuiltInType(typeIdentifier.Value.Text))
                            {
                                AddTag(typeIdentifier.Value.Span, RoslynStyleConfigurator.ClassObjectReference);
                            }
                        }
                        break;

                    case Microsoft.CodeAnalysis.CSharp.SyntaxKind.GenericName:
                        // 泛型类型 List<int> - 只在特定上下文中标记
                        var genericName = node as Microsoft.CodeAnalysis.CSharp.Syntax.GenericNameSyntax;
                        if (genericName != null && !IsBuiltInType(genericName.Identifier.Text))
                        {
                            // 检查是否是类型声明上下文
                            if (IsTypeContext(genericName))
                            {
                                AddTag(genericName.Identifier.Span, RoslynStyleConfigurator.ClassObjectReference);
                            }
                        }
                        break;

                    case Microsoft.CodeAnalysis.CSharp.SyntaxKind.IdentifierName:
                        // 标识符 - 检查是否是类型引用或静态成员访问
                        var identifierName = node as Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax;
                        if (identifierName != null)
                        {
                            var name = identifierName.Identifier.Text;

                            // 检查是否是类型上下文中的类型名（如变量声明 DateTime now = ...）
                            if (IsTypeNameInDeclaration(identifierName) && !IsBuiltInType(name))
                            {
                                AddTag(identifierName.Identifier.Span, RoslynStyleConfigurator.ClassObjectReference);
                            }
                            // 检查是否是静态成员访问的类型部分（如 Helper.FormatDate）
                            else if (IsStaticMemberAccessType(identifierName) && !IsBuiltInType(name) && char.IsUpper(name[0]))
                            {
                                AddTag(identifierName.Identifier.Span, RoslynStyleConfigurator.ClassObjectReference);
                            }
                        }
                        break;

                    case Microsoft.CodeAnalysis.CSharp.SyntaxKind.SimpleMemberAccessExpression:
                        // 成员访问表达式（如 Helper.FormatDate 或 new3.Test1）
                        var memberAccess = node as Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax;
                        if (memberAccess != null)
                        {
                            // 如果是方法调用的一部分，标记方法名
                            if (memberAccess.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax)
                            {
                                var methodNameText = memberAccess.Name.Identifier.Text;
                                if (!IsBuiltInMethod(methodNameText))
                                {
                                    AddTag(memberAccess.Name.Identifier.Span, RoslynStyleConfigurator.UsedMethodName);
                                }
                            }
                        }
                        break;
                }

                base.Visit(node);
            }

            private bool IsTypeNameInDeclaration(Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax identifier)
            {
                var parent = identifier.Parent;

                // 变量声明类型（如 DateTime now = ...）
                if (parent is Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclarationSyntax varDecl && varDecl.Type == identifier)
                    return true;

                // 参数类型
                if (parent is Microsoft.CodeAnalysis.CSharp.Syntax.ParameterSyntax param && param.Type == identifier)
                    return true;

                // 返回类型
                if (parent is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax method && method.ReturnType == identifier)
                    return true;

                // 属性类型
                if (parent is Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax prop && prop.Type == identifier)
                    return true;

                return false;
            }

            private bool IsStaticMemberAccessType(Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax identifier)
            {
                var parent = identifier.Parent;

                // 是 MemberAccessExpression 的左边部分（如 Helper.FormatDate 中的 Helper）
                if (parent is Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax memberAccess)
                {
                    return memberAccess.Expression == identifier;
                }

                return false;
            }

            private bool IsTypeContext(SyntaxNode node)
            {
                var parent = node.Parent;
                while (parent != null)
                {
                    if (parent is Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclarationSyntax ||
                        parent is Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax ||
                        parent is Microsoft.CodeAnalysis.CSharp.Syntax.TypeArgumentListSyntax ||
                        parent is Microsoft.CodeAnalysis.CSharp.Syntax.ParameterSyntax ||
                        parent is Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax ||
                        parent is Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax)
                    {
                        return true;
                    }
                    parent = parent.Parent;
                }
                return false;
            }

            private SyntaxToken? GetMethodNameToken(Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax expression)
            {
                if (expression is Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax identifierName)
                {
                    return identifierName.Identifier;
                }
                else if (expression is Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax memberAccess)
                {
                    return memberAccess.Name.Identifier;
                }
                return null;
            }

            private SyntaxToken? GetTypeIdentifier(Microsoft.CodeAnalysis.CSharp.Syntax.TypeSyntax typeSyntax)
            {
                if (typeSyntax is Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax identifierName)
                {
                    return identifierName.Identifier;
                }
                else if (typeSyntax is Microsoft.CodeAnalysis.CSharp.Syntax.GenericNameSyntax genericName)
                {
                    return genericName.Identifier;
                }
                return null;
            }

            private bool IsBuiltInType(string typeName)
            {
                // C# 内置类型不需要特殊高亮
                var builtInTypes = new HashSet<string>
                {
                    "int", "long", "short", "byte", "sbyte",
                    "uint", "ulong", "ushort",
                    "float", "double", "decimal",
                    "bool", "char", "string", "object",
                    "void", "var", "dynamic"
                };
                return builtInTypes.Contains(typeName);
            }

            private bool IsBuiltInMethod(string methodName)
            {
                // 常见的内置方法不需要特殊高亮
                var builtInMethods = new HashSet<string>
                {
                    "ToString", "GetHashCode", "Equals", "GetType",
                    "Parse", "TryParse", "Add", "Remove", "Clear",
                    "Count", "Length", "Contains", "IndexOf"
                };
                return builtInMethods.Contains(methodName);
            }

            private void AddTag(Microsoft.CodeAnalysis.Text.TextSpan span, ActiproSoftware.Text.IClassificationType classificationType)
            {
                if (classificationType == null)
                    return;

                var start = span.Start;
                var end = span.End;

                if (start >= 0 && end <= _snapshot.Length && start < end)
                {
                    var textRange = new ActiproSoftware.Text.TextRange(start, end);
                    var snapshotRange = new TextSnapshotRange(_snapshot, textRange);
                    var tag = new ClassificationTag(classificationType);

                    _tags.Add(new TagSnapshotRange<IClassificationTag>(snapshotRange, tag));
                }
            }
        }
    }
}
