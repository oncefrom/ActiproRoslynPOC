using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ActiproRoslynPOC.Models
{
    public class CompilationResult
    {
        public bool Success { get; set; }
        public Assembly Assembly { get; set; }
        public byte[] AssemblyBytes { get; set; }
        public byte[] PdbBytes { get; set; }  // 新增: PDB 字节数据
        public List<DiagnosticInfo> Diagnostics { get; set; } = new List<DiagnosticInfo>();

        public string ErrorSummary
        {
            get
            {
                int errors = Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
                int warnings = Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
                return $"{errors} 个错误, {warnings} 个警告";
            }
        }
    }
}