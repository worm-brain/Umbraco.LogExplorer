namespace Umbraco.Community.LogExplorer.Features.Context;

/// <summary>
/// A source has no record with the requested id (404 <c>record_not_found</c>). Providers signal
/// this with <see cref="KeyNotFoundException"/> (ADR 0008), which the problem filter already maps
/// to <c>source_not_found</c>, so record endpoints rethrow it as this type to keep the two apart.
/// </summary>
public sealed class RecordNotFoundException : Exception
{
    /// <summary>Creates the exception with no message.</summary>
    public RecordNotFoundException() { }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">Why the record was not found, shown as the ProblemDetails detail.</param>
    public RecordNotFoundException(string message)
        : base(message) { }

    /// <summary>Creates the exception wrapping the provider's own exception.</summary>
    /// <param name="message">Why the record was not found, shown as the ProblemDetails detail.</param>
    /// <param name="innerException">The provider's <see cref="KeyNotFoundException"/>.</param>
    public RecordNotFoundException(string message, Exception innerException)
        : base(message, innerException) { }
}
