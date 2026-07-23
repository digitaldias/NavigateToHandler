using Microsoft.CodeAnalysis;
using Xunit;

namespace HandlerLocator.Tests
{
    public class TypeMatchingTests
    {
        private const string Source = @"
public interface INotification {}
public class OrderCreated : INotification {}
public class Unrelated {}

public class EventBase<T> {}
public class SpecificEvent : EventBase<string> {}

public interface IHandler<T> {}
public class ConcreteHandler : IHandler<string> {}
public class OtherHandler : IHandler<int> {}
";

        [Fact]
        public void IsInherited_MatchesConcreteTypeThroughGenericBase()
        {
            var compilation = CompilationHelper.Compile(Source);
            var eventBase = compilation.GetType("EventBase`1");
            var specificEvent = compilation.GetType("SpecificEvent");

            Assert.True(TypeSymbolMatcher.IsInherited(eventBase, specificEvent));
        }

        [Fact]
        public void IsInherited_MatchesClassImplementingInterface()
        {
            var compilation = CompilationHelper.Compile(Source);
            var notification = compilation.GetType("INotification");
            var orderCreated = compilation.GetType("OrderCreated");

            Assert.True(TypeSymbolMatcher.IsInherited(notification, orderCreated));
        }

        [Fact]
        public void IsInherited_ReturnsFalseForUnrelatedType()
        {
            var compilation = CompilationHelper.Compile(Source);
            var notification = compilation.GetType("INotification");
            var unrelated = compilation.GetType("Unrelated");

            Assert.False(TypeSymbolMatcher.IsInherited(notification, unrelated));
        }

        [Fact]
        public void IsInherited_ReturnsFalseWhenCandidateIsSpecialType()
        {
            var compilation = CompilationHelper.Compile(Source);
            var notification = compilation.GetType("INotification");
            var stringType = compilation.GetSpecialType(SpecialType.System_String);

            Assert.False(TypeSymbolMatcher.IsInherited(notification, stringType));
        }

        [Fact]
        public void ImplementsInterface_MatchesSameGenericTypeArgument()
        {
            var compilation = CompilationHelper.Compile(Source);
            var handlerOfString = compilation.GetType("IHandler`1").Construct(compilation.GetSpecialType(SpecialType.System_String));
            var concreteHandler = compilation.GetType("ConcreteHandler");

            Assert.True(TypeSymbolMatcher.ImplementsInterface(handlerOfString, concreteHandler));
        }

        [Fact]
        public void ImplementsInterface_ReturnsFalseForPlainNonGenericInterface()
        {
            // Pins a real gap: ImplementsInterfaceWithMatchingTypeArguments only matches
            // generic interfaces (left.IsGenericType && implementedInterface.IsGenericType);
            // a non-generic exact interface implementation falls through to `return false`.
            var compilation = CompilationHelper.Compile(Source);
            var notification = compilation.GetType("INotification");
            var orderCreated = compilation.GetType("OrderCreated");

            Assert.False(TypeSymbolMatcher.ImplementsInterface(notification, orderCreated));
        }

        [Fact]
        public void ImplementsInterface_ReturnsFalseForMismatchedGenericTypeArgument()
        {
            var compilation = CompilationHelper.Compile(Source);
            var handlerOfInt = compilation.GetType("IHandler`1").Construct(compilation.GetSpecialType(SpecialType.System_Int32));
            var concreteHandler = compilation.GetType("ConcreteHandler");

            Assert.False(TypeSymbolMatcher.ImplementsInterface(handlerOfInt, concreteHandler));
        }

        [Theory]
        [InlineData(SpecialType.System_Boolean, true)]
        [InlineData(SpecialType.System_Int32, true)]
        [InlineData(SpecialType.System_String, true)]
        [InlineData(SpecialType.System_Object, true)]
        [InlineData(SpecialType.System_DateTime, false)]
        [InlineData(SpecialType.None, false)]
        public void IsBuiltInType_MatchesDocumentedSpecialTypes(SpecialType specialType, bool expected)
        {
            var compilation = CompilationHelper.Compile(Source);
            var typeSymbol = specialType == SpecialType.None
                ? (ITypeSymbol)compilation.GetType("Unrelated")
                : compilation.GetSpecialType(specialType);

            Assert.Equal(expected, FindHandlerLocator.IsBuiltInType(typeSymbol));
        }
    }
}
