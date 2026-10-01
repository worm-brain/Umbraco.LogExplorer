using System.Diagnostics.CodeAnalysis;

namespace Umbraco.Community.LogExplorer.Core.Records;

/// <summary>An exception attached to a <see cref="LogRecord"/>; any part may be missing.</summary>
/// <param name="Type">Full type name, for example <c>System.InvalidOperationException</c>.</param>
/// <param name="Message">The exception message.</param>
/// <param name="StackTrace">The stack trace as text, frames separated by new lines.</param>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Describes a logged exception rather than being one; the name is the BRIEF §8.1 contract."
)]
public sealed record LogException(string? Type, string? Message, string? StackTrace);
