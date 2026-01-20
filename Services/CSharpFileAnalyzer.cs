using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ActiproRoslynPOC.Services
{
    /// <summary>
    /// C# 文件入口方法参数信息
    /// </summary>
    public class CSharpParameterInfo
    {
        public string Name { get; set; }
        public string TypeName { get; set; }
        public bool HasDefaultValue { get; set; }
        public string DefaultValueText { get; set; }
        public bool IsOptional => HasDefaultValue;

        /// <summary>
        /// 获取用于 UI 显示的类型名称
        /// </summary>
        public string DisplayTypeName
        {
            get
            {
                // 简化常见类型名称
                switch (TypeName)
                {
                    case "String": return "string";
                    case "Int32": return "int";
                    case "Int64": return "long";
                    case "Boolean": return "bool";
                    case "Double": return "double";
                    case "Single": return "float";
                    case "Decimal": return "decimal";
                    default: return TypeName;
                }
            }
        }

        /// <summary>
        /// 获取参数对应的 .NET 类型
        /// </summary>
        public Type GetClrType()
        {
            switch (TypeName.ToLowerInvariant())
            {
                case "string": return typeof(string);
                case "int":
                case "int32": return typeof(int);
                case "long":
                case "int64": return typeof(long);
                case "bool":
                case "boolean": return typeof(bool);
                case "double": return typeof(double);
                case "float":
                case "single": return typeof(float);
                case "decimal": return typeof(decimal);
                case "datetime": return typeof(DateTime);
                case "object": return typeof(object);
                default: return typeof(object);
            }
        }
    }

    /// <summary>
    /// C# 文件入口方法签名信息
    /// </summary>
    public class CSharpEntryMethodInfo
    {
        public string FileName { get; set; }
        public string ClassName { get; set; }
        public string MethodName { get; set; }
        public string WorkflowName { get; set; }  // [Workflow(Name = "xxx")] 中的名称
        public string Description { get; set; }    // [Workflow(Description = "xxx")] 中的描述
        public string ReturnTypeName { get; set; }
        public List<CSharpParameterInfo> Parameters { get; set; } = new List<CSharpParameterInfo>();
        public bool HasParameters => Parameters.Count > 0;
        public bool HasReturnValue => !string.IsNullOrEmpty(ReturnTypeName) && ReturnTypeName != "void";
    }

    /// <summary>
    /// C# 文件分析器 - 使用 Roslyn 语法分析解析入口方法参数
    /// 不需要编译，直接分析源代码
    /// </summary>
    public class CSharpFileAnalyzer
    {
        /// <summary>
        /// 分析 C# 文件，获取入口方法信息
        /// </summary>
        /// <param name="filePath">C# 文件路径</param>
        /// <returns>入口方法信息，如果没有找到则返回 null</returns>
        public CSharpEntryMethodInfo AnalyzeFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"文件不存在: {filePath}");
            }

            var code = File.ReadAllText(filePath);
            return AnalyzeCode(code, Path.GetFileName(filePath));
        }

        /// <summary>
        /// 分析 C# 代码，获取入口方法信息
        /// </summary>
        /// <param name="code">C# 源代码</param>
        /// <param name="fileName">文件名（可选）</param>
        /// <returns>入口方法信息，如果没有找到则返回 null</returns>
        public CSharpEntryMethodInfo AnalyzeCode(string code, string fileName = null)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var root = syntaxTree.GetCompilationUnitRoot();

            // 查找继承自 CodedWorkflowBase 的类
            var classDeclarations = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(c => InheritsFromCodedWorkflowBase(c))
                .ToList();

            foreach (var classDecl in classDeclarations)
            {
                // 查找带有 [Workflow] 特性的方法
                var workflowMethod = classDecl.Members
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(m => HasWorkflowAttribute(m));

                if (workflowMethod != null)
                {
                    var info = new CSharpEntryMethodInfo
                    {
                        FileName = fileName,
                        ClassName = classDecl.Identifier.Text,
                        MethodName = workflowMethod.Identifier.Text,
                        ReturnTypeName = GetReturnTypeName(workflowMethod)
                    };

                    // 解析 [Workflow] 特性的参数
                    ParseWorkflowAttribute(workflowMethod, info);

                    // 解析方法参数
                    foreach (var param in workflowMethod.ParameterList.Parameters)
                    {
                        var paramInfo = new CSharpParameterInfo
                        {
                            Name = param.Identifier.Text,
                            TypeName = param.Type?.ToString() ?? "object",
                            HasDefaultValue = param.Default != null,
                            DefaultValueText = param.Default?.Value?.ToString()
                        };
                        info.Parameters.Add(paramInfo);
                    }

                    return info;
                }
            }

            return null;
        }

        /// <summary>
        /// 分析目录下所有 C# 文件
        /// </summary>
        public List<CSharpEntryMethodInfo> AnalyzeDirectory(string directoryPath)
        {
            var results = new List<CSharpEntryMethodInfo>();
            var csFiles = Directory.GetFiles(directoryPath, "*.cs", SearchOption.TopDirectoryOnly);

            foreach (var filePath in csFiles)
            {
                try
                {
                    var info = AnalyzeFile(filePath);
                    if (info != null)
                    {
                        results.Add(info);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CSharpFileAnalyzer] 分析文件失败 {filePath}: {ex.Message}");
                }
            }

            return results;
        }

        /// <summary>
        /// 检查类是否继承自 CodedWorkflowBase
        /// </summary>
        private bool InheritsFromCodedWorkflowBase(ClassDeclarationSyntax classDecl)
        {
            if (classDecl.BaseList == null)
                return false;

            return classDecl.BaseList.Types
                .Any(t => t.Type.ToString().Contains("CodedWorkflowBase"));
        }

        /// <summary>
        /// 检查方法是否有 [Workflow] 特性
        /// </summary>
        private bool HasWorkflowAttribute(MethodDeclarationSyntax method)
        {
            return method.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(a => a.Name.ToString() == "Workflow" ||
                         a.Name.ToString() == "WorkflowAttribute");
        }

        /// <summary>
        /// 解析 [Workflow] 特性的参数
        /// </summary>
        private void ParseWorkflowAttribute(MethodDeclarationSyntax method, CSharpEntryMethodInfo info)
        {
            var workflowAttr = method.AttributeLists
                .SelectMany(al => al.Attributes)
                .FirstOrDefault(a => a.Name.ToString() == "Workflow" ||
                                    a.Name.ToString() == "WorkflowAttribute");

            if (workflowAttr?.ArgumentList != null)
            {
                foreach (var arg in workflowAttr.ArgumentList.Arguments)
                {
                    var nameEquals = arg.NameEquals?.Name?.ToString();
                    var value = arg.Expression?.ToString()?.Trim('"');

                    if (nameEquals == "Name")
                    {
                        info.WorkflowName = value;
                    }
                    else if (nameEquals == "Description")
                    {
                        info.Description = value;
                    }
                }
            }

            // 如果没有指定 Name，使用方法名
            if (string.IsNullOrEmpty(info.WorkflowName))
            {
                info.WorkflowName = info.MethodName;
            }
        }

        /// <summary>
        /// 获取返回类型名称
        /// </summary>
        private string GetReturnTypeName(MethodDeclarationSyntax method)
        {
            var returnType = method.ReturnType;

            // 处理元组类型
            if (returnType is TupleTypeSyntax tupleType)
            {
                var elements = tupleType.Elements
                    .Select(e => $"{e.Type} {e.Identifier}")
                    .ToList();
                return $"({string.Join(", ", elements)})";
            }

            return returnType.ToString();
        }
    }
}
