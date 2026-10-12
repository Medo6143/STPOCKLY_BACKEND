namespace Stockly.Application.Validation;

/// <summary>Turns a validator property path (<c>Items[2].Quantity</c>) into the contract's field key (<c>items[2].quantity</c>).</summary>
public static class FieldPath
{
    public static string ToCamelCase(string propertyPath)
    {
        ArgumentNullException.ThrowIfNull(propertyPath);

        return string.Join('.', propertyPath.Split('.').Select(LowerFirst));
    }

    private static string LowerFirst(string segment) =>
        segment.Length == 0 || char.IsLower(segment[0])
            ? segment
            : string.Concat(char.ToLowerInvariant(segment[0]).ToString(), segment.AsSpan(1));
}
