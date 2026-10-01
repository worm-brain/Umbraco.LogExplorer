namespace Umbraco.Community.LogExplorer.Features.Sources.Files;

/// <summary>
/// One Umbraco log file, with the parts Serilog's rolling file sink encodes in its name, for
/// example <c>UmbracoTraceLog.WORM.20261001_001.json</c>.
/// </summary>
/// <param name="Path">Full path to the file.</param>
/// <param name="FileName">The file name alone; record ids are built from it.</param>
/// <param name="MachineName">
/// The machine that wrote the file, or null when the configured file name format has no machine
/// placeholder.
/// </param>
/// <param name="Date">The day the file covers, in the writer's local time (Serilog rolls on local days).</param>
/// <param name="RollIndex">0 for the first file of a day; 1 for <c>_001</c> and so on, written once the previous one hit its size limit.</param>
internal sealed record LogFile(
    string Path,
    string FileName,
    string? MachineName,
    DateOnly Date,
    int RollIndex
);
