using Serilog.Events;
using Serilog.Formatting.Compact.Reader;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Features.Sources.Files;

namespace Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

/// <summary>
/// Umbraco-style compact JSON events for comparing the core Log Viewer's dialect with the C#
/// filter (#34): every level, numbers and numeric strings, a boolean, a JSON null, nested and
/// array properties, a trace id, exceptions, a quote in a value and mixed-case text.
/// </summary>
internal static class DialectFixtures
{
    public const string TraceId = "f73dfa962952f5780ed2108020fe2029";

    public static readonly string[] Lines =
    [
        """{"@t":"2026-10-01T12:00:00Z","@mt":"HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Duration} ms","@tr":"f73dfa962952f5780ed2108020fe2029","@sp":"f426677ff16a5408","RequestMethod":"GET","RequestPath":"/api/basket","StatusCode":200,"Duration":65,"RequestId":"0HN501187:00000093","SourceContext":"Umbraco.Cms.Web.Common.Middleware.UmbracoRequestMiddleware"}""",
        """{"@t":"2026-10-01T12:00:01Z","@mt":"Slow request {RequestMethod} {RequestPath} took {Duration} ms","@l":"Warning","RequestMethod":"POST","RequestPath":"/umbraco/surface/contact/submit","StatusCode":500,"Duration":30512,"RequestId":"0HN501187:00000094","SourceContext":"LogExplorer.Site.Diagnostics.SlowRequests"}""",
        """{"@t":"2026-10-01T12:00:02Z","@mt":"An unhandled exception has occurred while executing the request.","@l":"Error","@x":"Microsoft.Data.SqlClient.SqlException (0x80131904): Execution Timeout Expired.\r\n   at Microsoft.Data.SqlClient.SqlCommand.ExecuteReader()","RequestPath":"/umbraco/surface/contact/submit","StatusCode":500,"RequestId":"0HN501187:00000094","SourceContext":"Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware"}""",
        """{"@t":"2026-10-01T12:00:03Z","@mt":"Running recurring background job {JobName}","@l":"Debug","JobName":"TempFileCleanupJob","SourceContext":"Umbraco.Cms.Infrastructure.BackgroundJobs.RecurringBackgroundJobHostedService"}""",
        """{"@t":"2026-10-01T12:00:04Z","@mt":"Basket {ContentId} priced {@Cart} with tags {@Tags}","ContentId":1234,"Cart":{"Total":130.78,"Items":5},"Tags":["new","sale"],"SourceContext":"LogExplorer.Site.Basket.BasketService"}""",
        """{"@t":"2026-10-01T12:00:05Z","@mt":"Boot failed","@l":"Fatal","@x":"System.InvalidOperationException: The runtime could not start.\r\n   at Umbraco.Cms.Core.Runtime.CoreRuntime.StartAsync()","SourceContext":"Umbraco.Cms.Core.Runtime.CoreRuntime"}""",
        """{"@t":"2026-10-01T12:00:06Z","@mt":"Timeout check after {Elapsed} ms","@l":"Verbose","Elapsed":12,"StatusCode":"404","Flag":true,"SourceContext":"Umbraco.Core.Legacy.TimeoutCheck"}""",
        """{"@t":"2026-10-01T12:00:07Z","@mt":"Content {ContentId} published as {DocumentKey}","ContentId":null,"DocumentKey":"9c1a7b52-3c4e-4f0a-8a8f-0f3c2d1e5b6a","SourceContext":"Umbraco.Cms.Core.Services.ContentService"}""",
        """{"@t":"2026-10-01T12:00:08Z","@mt":"Request timeout for {Name}","Name":"O'Brien","Flag":"true","SourceContext":"LogExplorer.Site.Contact"}""",
    ];

    private static readonly LogFile File = new(
        "UmbracoTraceLog.WORM.20261001.json",
        "UmbracoTraceLog.WORM.20261001.json",
        "WORM",
        new DateOnly(2026, 10, 1),
        0
    );

    /// <summary>Each line read the way the files source reads it, with the record it maps to.</summary>
    public static IReadOnlyList<(LogEvent Event, LogRecord Record)> Events { get; } =
    [
        .. Lines.Select(
            (line, index) =>
            {
                LogEvent logEvent = LogEventReader.ReadFromString(line);
                return (logEvent, CompactLogEventMapper.Map(logEvent, File, index));
            }
        ),
    ];
}
