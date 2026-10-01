using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace LogExplorer.Samples.LogGenerator;

/// <summary>
/// Writes realistic Umbraco-style log events through <see cref="ILogger"/>, so they land in the
/// site's real Umbraco log files with the same shape as production events (BRIEF Appendix C).
/// Each method is one scenario; <see cref="LogGeneratorService"/> decides when each runs.
/// </summary>
/// <remarks>
/// Message templates use named holes so the explorer sees structured, typed properties. Request
/// scenarios run inside an <see cref="Activity"/> and a logging scope, so every event of a request
/// shares a trace id and a <c>RequestId</c> (needed for "Same request").
/// </remarks>
internal sealed class SampleEvents
{
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

    private readonly ILogger _requests;
    private readonly ILogger _surfaceController;
    private readonly ILogger _database;
    private readonly ILogger _content;
    private readonly ILogger _backgroundJobs;
    private readonly ILogger _basket;
    private readonly Random _random;
    private int _publishCount;

    /// <summary>Initialises the scenario writer with one logger per simulated component.</summary>
    /// <param name="loggerFactory">Creates loggers whose categories become <c>SourceContext</c>.</param>
    /// <param name="random">Randomness source; injectable so a fixed seed gives repeatable output.</param>
    public SampleEvents(ILoggerFactory loggerFactory, Random random)
    {
        _requests = loggerFactory.CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics");
        _surfaceController = loggerFactory.CreateLogger(
            "LogExplorer.Site.Controllers.ContactSurfaceController"
        );
        _database = loggerFactory.CreateLogger(
            "Umbraco.Cms.Infrastructure.Persistence.UmbracoDatabase"
        );
        _content = loggerFactory.CreateLogger("Umbraco.Cms.Core.Services.ContentPublishingService");
        _backgroundJobs = loggerFactory.CreateLogger(
            "Umbraco.Cms.Infrastructure.BackgroundJobs.RecurringBackgroundJobHostedService"
        );
        _basket = loggerFactory.CreateLogger("LogExplorer.Site.Basket.BasketService");
        _random = random;
    }

    /// <summary>
    /// Logs one simulated HTTP request: a start line and a completion line with <c>StatusCode</c> and
    /// <c>Duration</c>. About 2% are slow (over 1000 ms) and log an extra warning.
    /// </summary>
    public void Request()
    {
        string path = Paths[_random.Next(Paths.Length)];
        bool slow = _random.NextDouble() < 0.02;
        int duration = slow ? _random.Next(1_200, 8_000) : _random.Next(3, 180);

        InRequest(
            path,
            requestId =>
            {
                _requests.LogInformation(
                    "Request starting {Protocol} {RequestMethod} {RequestPath}",
                    "HTTP/2",
                    "GET",
                    path
                );
                if (path.StartsWith("/api/basket", StringComparison.Ordinal))
                {
                    // Nested object and array properties ({@...} keeps their structure).
                    var cart = new
                    {
                        Total = Math.Round(_random.NextDouble() * 250, 2),
                        Items = _random.Next(1, 6),
                        Currency = "GBP",
                    };
                    string[] tags = Tags.OrderBy(_ => _random.Next())
                        .Take(_random.Next(1, 3))
                        .ToArray();
                    _basket.LogInformation(
                        "Basket {RequestId} priced {@Cart} with tags {@Tags}",
                        requestId,
                        cart,
                        tags
                    );
                }

                if (slow)
                {
                    _requests.LogWarning(
                        "Slow request {RequestMethod} {RequestPath} took {Duration} ms",
                        "GET",
                        path,
                        duration
                    );
                }

                _requests.LogInformation(
                    "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Duration} ms",
                    "GET",
                    path,
                    200,
                    duration
                );
            }
        );
    }

    /// <summary>Logs a <see cref="NullReferenceException"/> thrown in a surface controller during a form post.</summary>
    public void SurfaceControllerNullReference()
    {
        InRequest(
            "/umbraco/surface/contact/submit",
            _ =>
            {
                try
                {
                    ThrowNullReference();
                }
                catch (NullReferenceException ex)
                {
                    _surfaceController.LogError(
                        ex,
                        "Contact form submission failed for {FormName}",
                        "contact"
                    );
                }

                _requests.LogInformation(
                    "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Duration} ms",
                    "POST",
                    "/umbraco/surface/contact/submit",
                    500,
                    _random.Next(20, 90)
                );
            }
        );
    }

    /// <summary>Logs a SQL command timeout followed by a 30-second slow-request warning, in one request.</summary>
    public void SqlTimeout()
    {
        InRequest(
            "/blog",
            _ =>
            {
                try
                {
                    throw new TimeoutException(
                        "Execution Timeout Expired. The timeout period elapsed prior to completion of the operation or the server is not responding."
                    );
                }
                catch (TimeoutException ex)
                {
                    _database.LogError(
                        ex,
                        "An unhandled exception has occurred while executing the request."
                    );
                }

                _requests.LogWarning(
                    "Slow request {RequestMethod} {RequestPath} took {Duration} ms",
                    "GET",
                    "/blog",
                    30_012
                );
            }
        );
    }

    /// <summary>Logs a burst of 404s for one path, each as its own request (the "404 storm").</summary>
    /// <param name="count">How many 404 requests to log.</param>
    public void NotFoundStorm(int count)
    {
        for (int i = 0; i < count; i++)
        {
            InRequest(
                "/wp-login.php",
                _ =>
                    _requests.LogWarning(
                        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Duration} ms",
                        "GET",
                        "/wp-login.php",
                        404,
                        _random.Next(2, 9)
                    )
            );
        }
    }

    /// <summary>
    /// Logs a document publish with <c>ContentId</c> and <c>DocumentKey</c> (for CMS deep links);
    /// every third one fails with an <see cref="InvalidOperationException"/>, so a short run always
    /// includes a failure.
    /// </summary>
    public void ContentPublish()
    {
        int contentId = _random.Next(1050, 1200);
        Guid documentKey = Guid.NewGuid();
        if (++_publishCount % 3 == 0)
        {
            try
            {
                throw new InvalidOperationException(
                    "Cannot publish a document whose parent is not published."
                );
            }
            catch (InvalidOperationException ex)
            {
                _content.LogError(
                    ex,
                    "Failed to publish document {ContentId} ({DocumentKey})",
                    contentId,
                    documentKey
                );
            }

            return;
        }

        _content.LogInformation(
            "Document {ContentId} ({DocumentKey}) published by {UserId}",
            contentId,
            documentKey,
            -1
        );
    }

    /// <summary>Logs the noisy recurring background job line (for mute and exclude testing).</summary>
    public void NoisyJob() =>
        _backgroundJobs.LogInformation(
            "Running recurring background job {JobName}",
            "TempFileCleanupJob"
        );

    // Runs a scenario inside a fresh Activity (so Serilog records @tr/@sp) and a scope carrying the
    // request's correlation properties, then disposes both.
    private void InRequest(string path, Action<string> body)
    {
        using var activity = new Activity("LogExplorer.SampleRequest").Start();
        string requestId = $"0HN{_random.Next(100_000, 999_999)}:{_random.Next(1, 99):D8}";
        using IDisposable? scope = _requests.BeginScope(
            new Dictionary<string, object> { ["RequestId"] = requestId, ["RequestPath"] = path }
        );
        body(requestId);
    }

    // A real throw so the event carries a genuine stack trace.
    private static void ThrowNullReference()
    {
        string? name = null;
        _ = name!.Length;
    }
}
