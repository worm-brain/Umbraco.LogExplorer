namespace Umbraco.Community.LogExplorer.Core.Sources;

/// <summary>
/// Thrown when a user asks for a source that exists but is hidden from them; the API turns it into
/// the <c>forbidden_source</c> problem code.
/// </summary>
public sealed class ForbiddenSourceException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public ForbiddenSourceException()
        : base("The log source is not available to this user.") { }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The message.</param>
    public ForbiddenSourceException(string message)
        : base(message) { }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public ForbiddenSourceException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>Alias of the refused source, when known.</summary>
    public string? SourceAlias { get; init; }
}
