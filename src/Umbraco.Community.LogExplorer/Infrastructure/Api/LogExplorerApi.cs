namespace Umbraco.Community.LogExplorer.Infrastructure.Api;

/// <summary>Names shared by the package's Management API (ADR 0009).</summary>
public static class LogExplorerApi
{
    /// <summary>
    /// The API name: the route segment (<c>/umbraco/log-explorer/api/v1</c>) and the Swagger
    /// document name (<c>/umbraco/swagger/log-explorer/swagger.json</c>).
    /// </summary>
    public const string Name = "log-explorer";

    /// <summary>The ProblemDetails <c>code</c> values the API returns (BRIEF §11.1).</summary>
    public static class ProblemCodes
    {
        /// <summary>No source has the requested alias.</summary>
        public const string SourceNotFound = "source_not_found";

        /// <summary>The source exists but the user may not use it.</summary>
        public const string ForbiddenSource = "forbidden_source";

        /// <summary>The source does not support the requested feature or operator.</summary>
        public const string UnsupportedFeature = "unsupported_feature";

        /// <summary>The time range is longer than the source allows.</summary>
        public const string RangeTooLarge = "range_too_large";

        /// <summary>
        /// The query is malformed: an unknown relative range, <c>From</c> not before <c>To</c>, a
        /// cursor from another source, a page size below 1, an invalid regular expression, or search box input over
        /// the <c>POST /parse</c> length limit.
        /// </summary>
        public const string InvalidQuery = "invalid_query";
    }
}
