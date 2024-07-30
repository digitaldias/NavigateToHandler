using System.Diagnostics;

namespace HandlerLocator
{
    [DebuggerDisplay("{ClassName}.{MethodName}({LineNumber}, {Column})")]
    public class IdentifiedHandler
    {
        public string TypeToFind { get; set; }

        public string ClassName { get; set; }

        public string SourceFile { get; set; }

        public int LineNumber { get; set; }
        public string MethodName { get; set; }
        public int Column { get; set; }
        public string Fill { get; set; }
        public string DisplaySourceFile { get; set; }
        public int CaretPosition { get; set; }
        public string AsArgument { get; set; }
        public string ClassType { get; set; }
    }
}