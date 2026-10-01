using System.Reflection;

namespace Umbraco.Community.LogExplorer.Core.Tests.Architecture;

/// <summary>
/// Guards the rule that Core stays free of Umbraco references (BRIEF §7.1), so it can be
/// reused by provider authors and tools such as a CLI without pulling in the CMS.
/// </summary>
public class CoreDependencyTests
{
    [Fact]
    public void CoreAssembly_ReferencedAssemblies_ContainNoUmbracoAssembly()
    {
        // Arrange
        Assembly core = typeof(CoreAssemblyMarker).Assembly;

        // Act
        string[] umbracoReferences = core.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("Umbraco.", StringComparison.Ordinal))
            .ToArray();

        // Assert
        Assert.Empty(umbracoReferences);
    }
}
