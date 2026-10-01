namespace Umbraco.Community.LogExplorer.Core.Sources;

/// <summary>
/// Thrown when a query's <c>NativeQuery</c> cannot be compiled in the source's language; the API
/// turns it into the <c>invalid_native_query</c> problem code. <see cref="ILogSource.ValidateNative"/>
/// reports the same problem without throwing.
/// </summary>
public sealed class InvalidNativeQueryException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public InvalidNativeQueryException()
        : base("The native query is not valid.") { }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The message.</param>
    public InvalidNativeQueryException(string message)
        : base(message) { }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public InvalidNativeQueryException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>
    /// Zero-based character offset of the error in the native query, or null when the source's
    /// compiler does not say where it is.
    /// </summary>
    public int? Position { get; init; }
}
