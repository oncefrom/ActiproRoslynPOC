using ActiproRoslynPOC.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ActiproRoslynPOC.Services
{
    /// <summary>
    /// 代码执行服务
    /// </summary>
    public class CodeExecutionService
    {
        private readonly RoslynCompilerService _compiler;
        public event EventHandler<string> LogEvent;

        public CodeExecutionService()
        {
            _compiler = new RoslynCompilerService();
        }

        /// <summary>
        /// 执行单个文件（保持兼容）
        /// </summary>
        public ExecutionResult Execute(string code, string typeName = null)
        {
            return ExecuteInternal(new Dictionary<string, string> { { "main.cs", code } }, typeName);
        }

        /// <summary>
        /// 执行带依赖的文件（新增）
        /// </summary>
        public ExecutionResult ExecuteWithDependencies(
            string mainFilePath,
            string projectDirectory = null)
        {
            var codeFiles = new Dictionary<string, string>();

            // 1. 读取主文件
            string mainCode = File.ReadAllText(mainFilePath);
            string mainFileName = Path.GetFileName(mainFilePath);
            codeFiles[mainFileName] = mainCode;

            // 2. 如果指定了项目目录，读取所有其他 .cs 文件
            if (!string.IsNullOrEmpty(projectDirectory) && Directory.Exists(projectDirectory))
            {
                var allCsFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories);

                foreach (var filePath in allCsFiles)
                {
                    // 跳过主文件
                    if (Path.GetFullPath(filePath) == Path.GetFullPath(mainFilePath))
                        continue;

                    var fileName = Path.GetFileName(filePath);
                    var code = File.ReadAllText(filePath);
                    codeFiles[fileName] = code;

                    Log($"包含依赖文件: {fileName}");
                }
            }

            return ExecuteInternal(codeFiles, null);
        }

        /// <summary>
        /// 内部执行方法
        /// </summary>
        private ExecutionResult ExecuteInternal(
            Dictionary<string, string> codeFiles,
            string typeName = null)
        {
            var result = new ExecutionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                Log($"开始编译 {codeFiles.Count} 个文件...");

                // 编译所有文件
                var compileResult = _compiler.CompileMultiple(codeFiles);
                result.CompilationResult = compileResult;

                if (!compileResult.Success)
                {
                    result.Success = false;
                    Log($"编译失败: {compileResult.ErrorSummary}");
                    return result;
                }

                Log($"编译成功 ({sw.ElapsedMilliseconds}ms)");

                // 查找 CodedWorkflowBase 子类
                if (string.IsNullOrEmpty(typeName))
                {
                    foreach (var type in compileResult.Assembly.GetTypes())
                    {
                        if (type.IsSubclassOf(typeof(CodedWorkflowBase)) && !type.IsAbstract)
                        {
                            typeName = type.Name;
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(typeName))
                {
                    result.Success = false;
                    result.ErrorMessage = "找不到 CodedWorkflowBase 的子类";
                    Log(result.ErrorMessage);
                    return result;
                }

                Log($"创建实例: {typeName}");

                // 创建并执行
                var workflow = _compiler.CreateInstance<CodedWorkflowBase>(
                    compileResult.Assembly,
                    typeName
                );

                workflow.LogEvent += (s, msg) => Log(msg);

                Log("开始执行...");
                workflow.Execute();

                sw.Stop();

                result.Success = true;
                result.Result = workflow.Result;
                result.ExecutionTime = sw.Elapsed;

                Log($"执行完成，耗时 {sw.ElapsedMilliseconds}ms");
                if (workflow.Result != null)
                {
                    Log($"返回结果: {workflow.Result}");
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.Exception = ex;
                Log($"执行异常: {ex.Message}");
            }

            return result;
        }

        private void Log(string message)
        {
            LogEvent?.Invoke(this, message);
        }
    }

    /// <summary>
    /// 执行结果
    /// </summary>
    public class ExecutionResult
    {
        public bool Success { get; set; }
        public CompilationResult CompilationResult { get; set; }
        public object Result { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }
    }
}