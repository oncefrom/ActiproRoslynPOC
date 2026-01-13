using System;
using System.IO;
using System.Text;

namespace ActiproRoslynPOC.Services
{
    public class ConsoleRedirectWriter : TextWriter
    {
        private readonly Action<string> _outputAction;

        public ConsoleRedirectWriter(Action<string> outputAction)
        {
            _outputAction = outputAction;
        }

        // 核心：重写 Write(string)
        public override void Write(string value)
        {
            _outputAction?.Invoke(value);
        }

        // 重写 WriteLine(string)
        public override void WriteLine(string value)
        {
            _outputAction?.Invoke(value + Environment.NewLine);
        }

        public override Encoding Encoding => Encoding.UTF8;
    }
}