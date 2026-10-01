using Umbraco.Community.LogExplorer.Core.Filtering;
using Umbraco.Community.LogExplorer.Core.Query;

namespace Umbraco.Community.LogExplorer.Features.Sources.Files;

/// <summary>
/// The costly parts of a <see cref="Core.Records.LogRecord"/> that
/// <see cref="CompactLogEventMapper"/> can leave out, so the pager can test a filter against a
/// partial record before paying for the whole mapping (ADR 0017). Id, timestamp, severity, trace
/// and span ids and scope are cheap and always mapped.
/// </summary>
[Flags]
internal enum RecordParts
{
    /// <summary>Only the cheap members.</summary>
    None = 0,

    /// <summary><c>Body</c>: the rendered message.</summary>
    Body = 1,

    /// <summary><c>Exception</c>: <c>@x</c> split into type, message and stack trace.</summary>
    Exception = 2,

    /// <summary><c>MessageTemplate</c> and <c>TemplateHash</c> (an MD5 of the template).</summary>
    Template = 4,

    /// <summary><c>Attributes</c>: every property as typed JSON.</summary>
    Attributes = 8,

    /// <summary><c>Resource</c>: <c>host.name</c>.</summary>
    Resource = 16,

    /// <summary>The whole record.</summary>
    All = Body | Exception | Template | Attributes | Resource,
}

/// <summary>
/// Works out which <see cref="RecordParts"/> <see cref="LogRecordFilter"/> reads for a filter,
/// following <see cref="LogFields.Resolve"/>'s field mapping exactly.
/// </summary>
internal static class FilterRecordParts
{
    /// <summary>The parts a filter reads; a level set needs none beyond the cheap members.</summary>
    /// <param name="filter">The filter; null reads nothing.</param>
    /// <returns>
    /// The parts whose values can change the filter's result. Mapping these (plus the cheap
    /// members) gives the same match as mapping the whole record. A node type this does not
    /// know needs <see cref="RecordParts.All"/>.
    /// </returns>
    public static RecordParts For(FilterNode? filter) =>
        filter switch
        {
            null => RecordParts.None,
            AndNode and => and.Children.Aggregate(
                RecordParts.None,
                (parts, child) => parts | For(child)
            ),
            OrNode or => or.Children.Aggregate(
                RecordParts.None,
                (parts, child) => parts | For(child)
            ),
            NotNode not => For(not.Child),
            // Text search reads the body and the exception message (LogRecordFilter.MatchesText).
            TextNode => RecordParts.Body | RecordParts.Exception,
            ConditionNode condition => ForField(condition.Field),
            // LogRecordFilter throws on a node type it does not know; mapping everything leaves
            // that to happen where it always did.
            _ => RecordParts.All,
        };

    // Mirrors LogFields.Resolve: "@resource." paths read Resource, the portable "@" fields read
    // their member, and anything else, including an unknown "@" name, is an attribute path.
    private static RecordParts ForField(string field)
    {
        if (!field.StartsWith('@'))
        {
            return RecordParts.Attributes;
        }

        if (field.StartsWith(LogFields.ResourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return RecordParts.Resource;
        }

        return field.ToUpperInvariant() switch
        {
            "@TIMESTAMP" or "@SEVERITY" or "@SCOPE" or "@TRACEID" or "@SPANID" => RecordParts.None,
            "@BODY" => RecordParts.Body,
            "@TEMPLATE" => RecordParts.Template,
            "@EXCEPTION.TYPE" or "@EXCEPTION.MESSAGE" => RecordParts.Exception,
            _ => RecordParts.Attributes,
        };
    }
}
