using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Logging;

namespace LogExplorer.Samples.LogGenerator;

/// <summary>
/// Background service that keeps a sample site's logs busy with realistic events while it runs
/// (BRIEF Appendix C). Off unless <c>LogGenerator:Enabled</c> is true; the file scenarios and the
/// bulk set (<see cref="BulkLogWriter"/>) are separate one-off switches.
/// </summary>
/// <remarks>
/// Cadence, per second: requests at <see cref="LogGeneratorOptions.RequestsPerSecond"/>; every
/// 5 s the noisy job; every 30 s a content publish; every 45 s a surface-controller exception;
/// every 60 s a SQL timeout; every 120 s a 404 storm.
/// </remarks>
internal sealed class LogGeneratorService(
    IOptions<LogGeneratorOptions> options,
    ILoggerFactory loggerFactory,
    ILoggingConfiguration loggingConfiguration,
    ILogger<LogGeneratorService> logger
) : BackgroundService
{
    /// <summary>Runs the scenario loop until the host stops.</summary>
    /// <param name="stoppingToken">Signalled when the site shuts down.</param>
    /// <returns>A task that completes when the loop ends.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogGeneratorOptions settings = options.Value;
        if (settings.WriteFileScenarios)
        {
            FileScenarios.Write(
                loggingConfiguration.LogDirectory,
                settings.SecondMachineName,
                new Random(42)
            );
            logger.LogInformation(
                "Log generator wrote the file scenarios to {LogDirectory}",
                loggingConfiguration.LogDirectory
            );
        }

        if (settings.BulkGigabytes > 0)
        {
            // Off the start-up path: 2 GB takes a while, and the site is usable meanwhile.
            _ = Task.Run(() => WriteBulk(settings), stoppingToken);
        }

        if (!settings.Enabled)
        {
            return;
        }

        logger.LogInformation(
            "Log generator started at {RequestsPerSecond} requests per second",
            settings.RequestsPerSecond
        );
        var events = new SampleEvents(loggerFactory, new Random());
        var random = new Random();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        long tick = 0;

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            tick++;

            // Poisson-ish request volume: whole requests plus a chance of one more.
            int requests =
                (int)settings.RequestsPerSecond
                + (random.NextDouble() < settings.RequestsPerSecond % 1 ? 1 : 0);
            for (int i = 0; i < requests; i++)
            {
                events.Request();
            }

            if (tick % 5 == 0)
            {
                events.NoisyJob();
            }

            if (tick % 30 == 0)
            {
                events.ContentPublish();
            }

            if (tick % 45 == 0)
            {
                events.SurfaceControllerNullReference();
            }

            if (tick % 60 == 0)
            {
                events.SqlTimeout();
            }

            if (tick % 120 == 0)
            {
                events.NotFoundStorm(25);
            }
        }
    }

    // Writes the bulk set once: a directory that already holds the first bulk machine's files is
    // left alone, so restarts do not rewrite 2 GB.
    private void WriteBulk(LogGeneratorOptions settings)
    {
        string directory = settings.BulkDirectory ?? loggingConfiguration.LogDirectory;
        var bulk = new BulkLogSettings
        {
            Directory = directory,
            TargetBytes = (long)(settings.BulkGigabytes * 1024 * 1024 * 1024),
            End = DateTimeOffset.UtcNow,
            Span = TimeSpan.FromDays(settings.BulkDays),
            RollSizeBytes = settings.BulkRollMegabytes * 1024L * 1024,
        };
        if (
            Directory.Exists(directory)
            && Directory
                .EnumerateFiles(directory, $"UmbracoTraceLog.{bulk.MachineNames[0]}.*.json")
                .Any()
        )
        {
            logger.LogInformation(
                "Log generator bulk files already exist in {LogDirectory}",
                directory
            );
            return;
        }

        try
        {
            BulkLogResult result = BulkLogWriter.Write(bulk);
            logger.LogInformation(
                "Log generator wrote {Bytes} bytes of bulk logs ({Events} events, {Files} files) to {LogDirectory}",
                result.Bytes,
                result.Events,
                result.Files.Count,
                directory
            );
        }
        catch (IOException ex)
        {
            logger.LogError(
                ex,
                "Log generator could not write bulk logs to {LogDirectory}",
                directory
            );
        }
    }
}
