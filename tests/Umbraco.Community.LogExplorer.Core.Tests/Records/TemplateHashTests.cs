using Umbraco.Community.LogExplorer.Core.Records;

namespace Umbraco.Community.LogExplorer.Core.Tests.Records;

public class TemplateHashTests
{
    [Fact]
    public void Compute_Template_ReturnsLowerCaseMd5Hex()
    {
        // Act
        string hash = TemplateHash.Compute("Hello {Name}");

        // Assert: md5("Hello {Name}"), computed independently with md5sum.
        Assert.Equal("020069ca3179a80742e0b8b3e577fcf5", hash);
    }
}
