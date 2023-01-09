using System.Diagnostics;

namespace HandlerLocator
{
    [DebuggerDisplay("{TypeName}.{PublicMethod}({LineNumber}, {Column})")]
    public class IdentifiedHandler
    {
        public string TypeToFind { get; set; }

        public string TypeName { get; set; }

        public string SourceFile { get; set; }

        public int LineNumber { get; set; }
        public string PublicMethod { get; set; }
        public int Column { get; set; }
        public string Fill { get; set; }
        public string DisplaySourceFile { get; set; }
        public int CaretPosition { get; set; }
    }
}