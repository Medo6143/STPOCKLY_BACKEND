using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace Stockly.ArchitectureTests;

/// <summary>
/// Finds public controller actions whose return or parameter types (including generic arguments,
/// e.g. <c>Task&lt;ActionResult&lt;T&gt;&gt;</c>) come from a forbidden namespace (AGENTS.md rule 27).
/// </summary>
internal static class ControllerSignatureInspector
{
    public static IReadOnlyList<string> FindForbiddenUsages(IEnumerable<Type> types, string forbiddenNamespace)
    {
        return types
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(controller => controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => method.DeclaringType?.Assembly == controller.Assembly)
                .SelectMany(method => SignatureTypes(method)
                    .SelectMany(Expand)
                    .Where(type => IsInNamespace(type, forbiddenNamespace))
                    .Select(type => $"{controller.Name}.{method.Name} exposes {type.FullName}")))
            .Distinct()
            .ToList();
    }

    private static IEnumerable<Type> SignatureTypes(MethodInfo method) =>
        method.GetParameters().Select(parameter => parameter.ParameterType).Prepend(method.ReturnType);

    private static IEnumerable<Type> Expand(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is { } elementType)
        {
            foreach (var inner in Expand(elementType))
            {
                yield return inner;
            }
        }

        if (type.IsGenericType)
        {
            foreach (var inner in type.GetGenericArguments().SelectMany(Expand))
            {
                yield return inner;
            }
        }
    }

    // Arrays, pointers and by-refs report their element type's namespace; Expand yields that element on its own.
    private static bool IsInNamespace(Type type, string @namespace) =>
        !type.HasElementType
        && type.Namespace is { } typeNamespace
        && (typeNamespace == @namespace || typeNamespace.StartsWith(@namespace + ".", StringComparison.Ordinal));
}
