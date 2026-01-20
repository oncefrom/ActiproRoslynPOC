using ActiproRoslynPOC.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ActiproRoslynPOC.Services
{
    public class RoslynCompilerService
    {
        private readonly List<MetadataReference> _defaultReferences;

        public RoslynCompilerService()
        {
            _defaultReferences = new List<MetadataReference>();

            // 加载当前 AppDomain 中所有已加载的程序集
            // 这样在产品迁移时，主程序已加载的程序集都能被编译器使用
            LoadAppDomainAssemblies();

            // 手动加载 TestDLL.dll（位于程序目录下）
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            var testDllPath = Path.Combine(appPath, "TestDLL.dll");
            if (File.Exists(testDllPath))
            {
                _defaultReferences.Add(MetadataReference.CreateFromFile(testDllPath));
                System.Diagnostics.Debug.WriteLine($"[RoslynCompilerService] 已加载 TestDLL.dll: {testDllPath}");
            }
        }

        /// <summary>
        /// 加载当前 AppDomain 中已加载的所有程序集作为编译引用
        /// </summary>
        private void LoadAppDomainAssemblies()
        {
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var assembly in loadedAssemblies)
            {
                try
                {
                    // 跳过动态程序集
                    if (assembly.IsDynamic)
                        continue;

                    // 跳过没有位置的程序集
                    if (string.IsNullOrEmpty(assembly.Location))
                        continue;

                    // 避免重复添加
                    if (loadedPaths.Contains(assembly.Location))
                        continue;

                    loadedPaths.Add(assembly.Location);
                    _defaultReferences.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RoslynCompilerService] 加载程序集失败: {assembly.FullName}, 错误: {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"[RoslynCompilerService] 已加载 {_defaultReferences.Count} 个程序集引用");
        }

        /// <summary>
        /// 手动添加程序集引用（供外部调用）
        /// </summary>
        public void AddAssemblyReference(string assemblyPath)
        {
            if (File.Exists(assemblyPath))
            {
                _defaultReferences.Add(MetadataReference.CreateFromFile(assemblyPath));
                System.Diagnostics.Debug.WriteLine($"[RoslynCompilerService] 已手动添加程序集引用: {assemblyPath}");
            }
        }

        public List<DiagnosticInfo> CheckSyntax(string code)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("SyntaxCheck", new[] { syntaxTree },
                _defaultReferences, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            return ConvertDiagnostics(compilation.GetDiagnostics());
        }

        //public CompilationResult Compile(string code)
        //{
        //    var result = new CompilationResult();
        //    try
        //    {
        //        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        //        var compilation = CSharpCompilation.Create("DynamicWorkflow", new[] { syntaxTree },
        //            _defaultReferences, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        //        using (var ms = new MemoryStream())
        //        {
        //            var emitResult = compilation.Emit(ms);
        //            result.Diagnostics = ConvertDiagnostics(emitResult.Diagnostics);

        //            if (emitResult.Success)
        //            {
        //                ms.Seek(0, SeekOrigin.Begin);
        //                result.AssemblyBytes = ms.ToArray();
        //                result.Assembly = Assembly.Load(result.AssemblyBytes);
        //                result.Success = true;
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        result.Diagnostics.Add(new DiagnosticInfo
        //        {
        //            Severity = DiagnosticSeverity.Error,
        //            Message = $"编译异常: {ex.Message}"
        //        });
        //    }
        //    return result;
        //}

        /// <summary>
        /// 编译单个文件（已有方法，保持不变）
        /// </summary>
        public CompilationResult Compile(string code, string assemblyName = "DynamicWorkflow")
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            return CompileInternal(new[] { syntaxTree }, assemblyName);
        }

        /// <summary>
        /// 编译多个文件（新增）
        /// </summary>
        public CompilationResult CompileMultiple(
            Dictionary<string, string> codeFiles,
            string assemblyName = "DynamicWorkflow")
        {
            var syntaxTrees = new List<SyntaxTree>();

            foreach (var kvp in codeFiles)
            {
                var fileName = kvp.Key;
                var code = kvp.Value;
                var sourceText = SourceText.From(code, Encoding.UTF8);
                var syntaxTree = CSharpSyntaxTree.ParseText(
                    sourceText,
                    path: fileName  // 设置文件名，用于错误提示
                );

                syntaxTrees.Add(syntaxTree);
            }

            return CompileInternal(syntaxTrees, assemblyName);
        }

        /// <summary>
        /// 编译项目中的所有 .cs 文件（新增）
        /// </summary>
        public CompilationResult CompileProject(string projectDirectory)
        {
            var codeFiles = new Dictionary<string, string>();

            // 查找所有 .cs 文件
            var csFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories);

            foreach (var filePath in csFiles)
            {
                var fileName = Path.GetFileName(filePath);
                var code = File.ReadAllText(filePath);
                codeFiles[fileName] = code;
            }

            return CompileMultiple(codeFiles, Path.GetFileName(projectDirectory));
        }

        /// <summary>
        /// 内部编译方法
        /// </summary>
        private CompilationResult CompileInternal(IEnumerable<SyntaxTree> syntaxTrees, string assemblyName)
        {
            var result = new CompilationResult();
            try
            {
                // 1. 强制使用 Debug 模式
                var compilation = CSharpCompilation.Create(
                    assemblyName,
                    syntaxTrees,
                    _defaultReferences,
                    new CSharpCompilationOptions(
                        OutputKind.DynamicallyLinkedLibrary,
                        optimizationLevel: OptimizationLevel.Debug)); // 开启调试优化级别

                using (var dllMs = new MemoryStream())
                using (var pdbMs = new MemoryStream()) // 新增：PDB 内存流
                {
                    // 2. 指定 EmitOptions 使用 PortablePdb
                    var emitOptions = new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb);

                    var emitResult = compilation.Emit(dllMs, pdbMs, options: emitOptions);
                    result.Diagnostics = ConvertDiagnostics(emitResult.Diagnostics);

                    if (emitResult.Success)
                    {
                        dllMs.Seek(0, SeekOrigin.Begin);
                        pdbMs.Seek(0, SeekOrigin.Begin);

                        result.AssemblyBytes = dllMs.ToArray();
                        result.PdbBytes = pdbMs.ToArray();  // 保存 PDB 字节数据

                        // 3. 关键：同时加载 DLL 和 PDB 字节
                        // 这样在异常发生时，StackTrace 就能对应到行号了
                        result.Assembly = Assembly.Load(result.AssemblyBytes, result.PdbBytes);
                        result.Success = true;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Diagnostics.Add(new DiagnosticInfo { Severity = DiagnosticSeverity.Error, Message = $"编译异常: {ex.Message}" });
            }
            return result;
        }

        public T CreateInstance<T>(Assembly assembly, string typeName) where T : class
        {
            var type = assembly.GetTypes().FirstOrDefault(t => t.Name == typeName || t.FullName == typeName);
            if (type == null) throw new InvalidOperationException($"找不到类型: {typeName}");
            return Activator.CreateInstance(type) as T;
        }

        private List<DiagnosticInfo> ConvertDiagnostics(IEnumerable<Diagnostic> diagnostics)
        {
            var result = new List<DiagnosticInfo>();
            foreach (var diag in diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning))
            {
                var lineSpan = diag.Location.GetLineSpan();
                result.Add(new DiagnosticInfo
                {
                    Id = diag.Id,
                    Message = diag.GetMessage(),
                    Severity = diag.Severity,
                    Line = lineSpan.StartLinePosition.Line + 1,
                    Column = lineSpan.StartLinePosition.Character + 1,
                    FileName = System.IO.Path.GetFileName(lineSpan.Path)
                });
            }
            return result;
        }
    }
}