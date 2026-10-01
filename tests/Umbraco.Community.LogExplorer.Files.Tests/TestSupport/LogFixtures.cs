namespace Umbraco.Community.LogExplorer.Files.Tests.TestSupport;

/// <summary>
/// The committed log directory (BRIEF §16). The files are byte-exact (see .gitattributes), so the
/// line offsets tests assert on are stable:
/// <list type="bullet">
/// <item><c>UmbracoTraceLog.WORM.20261001.json</c>: CRLF endings; events at 0, 147 and 523 with a
/// blank line at 145; multi-byte UTF-8, nested and array properties, <c>@tr</c>/<c>@sp</c>, an
/// <c>@x</c> warning.</item>
/// <item><c>UmbracoTraceLog.NODE2.20260930_001.json</c>: LF endings; events at 0, 62 and 190, a
/// malformed line at 125 and an unterminated, truncated last line at 252.</item>
/// <item>Other days and machine <c>NODE2</c>, plus empty decoys whose names do not match the format.</item>
/// </list>
/// </summary>
internal static class LogFixtures
{
    public static string Directory { get; } =
        Path.Combine(AppContext.BaseDirectory, "Features", "Sources", "Files", "Fixtures", "Logs");

    public const string CrlfFile = "UmbracoTraceLog.WORM.20261001.json";

    public const string RolledFile = "UmbracoTraceLog.NODE2.20260930_001.json";

    public static string PathOf(string fileName) => Path.Combine(Directory, fileName);
}

/// <summary>A temporary directory for tests that need files the fixtures do not have; deleted on dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "log-explorer-tests-" + Guid.NewGuid().ToString("N")
        );

    public TempDirectory() => System.IO.Directory.CreateDirectory(Path);

    public string Write(string fileName, byte[] content)
    {
        string path = System.IO.Path.Combine(Path, fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    public void Dispose() => System.IO.Directory.Delete(Path, recursive: true);
}
