using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace HandlerLocator.Tests
{
    internal sealed class AdhocWorkspaceFixture
    {
        private readonly AdhocWorkspace _workspace = new AdhocWorkspace();
        private readonly ProjectId _projectId = ProjectId.CreateNewId();
        private readonly List<DocumentId> _documentIds = new List<DocumentId>();

        public AdhocWorkspaceFixture()
        {
            var reference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            var projectInfo = ProjectInfo.Create(
                _projectId,
                VersionStamp.Create(),
                "TestProject",
                "TestProject",
                LanguageNames.CSharp,
                metadataReferences: new[] { reference });

            _workspace.AddProject(projectInfo);
        }

        public Solution Solution => _workspace.CurrentSolution;

        public IEnumerable<Document> Documents
        {
            get
            {
                foreach (var id in _documentIds)
                {
                    yield return Solution.GetDocument(id);
                }
            }
        }

        public Document AddDocument(string name, string source)
        {
            var document = _workspace.AddDocument(_projectId, name, SourceText.From(source));
            _documentIds.Add(document.Id);
            return document;
        }
    }
}
