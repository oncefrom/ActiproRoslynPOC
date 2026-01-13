using Microsoft.CodeAnalysis;

namespace ActiproRoslynPOC.Models
{
    public class DiagnosticInfo
    {
        public string Id { get; set; }
        public string Message { get; set; }
        public DiagnosticSeverity Severity { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }

        public string SeverityText
        {
            get
            {
                switch (Severity)
                {
                    case DiagnosticSeverity.Error:
                        return "错误";
                    case DiagnosticSeverity.Warning:
                        return "警告";
                    case DiagnosticSeverity.Info:
                        return "信息";
                    default:
                        return "隐藏";
                }
            }
        }

        public override string ToString()
        {
            return $"[{SeverityText}] 第 {Line} 行: {Message}";
        }
    }
}