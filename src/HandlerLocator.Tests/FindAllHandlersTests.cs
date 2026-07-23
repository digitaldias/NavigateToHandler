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
        public async Task FindAllHandlers_ReturnsEmpty_ForPlainNonGenericInterface()
        {
            // Pins a real gap (see TypeMatchingTests.ImplementsInterface_ReturnsFalseForPlainNonGenericInterface):
            // IsSymbolMatch's IsInherited shortcut is disabled for interfaces, and the
            // ImplementsInterface fallback only matches generic interfaces, so searching for a
            // plain non-generic interface currently finds nothing, even though OrderCreated
            // implements INotification.
            var (fixture, typesDocument) = CreateWorkspace();
            var linePosition = TypesSource.IndexOf("INotification");

            var locator = new FindHandlerLocator(fixture.Solution, typesDocument, linePosition);
            var handlers = (await locator.FindAllHandlers()).ToList();

            Assert.Empty(handlers);
        }

        [Fact]
        public async Task FindAllHandlers_ReturnsEmpty_WhenCursorIsNotOnAType()
        {
            var (fixture, typesDocument) = CreateWorkspace();

            var locator = new FindHandlerLocator(fixture.Solution, typesDocument, 0);
            var handlers = (await locator.FindAllHandlers()).ToList();

            Assert.Empty(handlers);
        }
    }
}
