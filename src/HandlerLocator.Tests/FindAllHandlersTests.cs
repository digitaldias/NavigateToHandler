using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace HandlerLocator.Tests
{
    public class FindAllHandlersTests
    {
        private const string TypesSource = @"
public interface INotification {}
public class OrderCreated : INotification {}

public class EventBase<T> {}
public class SpecificEvent : EventBase<string> {}

public interface IHandler<T> {}
public class ConcreteHandler : IHandler<string> {}
";

        private const string HandlersSource = @"
public class Handlers
{
    public void OnSpecificEvent(SpecificEvent e) { }
    protected void OnSpecificEventProtected(SpecificEvent e) { }
    internal void OnSpecificEventInternal(SpecificEvent e) { }
    private void OnSpecificEventPrivate(SpecificEvent e) { }
    void OnSpecificEventImplicitPrivate(SpecificEvent e) { }

    public void OnConcreteHandler(ConcreteHandler e) { }
    public void OnOrderCreated(OrderCreated e) { }
}
";

        private static (AdhocWorkspaceFixture Fixture, Document TypesDocument) CreateWorkspace()
        {
            var fixture = new AdhocWorkspaceFixture();
            var typesDoc = fixture.AddDocument("Types.cs", TypesSource);
            fixture.AddDocument("Handlers.cs", HandlersSource);
            return (fixture, fixture.Solution.GetDocument(typesDoc.Id));
        }

        [Fact]
        public async Task FindAllHandlers_MatchesConcreteTypeThroughGenericBase_AndFiltersOnlyPrivateAccess()
        {
            var (fixture, typesDocument) = CreateWorkspace();
            var linePosition = TypesSource.IndexOf("EventBase");

            var locator = new FindHandlerLocator(fixture.Solution, typesDocument, linePosition);
            var handlers = (await locator.FindAllHandlers()).ToList();

            var methodNames = handlers.Select(h => h.MethodName).OrderBy(n => n).ToList();
            Assert.Equal(
                new[]
                {
                    "OnSpecificEvent(...)",
                    "OnSpecificEventInternal(...)",
                    "OnSpecificEventProtected(...)",
                },
                methodNames);

            var publicHandler = handlers.Single(h => h.MethodName == "OnSpecificEvent(...)");
            Assert.Equal("SpecificEvent as EventBase<String>", publicHandler.AsArgument);
            Assert.Equal("Handlers", publicHandler.ClassName);
            Assert.Equal("class", publicHandler.ClassType);
        }

        [Fact]
        public async Task FindAllHandlers_MatchesConcreteTypeThroughGenericInterface()
        {
            var (fixture, typesDocument) = CreateWorkspace();
            var linePosition = TypesSource.IndexOf("IHandler<string>");

            var locator = new FindHandlerLocator(fixture.Solution, typesDocument, linePosition);
            var handlers = (await locator.FindAllHandlers()).ToList();

            var handler = Assert.Single(handlers);
            Assert.Equal("OnConcreteHandler(...)", handler.MethodName);
        }

        [Fact]
        public async Task FindAllHandlers_FindsHandler_ForPlainNonGenericInterface()
        {
            var (fixture, typesDocument) = CreateWorkspace();
            var linePosition = TypesSource.IndexOf("INotification");

            var locator = new FindHandlerLocator(fixture.Solution, typesDocument, linePosition);
            var handlers = (await locator.FindAllHandlers()).ToList();

            var handler = Assert.Single(handlers);
            Assert.Equal("OnOrderCreated(...)", handler.MethodName);
        }

        [Fact]
        public async Task FindAllHandlers_CollectsAllMatches_AcrossManyDocuments()
        {
            var (fixture, typesDocument) = CreateWorkspace();

            const int documentCount = 30;
            for (int i = 0; i < documentCount; i++)
            {
                fixture.AddDocument($"ScaleHandlers{i}.cs", $@"
public class ScaleHandler{i}
{{
    public void OnScaleEvent{i}(SpecificEvent e) {{ }}
    public void Ignore{i}(ConcreteHandler c) {{ }}
}}
");
            }

            var currentTypesDocument = fixture.Solution.GetDocument(typesDocument.Id);
            var linePosition = TypesSource.IndexOf("EventBase");
            var locator = new FindHandlerLocator(fixture.Solution, currentTypesDocument, linePosition);
            var handlers = (await locator.FindAllHandlers()).ToList();

            var scaleHandlerNames = handlers
                .Select(h => h.MethodName)
                .Where(n => n.StartsWith("OnScaleEvent"))
                .ToList();

            Assert.Equal(documentCount, scaleHandlerNames.Count);
            Assert.Equal(documentCount, scaleHandlerNames.Distinct().Count());
            for (int i = 0; i < documentCount; i++)
            {
                Assert.Contains($"OnScaleEvent{i}(...)", scaleHandlerNames);
            }
        }

        [Fact]
        public async Task FindAllHandlers_ReturnsEmpty_WhenCursorIsNotOnAType()
        {
            // A document with no type declarations at all -- position 0 in TypesSource
            // does not work for this: Roslyn's FindNode ties position 0 to the first
            // declaration in the file (INotification) rather than to "nothing".
            var fixture = new AdhocWorkspaceFixture();
            var emptyDoc = fixture.AddDocument("Empty.cs", "// no types here\n");
            var emptyDocument = fixture.Solution.GetDocument(emptyDoc.Id);

            var locator = new FindHandlerLocator(fixture.Solution, emptyDocument, 0);
            var handlers = (await locator.FindAllHandlers()).ToList();

            Assert.Empty(handlers);
        }
    }
}
