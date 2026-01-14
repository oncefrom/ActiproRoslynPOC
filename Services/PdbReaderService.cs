using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ActiproRoslynPOC.Services
{
    /// <summary>
    /// PDB 读取服务 - 从 Portable PDB 文件中提取调试信息
    /// </summary>
    public class PdbReaderService
    {
        private MetadataReader _pdbReader;
        private Dictionary<string, MethodDebugInfo> _methodDebugInfo;

        /// <summary>
        /// 从文件加载 PDB
        /// </summary>
        public bool LoadFromFile(string pdbPath)
        {
            try
            {
                if (!File.Exists(pdbPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[PdbReader] PDB 文件不存在: {pdbPath}");
                    return false;
                }

                var pdbBytes = File.ReadAllBytes(pdbPath);
                return LoadFromBytes(pdbBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PdbReader] 加载 PDB 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从字节数组加载 PDB
        /// </summary>
        public bool LoadFromBytes(byte[] pdbData)
        {
            try
            {
                using (var stream = new MemoryStream(pdbData))
                {
                    var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
                    _pdbReader = provider.GetMetadataReader();
                }
                BuildMethodDebugInfo();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PdbReader] 从字节加载 PDB 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 构建方法调试信息映射
        /// </summary>
        private void BuildMethodDebugInfo()
        {
            _methodDebugInfo = new Dictionary<string, MethodDebugInfo>();

            foreach (var methodHandle in _pdbReader.MethodDebugInformation)
            {
                try
                {
                    var methodDebugData = _pdbReader.GetMethodDebugInformation(methodHandle);

                    // 获取序列点 (行号到 IL 偏移量的映射)
                    var sequencePoints = methodDebugData.GetSequencePoints().ToList();
                    if (!sequencePoints.Any()) continue;

                    // 获取方法名 (需要从 MethodDefinition 中获取)
                    // 注意: MethodDebugInformation 和 MethodDefinition 使用相同的 token
                    var methodToken = MetadataTokens.GetToken(methodHandle);
                    var methodName = $"Method_{methodToken:X8}"; // 临时使用 token 作为键

                    var debugInfo = new MethodDebugInfo
                    {
                        MethodToken = methodToken
                    };

                    // 解析序列点
                    foreach (var sp in sequencePoints)
                    {
                        if (sp.IsHidden) continue;
                        if (sp.StartLine == 0xFEEFEE) continue; // 隐藏序列点

                        // 获取文档名称
                        var document = _pdbReader.GetDocument(sp.Document);
                        var documentName = _pdbReader.GetString(document.Name);

                        debugInfo.SequencePoints.Add(new SequencePointInfo
                        {
                            ILOffset = sp.Offset,
                            StartLine = sp.StartLine,
                            StartColumn = sp.StartColumn,
                            EndLine = sp.EndLine,
                            EndColumn = sp.EndColumn,
                            DocumentName = documentName
                        });

                        // 建立行号到 IL 偏移量的映射
                        if (!debugInfo.LineToILOffset.ContainsKey(sp.StartLine))
                        {
                            debugInfo.LineToILOffset[sp.StartLine] = sp.Offset;
                        }
                    }

                    // 获取局部变量作用域
                    BuildLocalVariableInfo(methodHandle, debugInfo);

                    _methodDebugInfo[methodName] = debugInfo;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PdbReader] 处理方法调试信息失败: {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"[PdbReader] 已解析 {_methodDebugInfo.Count} 个方法的调试信息");
        }

        /// <summary>
        /// 构建局部变量信息
        /// </summary>
        private void BuildLocalVariableInfo(MethodDebugInformationHandle methodHandle, MethodDebugInfo debugInfo)
        {
            try
            {
                // 查找与该方法关联的局部作用域
                foreach (var scopeHandle in _pdbReader.LocalScopes)
                {
                    var scope = _pdbReader.GetLocalScope(scopeHandle);

                    // 检查该作用域是否属于当前方法
                    var scopeMethod = scope.Method;
                    if (!scopeMethod.Equals(methodHandle)) continue;

                    // 解析局部变量
                    foreach (var varHandle in scope.GetLocalVariables())
                    {
                        var variable = _pdbReader.GetLocalVariable(varHandle);

                        var varInfo = new LocalVariableInfo
                        {
                            SlotIndex = variable.Index,
                            Name = _pdbReader.GetString(variable.Name),
                            StartOffset = scope.StartOffset,
                            EndOffset = scope.EndOffset,
                            Attributes = variable.Attributes
                        };

                        debugInfo.LocalVariables.Add(varInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PdbReader] 构建局部变量信息失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取所有可执行行号
        /// </summary>
        public List<int> GetAllExecutableLines()
        {
            var lines = new HashSet<int>();
            foreach (var methodInfo in _methodDebugInfo.Values)
            {
                foreach (var sp in methodInfo.SequencePoints)
                {
                    lines.Add(sp.StartLine);
                }
            }
            return lines.OrderBy(l => l).ToList();
        }

        /// <summary>
        /// 根据行号获取 IL 偏移量
        /// </summary>
        public int GetILOffsetForLine(int lineNumber)
        {
            foreach (var methodInfo in _methodDebugInfo.Values)
            {
                if (methodInfo.LineToILOffset.TryGetValue(lineNumber, out var offset))
                {
                    return offset;
                }
            }
            return -1;
        }

        /// <summary>
        /// 根据 IL 偏移量获取行号
        /// </summary>
        public int GetLineNumberForILOffset(int ilOffset)
        {
            foreach (var methodInfo in _methodDebugInfo.Values)
            {
                var sp = methodInfo.SequencePoints
                    .FirstOrDefault(s => s.ILOffset == ilOffset);
                if (sp != null)
                    return sp.StartLine;
            }
            return -1;
        }

        /// <summary>
        /// 获取指定方法的调试信息
        /// </summary>
        public MethodDebugInfo GetMethodDebugInfo(string methodName)
        {
            return _methodDebugInfo.TryGetValue(methodName, out var info) ? info : null;
        }

        /// <summary>
        /// 获取所有方法的调试信息
        /// </summary>
        public Dictionary<string, MethodDebugInfo> GetAllMethodDebugInfo()
        {
            return _methodDebugInfo;
        }
    }

    /// <summary>
    /// 方法调试信息
    /// </summary>
    public class MethodDebugInfo
    {
        public int MethodToken { get; set; }
        public List<SequencePointInfo> SequencePoints { get; set; } = new List<SequencePointInfo>();
        public Dictionary<int, int> LineToILOffset { get; set; } = new Dictionary<int, int>();
        public List<LocalVariableInfo> LocalVariables { get; set; } = new List<LocalVariableInfo>();
    }

    /// <summary>
    /// 序列点信息 (源代码位置到 IL 偏移量的映射)
    /// </summary>
    public class SequencePointInfo
    {
        public int ILOffset { get; set; }
        public int StartLine { get; set; }
        public int StartColumn { get; set; }
        public int EndLine { get; set; }
        public int EndColumn { get; set; }
        public string DocumentName { get; set; }
    }

    /// <summary>
    /// 局部变量信息
    /// </summary>
    public class LocalVariableInfo
    {
        public int SlotIndex { get; set; }          // 变量槽位索引 (V_0, V_1...)
        public string Name { get; set; }             // 变量名称
        public int StartOffset { get; set; }         // 作用域开始 IL 偏移量
        public int EndOffset { get; set; }           // 作用域结束 IL 偏移量
        public LocalVariableAttributes Attributes { get; set; }

        /// <summary>
        /// 检查变量是否在指定的 IL 偏移量处有效
        /// </summary>
        public bool IsInScope(int ilOffset)
        {
            return ilOffset >= StartOffset && ilOffset < EndOffset;
        }
    }
}
