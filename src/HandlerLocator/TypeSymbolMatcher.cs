using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace HandlerLocator
{
    /// <summary>
    /// Pure type-symbol equality/inheritance/generic-argument matching used to decide whether a
    /// method parameter type matches the type the user searched for. Split out of
    /// <see cref="FindHandlerLocator"/> because it is a self-contained, mutually-recursive engine
    /// with no dependency on the enclosing class' Solution/Document state.
    /// </summary>
    internal static class TypeSymbolMatcher
    {
        internal static bool ArgumentsAreTheSame(INamedTypeSymbol left, INamedTypeSymbol right)
        {
            if (left.TypeArguments.Length != right.TypeArguments.Length)
                return false;

            bool AllMatch = true;
            for (int i = 0; i < left.TypeArguments.Length; i++)
            {
                if (!SymbolEqualityComparer.Default.Equals(left.TypeArguments[i], right.TypeArguments[i]))
                {
                    AllMatch = false;
                    break;
                }
            }
            return AllMatch;
        }

        internal static bool IsInherited(ITypeSymbol typeToMatch, ITypeSymbol candidateType)
        {
            if (candidateType.SpecialType != SpecialType.None)
                return false;

            if ((typeToMatch is INamedTypeSymbol) == false || (candidateType is INamedTypeSymbol) == false)
                return false;

            var leftSide = typeToMatch as INamedTypeSymbol;
            var rightSide = candidateType as INamedTypeSymbol;

            return MatchesGenericBaseType(leftSide, rightSide) || MatchesInterface(leftSide, rightSide);
        }

        private static bool MatchesGenericBaseType(INamedTypeSymbol leftSide, INamedTypeSymbol rightSide)
        {
            if (!leftSide.IsGenericType || rightSide.BaseType == null)
                return false;

            var baseType = rightSide.BaseType;

            if (baseType.IsGenericType)
            {
                Debug.WriteLine($"Comparing {leftSide.Name} to {baseType.Name}");
                var argumentsAreTheSame = ArgumentsAreTheSame(leftSide, baseType);
                if (!argumentsAreTheSame && baseType.BaseType != null)
                {
                    return ArgumentsAreTheSame(leftSide, baseType.OriginalDefinition);
                }

                return argumentsAreTheSame;
            }

            return MatchesNonGenericBaseType(leftSide, rightSide, baseType);
        }

        private static bool MatchesNonGenericBaseType(INamedTypeSymbol leftSide, INamedTypeSymbol rightSide, INamedTypeSymbol baseType)
        {
            if (AreEqual(leftSide, rightSide))
                return true;

            if (AreEqual(leftSide, baseType.OriginalDefinition))
                return true;

            return AreEqual(leftSide.OriginalDefinition, baseType.OriginalDefinition);
        }

        private static bool MatchesInterface(INamedTypeSymbol leftSide, INamedTypeSymbol rightSide)
        {
            if (leftSide.TypeKind != TypeKind.Interface || !rightSide.AllInterfaces.Any())
                return false;

            foreach (var iface in rightSide.AllInterfaces)
            {
                if (AreEqual(leftSide, iface))
                {
                    return ArgumentsAreTheSame(leftSide, iface);
                }
            }

            return false;
        }

        internal static bool AreEqual(INamedTypeSymbol left, INamedTypeSymbol right)
        {
            if (left == null || right == null)
            {
                return false;
            }
            return SymbolEqualityComparer.Default.Equals(left.WithNullableAnnotation(NullableAnnotation.NotAnnotated), right.WithNullableAnnotation(NullableAnnotation.NotAnnotated));
        }

        internal static bool AreEqual(ITypeSymbol left, ITypeSymbol right)
        {
            return SymbolEqualityComparer.Default.Equals(left, right);
        }

        internal static bool ImplementsInterface(INamedTypeSymbol left, INamedTypeSymbol right)
        {
            // Check if 'right' is an interface
            if (right.TypeKind == TypeKind.Interface)
            {
                // Return true if there is an exact match
                if (SymbolEqualityComparer.Default.Equals(left, right))
                {
                    return true;
                }
            }

            // Check if 'right' or any of its base types implement 'left' with matching type arguments
            INamedTypeSymbol currentType = right;

            while (currentType != null)
            {
                // Check if the current type directly implements the interface with matching type arguments
                foreach (var interfaceType in currentType.AllInterfaces)
                {
                    if (ImplementsInterfaceWithMatchingTypeArguments(left, interfaceType))
                    {
                        return true;
                    }
                }

                // Move to the base type
                currentType = currentType.BaseType;
            }

            return false;
        }

        internal static bool ImplementsInterfaceWithMatchingTypeArguments(INamedTypeSymbol left, INamedTypeSymbol implementedInterface)
        {
            // Check if both interfaces have the same original definition
            if (!AreEqual(left.OriginalDefinition, implementedInterface.OriginalDefinition))
            {
                return false;
            }

            // Non-generic interfaces have no type arguments to compare -- same original
            // definition is the full match.
            if (!left.IsGenericType)
            {
                return true;
            }

            // Check if both are generic and have the same number of type arguments.
            // A non-null verdict here is authoritative: false means a definite mismatch
            // (skip the recursive fallback below), true means a match was found.
            // Null means "no verdict yet" -- fall through to the recursive base-interface check.
            var genericArgumentsMatch = TryMatchGenericTypeArguments(left, implementedInterface);
            if (genericArgumentsMatch.HasValue)
            {
                return genericArgumentsMatch.Value;
            }

            // Recursively check base interfaces of the implemented interface
            foreach (var baseInterface in implementedInterface.AllInterfaces)
            {
                if (ImplementsInterfaceWithMatchingTypeArguments(left, baseInterface))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool? TryMatchGenericTypeArguments(INamedTypeSymbol left, INamedTypeSymbol implementedInterface)
        {
            if (!implementedInterface.IsGenericType)
            {
                return null;
            }

            var leftTypeArguments = left.TypeArguments;
            var implementedTypeArguments = implementedInterface.TypeArguments;

            if (leftTypeArguments.Length != implementedTypeArguments.Length)
            {
                return false;
            }

            for (int i = 0; i < leftTypeArguments.Length; i++)
            {
                if (TypeArgumentAtIndexMatches(left, leftTypeArguments[i], implementedTypeArguments[i]))
                {
                    return true;
                }
            }

            return null;
        }

        private static bool TypeArgumentAtIndexMatches(INamedTypeSymbol left, ITypeSymbol leftTypeArgument, ITypeSymbol implementedTypeArgument)
        {
            if (TypeArgumentsMatch(leftTypeArgument, implementedTypeArgument))
            {
                return true;
            }

            if (implementedTypeArgument.BaseType == null || implementedTypeArgument.BaseType.Kind != SymbolKind.NamedType)
            {
                return false;
            }

            if (leftTypeArgument.TypeKind == TypeKind.TypeParameter)
            {
                return true;
            }

            foreach (var rightInterface in implementedTypeArgument.BaseType.AllInterfaces)
            {
                if (TypeArgumentsMatch(left, rightInterface))
                {
                    return true;
                }
            }

            return TypeArgumentsMatch(leftTypeArgument, implementedTypeArgument.BaseType);
        }

        internal static bool TypeArgumentsMatch(ITypeSymbol leftTypeArgument, ITypeSymbol implementedTypeArgument)
        {
            // Check if the type arguments are equal
            if (AreEqual(leftTypeArgument, implementedTypeArgument))
            {
                return true;
            }

            // Only named types can have nested type arguments to compare
            if (!(leftTypeArgument is INamedTypeSymbol leftNamedType && implementedTypeArgument is INamedTypeSymbol implementedNamedType))
            {
                return false;
            }

            // Both sides must be generic for there to be nested type arguments
            if (!leftNamedType.IsGenericType || !implementedNamedType.IsGenericType)
            {
                return false;
            }

            // Recursively check their nested type arguments
            return NestedTypeArgumentsMatch(leftNamedType.TypeArguments, implementedNamedType.TypeArguments);
        }

        private static bool NestedTypeArgumentsMatch(ImmutableArray<ITypeSymbol> leftTypeArguments, ImmutableArray<ITypeSymbol> implementedTypeArguments)
        {
            if (leftTypeArguments.Length != implementedTypeArguments.Length)
            {
                return false;
            }

            for (int i = 0; i < leftTypeArguments.Length; i++)
            {
                if (!TypeArgumentsMatch(leftTypeArguments[i], implementedTypeArguments[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
