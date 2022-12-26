namespace HandlerLocator
{
    public class IdentifiedHandler
    {
        public string TypeName { get; set; }

        public string SourceFile { get; set; }

        public int LineNumber { get; set; }
    }
}