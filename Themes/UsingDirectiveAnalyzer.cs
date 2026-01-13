using ActiproSoftware.Text;
using ActiproSoftware.Text.Tagging;
using ActiproSoftware.Text.Tagging.Implementation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace ActiproRoslynPOC.Themes
{
    /// <summary>
    /// using 引用状态分析器 - 检测未使用的 using
    /// </summary>
    public class UsingDirectiveAnalyzer
    {
        /// <summary>
        /// 分析代码中的 using 引用，返回未使用的 using 位置
        /// </summary>
        public static List<TextRange> AnalyzeUnusedUsings(string code)
        {
            var unusedRanges = new List<TextRange>();

            if (string.IsNullOrWhiteSpace(code))
                return unusedRanges;

            try
            {
                // 解析代码
                var syntaxTree = CSharpSyntaxTree.ParseText(code);
                var root = syntaxTree.GetRoot() as CompilationUnitSyntax;

                if (root == null)
                    return unusedRanges;

                // 获取所有 using 指令
                var usingDirectives = root.Usings.ToList();
                if (!usingDirectives.Any())
                    return unusedRanges;

                // 获取代码中所有使用的标识符
                var usedIdentifiers = GetUsedIdentifiers(root);

                // 检查每个 using 是否被使用
                foreach (var usingDirective in usingDirectives)
                {
                    var namespaceName = usingDirective.Name.ToString();
                    var lastPart = namespaceName.Split('.').Last();

                    // 简单检测：如果命名空间的最后一部分没有在代码中出现，标记为未使用
                    bool isUsed = IsNamespaceUsed(namespaceName, usedIdentifiers, root);

                    if (!isUsed)
                    {
                        var span = usingDirective.FullSpan;
                        unusedRanges.Add(new TextRange(span.Start, span.End));
                    }
                }
            }
            catch
            {
                // 解析失败时返回空列表
            }

            return unusedRanges;
        }

        /// <summary>
        /// 获取代码中所有使用的标识符
        /// </summary>
        private static HashSet<string> GetUsedIdentifiers(CompilationUnitSyntax root)
        {
            var identifiers = new HashSet<string>();

            // 遍历所有标识符节点
            var identifierNodes = root.DescendantNodes()
                .OfType<IdentifierNameSyntax>();

            foreach (var node in identifierNodes)
            {
                identifiers.Add(node.Identifier.Text);
            }

            return identifiers;
        }

        /// <summary>
        /// 检查命名空间是否被使用
        /// </summary>
        private static bool IsNamespaceUsed(string namespaceName, HashSet<string> usedIdentifiers, CompilationUnitSyntax root)
        {
            // 提取命名空间的各个部分
            var parts = namespaceName.Split('.');

            // 检查是否有任何部分在代码中被使用
            foreach (var part in parts)
            {
                if (usedIdentifiers.Contains(part))
                    return true;
            }

            // 特殊检查：System.Linq 的扩展方法
            if (namespaceName.Contains("System.Linq"))
            {
                var hasLinqMethods = root.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv =>
                    {
                        var name = inv.Expression.ToString();
                        return name.Contains("Where") || name.Contains("Select") ||
                               name.Contains("First") || name.Contains("Any") ||
                               name.Contains("Count") || name.Contains("ToList");
                    });

                if (hasLinqMethods)
                    return true;
            }

            // 特殊检查：System.Collections.Generic 的泛型集合
            if (namespaceName.Contains("System.Collections.Generic"))
            {
                var hasGenericCollections = root.DescendantNodes()
                    .OfType<GenericNameSyntax>()
                    .Any(g =>
                    {
                        var name = g.Identifier.Text;
                        return name == "List" || name == "Dictionary" ||
                               name == "HashSet" || name == "Queue" ||
                               name == "Stack";
                    });

                if (hasGenericCollections)
                    return true;
            }

            return false;
        }
    }
}
