using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ShortP2P;

/// <summary>BCL-neutral argument checks (net48 does not have ArgumentNullException.ThrowIfNull).</summary>
internal static class Require
{
    public static void NotNull(
        [NotNull] object? argument,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        if (argument is null)
            throw new ArgumentNullException(paramName);
    }

    public static void NotNullOrWhiteSpace(
        [NotNull] string? argument,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        if (argument is null || string.IsNullOrWhiteSpace(argument))
            throw new ArgumentException(
                "The value cannot be an empty string or composed entirely of whitespace.",
                paramName);
    }
}
