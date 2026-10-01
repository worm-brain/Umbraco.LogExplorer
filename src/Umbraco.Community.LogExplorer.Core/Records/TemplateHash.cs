using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Umbraco.Community.LogExplorer.Core.Records;

/// <summary>
/// Computes <see cref="LogRecord.TemplateHash"/> for sources that store template text (BRIEF §9.3).
/// </summary>
public static class TemplateHash
{
    /// <summary>
    /// MD5 of the UTF-8 template text as 32 lower-case hex characters, the same convention as
    /// Serilog.Sinks.OpenTelemetry's <c>message_template.hash.md5</c>, so hashes match across tools.
    /// </summary>
    /// <param name="template">The message template text.</param>
    /// <returns>The hash.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="template"/> is null.</exception>
    [SuppressMessage(
        "Security",
        "CA5351:Do Not Use Broken Cryptographic Algorithms",
        Justification = "A grouping key, not a security boundary; MD5 is the cross-tool convention."
    )]
    public static string Compute(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(template)));
    }
}
