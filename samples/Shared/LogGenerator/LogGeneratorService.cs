using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Logging;

namespace LogExplorer.Samples.LogGenerator;

/// <summary>
/// Background service that keeps a sample site's logs busy with realistic events while it runs
/// (BRIEF Appendix C). Off unless <c>LogGenerator:Enabled</c> is true.
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
}
