using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace LogExplorer.Samples.LogGenerator;

/// <summary>What <see cref="BulkLogWriter"/> writes and where.</summary>
internal sealed record BulkLogSettings
{
    /// <summary>The log directory to write into; created if missing.</summary>
    public required string Directory { get; init; }

    /// <summary>
    /// Roughly how many bytes to write across every machine. The writer spaces events so the
    /// timeline fills <see cref="Span"/> with about this much; the result lands within a few
    /// percent of it.
    /// </summary>
    public required long TargetBytes { get; init; }

    /// <summary>
    /// Where the timeline ends; it runs from <c>End - Span</c>. The last scenario starts before
    /// this but can finish its lines a few milliseconds after it.
    /// </summary>
    public required DateTimeOffset End { get; init; }

    /// <summary>How much simulated time the events cover.</summary>
    public TimeSpan Span { get; init; } = TimeSpan.FromDays(7);

    /// <summary>One set of files per machine, each with an equal share of <see cref="TargetBytes"/>.</summary>
    public IReadOnlyList<string> MachineNames { get; init; } = ["BULK-NODE1", "BULK-NODE2"];

    /// <summary>
    /// A file rolls to <c>_001</c>, <c>_002</c>, ... once it has reached this size, as Serilog's
    /// <c>rollOnFileSizeLimit</c> does (so a file can exceed it by one line).
    /// </summary>
    public long RollSizeBytes { get; init; } = 100L * 1024 * 1024;

    /// <summary>
    /// The writer's time zone, which picks each file's date: Serilog rolls on local days while
    /// <c>@t</c> is UTC.
    /// </summary>
    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Local;

    /// <summary>Seed for the event mix; the same settings and seed give the same bytes.</summary>
    public int Seed { get; init; } = 36;

    /// <summary>Line ending; Serilog writes <see cref="Environment.NewLine"/>.</summary>
    public string NewLine { get; init; } = Environment.NewLine;
}

/// <summary>What <see cref="BulkLogWriter.Write"/> wrote.</summary>
/// <param name="Files">Full paths, per machine in the order written.</param>
/// <param name="Events">Events written across every file.</param>
/// <param name="Bytes">Bytes written across every file.</param>
internal sealed record BulkLogResult(IReadOnlyList<string> Files, long Events, long Bytes);

/// <summary>
/// Writes days of Umbraco-style logs quickly, for the files provider's performance check (BRIEF
/// Appendix C bulk mode, #36): about 2 GB over 7 simulated days, under two machine names.
/// </summary>
/// <remarks>
/// <para>
/// Lines are written directly as Serilog compact JSON rather than through <c>ILogger</c>, which
/// would take hours for 2 GB and could not back-date timestamps. They copy what Umbraco 17 writes:
/// <c>@t</c> in UTC round-trip format, <c>@l</c> omitted for Information, <c>@x</c> as
/// <c>Exception.ToString()</c> text, <c>@tr</c>/<c>@sp</c> inside requests, and Umbraco's enricher
/// properties (<c>ProcessId</c>, <c>MachineName</c>, <c>Log4NetLevel</c>, ...) after the event's
/// own. Files are named <c>UmbracoTraceLog.{machine}.{yyyyMMdd}[_NNN].json</c>, rolling daily on
/// the writer's local day and on <see cref="BulkLogSettings.RollSizeBytes"/>.
/// </para>
/// <para>
/// The mix, per scenario: about 87% requests (2% of them slow, with a warning), 8% the noisy
/// background job, 3% content publishes (a third failing), and 1% each surface-controller and SQL
/// timeout exceptions; 404 storms of 25 requests are rare. One <c>Fatal</c> boot failure, an hour
/// into the first machine's timeline, is the rarest event: a newest-first query for it scans
/// almost the whole set.
/// </para>
/// <para>No Umbraco dependency, so tests and the benchmark compile it without a host.</para>
/// </remarks>
internal static class BulkLogWriter
{
    private const string FilePrefix = "UmbracoTraceLog.";
    private const string ApplicationId = "2a55be0ae14c7bb9512beb3c57290e7faef56e59";

    private static readonly string[] Paths =
    [
        "/",
        "/about-us",
        "/blog",
        "/blog/hello-world",
        "/contact",
        "/api/products",
        "/api/basket",
    ];

    private static readonly string[] Tags = ["sale", "new", "featured", "clearance"];

    private static readonly string NullReferenceTrace =
        "System.NullReferenceException: Object reference not set to an instance of an object.\r\n"
        + "   at LogExplorer.Site.Controllers.ContactSurfaceController.Submit(ContactForm model) in C:\\src\\Site\\Controllers\\ContactSurfaceController.cs:line 42\r\n"
        + "   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.SyncActionResultExecutor.Execute(ActionContext actionContext, IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)\r\n"
        + "   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.InvokeActionMethodAsync()";

    private static readonly string SqlTimeoutTrace =
        "Microsoft.Data.SqlClient.SqlException (0x80131904): Execution Timeout Expired. The timeout period elapsed prior to completion of the operation or the server is not responding.\r\n"
        + " ---> System.ComponentModel.Win32Exception (258): The wait operation timed out.\r\n"
        + "   --- End of inner exception stack trace ---\r\n"
        + "   at Microsoft.Data.SqlClient.SqlConnection.OnError(SqlException exception, Boolean breakConnection, Action`1 wrapCloseInAction)\r\n"
        + "   at Microsoft.Data.SqlClient.SqlCommand.ExecuteReader()\r\n"
        + "   at NPoco.Database.ExecuteReaderHelper(DbCommand cmd)\r\n"
        + "   at Umbraco.Cms.Infrastructure.Persistence.UmbracoDatabase.ExecuteReader(DbCommand cmd)";

    private static readonly string PublishFailureTrace =
        "System.InvalidOperationException: Cannot publish a document whose parent is not published.\r\n"
        + "   at Umbraco.Cms.Core.Services.ContentPublishingService.PublishAsync(Guid key, ICollection`1 culturesToPublishOrSchedule, Guid userKey)";

    private static readonly string BootFailureTrace =
        "System.InvalidOperationException: The runtime could not start: the database is not configured.\r\n"
        + "   at Umbraco.Cms.Infrastructure.Runtime.CoreRuntime.StartAsync(CancellationToken cancellationToken)";

    private static readonly JsonWriterOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Writes the set. Existing files with the same names are overwritten; others are left alone.
    /// </summary>
    /// <param name="settings">What to write.</param>
    /// <returns>The files written and their totals.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="BulkLogSettings.TargetBytes"/>, <see cref="BulkLogSettings.RollSizeBytes"/> or
    /// <see cref="BulkLogSettings.Span"/> is not positive, or there are no machine names.
    /// </exception>
    public static BulkLogResult Write(BulkLogSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.TargetBytes, 1, nameof(settings));
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.RollSizeBytes, 1, nameof(settings));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            settings.Span,
            TimeSpan.Zero,
            nameof(settings)
        );
        ArgumentOutOfRangeException.ThrowIfZero(settings.MachineNames.Count, nameof(settings));
        System.IO.Directory.CreateDirectory(settings.Directory);

        var files = new List<string>();
        long events = 0;
        long bytes = 0;
        long share = settings.TargetBytes / settings.MachineNames.Count;
        for (int machine = 0; machine < settings.MachineNames.Count; machine++)
        {
            using var writer = new MachineFiles(settings, settings.MachineNames[machine]);
            var timeline = new Timeline(
                writer,
                settings,
                settings.MachineNames[machine],
                new Random(settings.Seed + machine)
            );
            timeline.Run(share, bootFailure: machine == 0);
            writer.Close();
            files.AddRange(writer.Files);
            events += writer.Events;
            bytes += writer.Bytes;
        }

        return new BulkLogResult(files, events, bytes);
    }

    /// <summary>One machine's simulated activity, written in timestamp order.</summary>
    private sealed class Timeline(
        MachineFiles files,
        BulkLogSettings settings,
        string machineName,
        Random random
    )
    {
        // Bytes per scenario on average (about two 600-byte lines), measured from this writer's
        // own output; it spaces the scenarios so the target size and the span come out together.
        private const double AverageScenarioBytes = 1210;

        private readonly ArrayBufferWriter<byte> _buffer = new(4096);
        private readonly int _processId = 4000 + random.Next(1000);
        private DateTimeOffset _now;
        private long _requestNumber;
        private int _publishCount;

        public void Run(long targetBytes, bool bootFailure)
        {
            DateTimeOffset start = settings.End - settings.Span;
            double scenarios = Math.Max(1, targetBytes / AverageScenarioBytes);
            // Uniform gaps in [0, 2 * mean) keep the volume even over the span with some jitter.
            double meanGapTicks = settings.Span.Ticks / scenarios;
            _now = start;

            // An hour in, so a "last 7 days" window ending a little after End still includes it.
            DateTimeOffset? bootFailureAt = bootFailure ? start.AddHours(1) : null;
            while (true)
            {
                long gap = (long)(random.NextDouble() * 2 * meanGapTicks);
                DateTimeOffset next = _now.AddTicks(Math.Max(gap, 1));
                // Time, not size, ends the run, so the set always reaches End.
                if (next > settings.End)
                {
                    break;
                }

                _now = next;
                if (_now >= bootFailureAt)
                {
                    BootFailure();
                    bootFailureAt = null;
                }

                double roll = random.NextDouble();
                if (roll < 0.87)
                {
                    Request();
                }
                else if (roll < 0.95)
                {
                    NoisyJob();
                }
                else if (roll < 0.98)
                {
                    ContentPublish();
                }
                else if (roll < 0.99)
                {
                    SurfaceControllerNullReference();
                }
                else if (roll < 0.9995)
                {
                    SqlTimeout();
                }
                else
                {
                    NotFoundStorm(25);
                }
            }
        }

        private void Request()
        {
            string path = Paths[random.Next(Paths.Length)];
            bool slow = random.NextDouble() < 0.02;
            int duration = slow ? random.Next(1_200, 8_000) : random.Next(3, 180);
            var request = NewRequest(path);

            Line(
                request,
                "Request starting {Protocol} {RequestMethod} {RequestPath}",
                "Information",
                "Microsoft.AspNetCore.Hosting.Diagnostics",
                null,
                w =>
                {
                    w.WriteString("Protocol", "HTTP/2");
                    w.WriteString("RequestMethod", "GET");
                    w.WriteString("RequestPath", path);
                }
            );
            if (path == "/api/basket")
            {
                Line(
                    request,
                    "Basket {RequestId} priced {@Cart} with tags {@Tags}",
                    "Information",
                    "LogExplorer.Site.Basket.BasketService",
                    null,
                    w =>
                    {
                        w.WriteString("RequestId", request.RequestId);
                        w.WriteStartObject("Cart");
                        w.WriteNumber("Total", Math.Round(random.NextDouble() * 250, 2));
                        w.WriteNumber("Items", random.Next(1, 6));
                        w.WriteString("Currency", "GBP");
                        w.WriteEndObject();
                        w.WriteStartArray("Tags");
                        w.WriteStringValue(Tags[random.Next(Tags.Length)]);
                        w.WriteStringValue(Tags[random.Next(Tags.Length)]);
                        w.WriteEndArray();
                    }
                );
            }

            if (slow)
            {
                Line(
                    request,
                    "Slow request {RequestMethod} {RequestPath} took {Duration} ms",
                    "Warning",
                    "Microsoft.AspNetCore.Hosting.Diagnostics",
                    null,
                    w =>
                    {
                        w.WriteString("RequestMethod", "GET");
                        w.WriteString("RequestPath", path);
                        w.WriteNumber("Duration", duration);
                    }
                );
            }

            Completion(request, "GET", path, 200, duration, "Information");
        }

        private void SurfaceControllerNullReference()
        {
            const string path = "/umbraco/surface/contact/submit";
            var request = NewRequest(path);
            Line(
                request,
                "Contact form submission failed for {FormName}",
                "Error",
                "LogExplorer.Site.Controllers.ContactSurfaceController",
                NullReferenceTrace,
                w => w.WriteString("FormName", "contact")
            );
            Completion(request, "POST", path, 500, random.Next(20, 90), "Information");
        }

        private void SqlTimeout()
        {
            var request = NewRequest("/blog");
            Line(
                request,
                "An unhandled exception has occurred while executing the request.",
                "Error",
                "Umbraco.Cms.Infrastructure.Persistence.UmbracoDatabase",
                SqlTimeoutTrace,
                _ => { }
            );
            Line(
                request,
                "Slow request {RequestMethod} {RequestPath} took {Duration} ms",
                "Warning",
                "Microsoft.AspNetCore.Hosting.Diagnostics",
                null,
                w =>
                {
                    w.WriteString("RequestMethod", "GET");
                    w.WriteString("RequestPath", "/blog");
                    w.WriteNumber("Duration", 30_012);
                }
            );
        }

        private void NotFoundStorm(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Completion(
                    NewRequest("/wp-login.php"),
                    "GET",
                    "/wp-login.php",
                    404,
                    random.Next(2, 9),
                    "Warning"
                );
            }
        }

        private void ContentPublish()
        {
            int contentId = random.Next(1050, 1200);
            string documentKey = NewGuid().ToString();
            bool fails = ++_publishCount % 3 == 0;
            Line(
                null,
                fails
                    ? "Failed to publish document {ContentId} ({DocumentKey})"
                    : "Document {ContentId} ({DocumentKey}) published by {UserId}",
                fails ? "Error" : "Information",
                "Umbraco.Cms.Core.Services.ContentPublishingService",
                fails ? PublishFailureTrace : null,
                w =>
                {
                    w.WriteNumber("ContentId", contentId);
                    w.WriteString("DocumentKey", documentKey);
                    if (!fails)
                    {
                        w.WriteNumber("UserId", -1);
                    }
                }
            );
        }

        private void NoisyJob() =>
            Line(
                null,
                "Running recurring background job {JobName}",
                "Information",
                "Umbraco.Cms.Infrastructure.BackgroundJobs.RecurringBackgroundJobHostedService",
                null,
                w => w.WriteString("JobName", "TempFileCleanupJob")
            );

        private void BootFailure() =>
            Line(
                null,
                "Boot failed",
                "Fatal",
                "Umbraco.Cms.Infrastructure.Runtime.CoreRuntime",
                BootFailureTrace,
                _ => { }
            );

        private void Completion(
            RequestContext request,
            string method,
            string path,
            int statusCode,
            int duration,
            string level
        ) =>
            Line(
                request,
                "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Duration} ms",
                level,
                "Microsoft.AspNetCore.Hosting.Diagnostics",
                null,
                w =>
                {
                    w.WriteString("RequestMethod", method);
                    w.WriteString("RequestPath", path);
                    w.WriteNumber("StatusCode", statusCode);
                    w.WriteNumber("Duration", duration);
                }
            );

        private RequestContext NewRequest(string path)
        {
            _requestNumber++;
            return new RequestContext(
                Hex(16),
                Hex(8),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"0HN{random.Next(100_000, 999_999)}:{random.Next(1, 99):D8}"
                ),
                path,
                NewGuid().ToString(),
                _requestNumber
            );
        }

        // Writes one event a millisecond or so after the previous one, so a request's lines stay
        // together and every file stays in time order.
        private void Line(
            RequestContext? request,
            string template,
            string level,
            string sourceContext,
            string? exception,
            Action<Utf8JsonWriter> properties
        )
        {
            _now = _now.AddTicks(random.Next(1, 20_000));
            _buffer.ResetWrittenCount();
            using (var w = new Utf8JsonWriter(_buffer, JsonOptions))
            {
                w.WriteStartObject();
                w.WriteString("@t", _now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
                w.WriteString("@mt", template);
                if (level != "Information")
                {
                    w.WriteString("@l", level);
                }

                if (exception is not null)
                {
                    w.WriteString("@x", exception);
                }

                if (request is not null)
                {
                    w.WriteString("@tr", request.TraceId);
                    w.WriteString("@sp", request.SpanId);
                }

                properties(w);
                w.WriteString("SourceContext", sourceContext);
                // Scope properties, which Serilog adds only when the event has none of that name;
                // the template's holes are exactly the event's own properties.
                if (request is not null)
                {
                    if (!template.Contains("{RequestId}", StringComparison.Ordinal))
                    {
                        w.WriteString("RequestId", request.RequestId);
                    }

                    if (!template.Contains("{RequestPath}", StringComparison.Ordinal))
                    {
                        w.WriteString("RequestPath", request.Path);
                    }

                    w.WriteString("HttpRequestId", request.HttpRequestId);
                    w.WriteNumber("HttpRequestNumber", request.Number);
                }

                w.WriteNumber("ProcessId", _processId);
                w.WriteString("ProcessName", "LogExplorer.Site17");
                w.WriteNumber("ThreadId", random.Next(1, 64));
                w.WriteString("ApplicationId", ApplicationId);
                w.WriteString("MachineName", machineName);
                w.WriteString("Log4NetLevel", Log4NetLevel(level));
                w.WriteEndObject();
            }

            files.Write(_now, _buffer.WrittenSpan);
        }

        private string Hex(int bytes)
        {
            Span<byte> value = stackalloc byte[bytes];
            random.NextBytes(value);
            return Convert.ToHexStringLower(value);
        }

        private Guid NewGuid()
        {
            Span<byte> value = stackalloc byte[16];
            random.NextBytes(value);
            return new Guid(value);
        }

        // Umbraco's Log4NetLevel enricher pads every level name to five characters.
        private static string Log4NetLevel(string level) =>
            level switch
            {
                "Warning" => "WARN ",
                "Error" => "ERROR",
                "Fatal" => "FATAL",
                "Debug" => "DEBUG",
                _ => "INFO ",
            };
    }

    /// <summary>Correlation values shared by every line of one simulated request.</summary>
    private sealed record RequestContext(
        string TraceId,
        string SpanId,
        string RequestId,
        string Path,
        string HttpRequestId,
        long Number
    );

    /// <summary>One machine's files, rolled by local day and size as Serilog's rolling sink does.</summary>
    private sealed class MachineFiles(BulkLogSettings settings, string machineName) : IDisposable
    {
        private readonly byte[] _newLine = Encoding.UTF8.GetBytes(settings.NewLine);
        private readonly List<string> _files = [];
        private FileStream? _stream;
        private DateOnly _day;
        private int _rollIndex;
        private long _length;

        public IReadOnlyList<string> Files => _files;

        public long Events { get; private set; }

        public long Bytes { get; private set; }

        public void Write(DateTimeOffset timestamp, ReadOnlySpan<byte> line)
        {
            var day = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(timestamp, settings.TimeZone).DateTime
            );
            if (_stream is null || day != _day)
            {
                Open(day, 0);
            }
            else if (_length >= settings.RollSizeBytes)
            {
                Open(day, _rollIndex + 1);
            }

            _stream!.Write(line);
            _stream.Write(_newLine);
            int written = line.Length + _newLine.Length;
            _length += written;
            Bytes += written;
            Events++;
        }

        public void Close()
        {
            _stream?.Dispose();
            _stream = null;
        }

        public void Dispose() => Close();

        private void Open(DateOnly day, int rollIndex)
        {
            Close();
            string roll =
                rollIndex == 0 ? "" : "_" + rollIndex.ToString("000", CultureInfo.InvariantCulture);
            string path = Path.Combine(
                settings.Directory,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{FilePrefix}{machineName}.{day:yyyyMMdd}{roll}.json"
                )
            );
            _stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 1024 * 1024
            );
            _files.Add(path);
            _day = day;
            _rollIndex = rollIndex;
            _length = 0;
        }
    }
}
