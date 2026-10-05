using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Umbraco.Community.LogExplorer.Core.Records;
using Umbraco.Community.LogExplorer.Core.Severity;

namespace Umbraco.Community.LogExplorer.Core.Fake;

/// <summary>
/// The UI brief §12 sample hour: a port of the prototype's <c>data()</c> generator
/// (<c>log-explorer-prototype.dc.html</c>, kept with the briefs) producing the same 238 entries, so the UI
/// built against it matches the prototype's screenshots. Keep it in step with the prototype rather
/// than "improving" the data.
/// </summary>
internal static partial class FakeLogData
{
    private const string Machine1 = "wn1xsdwk000EJF";
    private const string Machine2 = "wn1xsdwk000EJK";
    private const string ContactPath = "/umbraco/surface/contact/submit";

    private static readonly LogException SqlTimeout = new(
        "Microsoft.Data.SqlClient.SqlException",
        "Execution Timeout Expired. The timeout period elapsed prior to completion of the operation or the server is not responding.",
        "Microsoft.Data.SqlClient.SqlException (0x80131904): Execution Timeout Expired.\n   at Microsoft.Data.SqlClient.SqlCommand.ExecuteNonQueryAsync(CancellationToken cancellationToken)\n   at Client.Web.Forms.ContactRepository.SaveAsync(ContactSubmission submission)\n   at Client.Web.Controllers.ContactSurfaceController.Submit(ContactFormModel model)\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.Execute(...)"
    );

    private static readonly LogException PublishFailure = new(
        "System.InvalidOperationException",
        "The document could not be published because a mandatory property is empty: contactEmail.",
        "System.InvalidOperationException: The document could not be published because a mandatory property is empty: contactEmail.\n   at Client.Web.Notifications.ValidateContactPageHandler.Handle(ContentPublishingNotification notification)\n   at Umbraco.Cms.Core.Events.EventAggregator.PublishCore(...)"
    );

    /// <summary>Generates the sample hour ending at <paramref name="end"/>.</summary>
    /// <param name="end">Exclusive end of the hour; every entry falls in <c>[end - 1h, end)</c>.</param>
    /// <returns>The entries, oldest first; ties keep generation order.</returns>
    public static IReadOnlyList<LogRecord> Generate(DateTimeOffset end)
    {
        var generator = new Generator(end.AddHours(-1));

        for (int m = 0; m < 60; m++)
        {
            int s = m * 60;
            string requestId = $"0HNFKQ{m:00}A1:00000001";
            double elapsed = Math.Round(4 + ((m * 37) % 61) / 10.0, 4);

            generator.Add(
                s + 9,
                112 + (m * 13) % 300,
                SeverityMap.Info,
                "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed} ms",
                null,
                ("RequestMethod", "GET"),
                ("RequestPath", "/healtz"),
                ("StatusCode", 200),
                ("Elapsed", elapsed),
                ("RequestId", requestId),
                ("MachineName", Machine1),
                ("SourceContext", "Serilog.AspNetCore.RequestLoggingMiddleware")
            );
            generator.Add(
                s + 9,
                118 + (m * 13) % 300,
                SeverityMap.Warn,
                "Anonymous user with ip: {Ip}, requested url: {Url}, with method: {Method} and a response with status code: {StatusCode} was returned",
                null,
                ("Ip", "10.71.96.179"),
                ("Url", "/healtz"),
                ("Method", "GET"),
                ("StatusCode", 200),
                ("RequestId", requestId),
                ("MachineName", Machine1),
                ("SourceContext", "Client.Web.Middleware.AnonymousRequestLogger")
            );
            generator.Add(
                s + 30,
                5 + (m * 7) % 90,
                SeverityMap.Info,
                "Running recurring background job {JobName}",
                null,
                ("JobName", m % 2 == 1 ? "TempFileCleanupJob" : "HealthCheckNotifierJob"),
                ("MachineName", m % 2 == 1 ? Machine1 : Machine2),
                (
                    "SourceContext",
                    "Umbraco.Cms.Infrastructure.BackgroundJobs.RecurringBackgroundJobHostedService"
                )
            );

            if (m % 5 == 0)
            {
                generator.Add(
                    s + 28,
                    401,
                    SeverityMap.Info,
                    "MSAL {MsalVersion} {Platform} TokenEndpoint: {TokenEndpoint}",
                    null,
                    ("MsalVersion", "4.81.0"),
                    ("Platform", "MSAL.NetCore .NET 10.0.11"),
                    ("TokenEndpoint", "****"),
                    ("MachineName", Machine1),
                    ("SourceContext", "Azure.Identity")
                );
                generator.Add(
                    s + 28,
                    402,
                    SeverityMap.Info,
                    "MSAL {MsalVersion} [FindAccessTokenAsync] Discovered {TokenCount} access tokens in cache using partition key: {PartitionKey}",
                    null,
                    ("MsalVersion", "4.81.0"),
                    ("TokenCount", 2),
                    (
                        "PartitionKey",
                        "system_assigned_managed_identity_managed_identity_AppTokenCache"
                    ),
                    ("MachineName", Machine1),
                    ("SourceContext", "Azure.Identity")
                );
            }

            if (m % 7 == 3)
            {
                generator.Add(
                    s + 3,
                    50 + m,
                    SeverityMap.Info,
                    "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed} ms",
                    null,
                    ("RequestMethod", "GET"),
                    ("RequestPath", m % 2 == 1 ? "/" : "/wp-login.php"),
                    ("StatusCode", 404),
                    ("Elapsed", 16.032),
                    ("RequestId", $"0HNFKQ{m:00}B1:00000002"),
                    ("MachineName", m % 2 == 1 ? Machine1 : Machine2),
                    ("SourceContext", "Serilog.AspNetCore.RequestLoggingMiddleware")
                );
            }

            // The SQL timeout incident: two failing form posts a minute for four minutes.
            if (m is >= 41 and <= 44)
            {
                int[] offsets = [12, 37];
                for (int k = 0; k < offsets.Length; k++)
                {
                    int o = offsets[k];
                    string incidentRequestId =
                        $"0HNFKQ{m:00}{(k == 1 ? "D4" : "C4")}:0000000{k + 3}";
                    generator.Add(
                        s + o,
                        210,
                        SeverityMap.Info,
                        "Request starting {Protocol} {RequestMethod} {RequestPath}",
                        null,
                        ("Protocol", "HTTP/2"),
                        ("RequestMethod", "POST"),
                        ("RequestPath", ContactPath),
                        ("RequestId", incidentRequestId),
                        ("MachineName", Machine2),
                        ("SourceContext", "Microsoft.AspNetCore.Hosting.Diagnostics")
                    );
                    generator.Add(
                        s + o + 15,
                        604,
                        SeverityMap.Error,
                        "An unhandled exception has occurred while executing the request.",
                        SqlTimeout,
                        ("RequestPath", ContactPath),
                        ("RequestId", incidentRequestId),
                        ("MachineName", Machine2),
                        (
                            "SourceContext",
                            "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware"
                        )
                    );
                    generator.Add(
                        s + o + 15,
                        611,
                        SeverityMap.Warn,
                        "Slow request {RequestMethod} {RequestPath} took {Elapsed} ms",
                        null,
                        ("RequestMethod", "POST"),
                        ("RequestPath", ContactPath),
                        ("Elapsed", 30012 + m * 3 + k),
                        ("RequestId", incidentRequestId),
                        ("MachineName", Machine2),
                        ("SourceContext", "Client.Web.Diagnostics.SlowRequestLogger")
                    );
                }
            }

            if (m == 43)
            {
                generator.Add(
                    s + 50,
                    77,
                    SeverityMap.Error,
                    "Failed to publish document {ContentId} ({DocumentKey})",
                    PublishFailure,
                    ("ContentId", 1203),
                    ("DocumentKey", "6f1c2a9e-4b7d-4f0a-9a51-2d8e3c7b1f04"),
                    ("MachineName", Machine1),
                    ("SourceContext", "Umbraco.Cms.Core.Services.ContentPublishingService")
                );
            }
        }

        return generator.Build();
    }

    /// <summary>
    /// Renders a message template the way Serilog does: string values in double quotes, numbers
    /// and other values as their JSON text; unknown placeholders stay as written.
    /// </summary>
    internal static string Render(
        string template,
        IReadOnlyDictionary<string, JsonElement> values
    ) =>
        PlaceholderPattern()
            .Replace(
                template,
                match =>
                    values.TryGetValue(match.Groups[1].Value, out JsonElement value)
                        ? value.ValueKind == JsonValueKind.String
                            ? $"\"{value.GetString()}\""
                            : value.GetRawText()
                        : match.Value
            );

    [GeneratedRegex(@"\{(\w+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    private sealed class Generator(DateTimeOffset start)
    {
        private readonly List<LogRecord> _records = [];

        public void Add(
            int second,
            int millisecond,
            string level,
            string template,
            LogException? exception,
            params (string Name, object Value)[] properties
        )
        {
            var attributes = properties.ToDictionary(
                property => property.Name,
                property =>
                    JsonSerializer.SerializeToElement(property.Value, property.Value.GetType())
            );
            string? requestId = properties
                .Where(property => property.Name == "RequestId")
                .Select(property => (string)property.Value)
                .FirstOrDefault();
            int severity = SeverityMap.FromShortName(level);

            _records.Add(
                new LogRecord
                {
                    // "e{n}" in generation order, as in the prototype.
                    Id = "e" + _records.Count.ToString(CultureInfo.InvariantCulture),
                    Timestamp = start.AddSeconds(second).AddMilliseconds(millisecond),
                    SeverityNumber = severity,
                    SeverityText = SeverityMap.ToSerilog(severity),
                    Body = Render(template, attributes),
                    MessageTemplate = template,
                    TemplateHash = TemplateHash.Compute(template),
                    TraceId = requestId is null ? null : TraceIdFor(requestId),
                    Scope = (string)properties.First(p => p.Name == "SourceContext").Value,
                    Exception = exception,
                    Attributes = attributes,
                    Resource = new Dictionary<string, JsonElement>
                    {
                        ["host.name"] = attributes["MachineName"],
                    },
                }
            );
        }

        // The prototype has no trace ids; derive a stable W3C-shaped one (32 hex characters) per
        // request so trace correlation has something to work with.
        private static string TraceIdFor(string requestId) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(requestId))[..16]);

        // Stable sort: entries in the same millisecond keep their generation order.
        public LogRecord[] Build() => _records.OrderBy(record => record.Timestamp).ToArray();
    }
}
