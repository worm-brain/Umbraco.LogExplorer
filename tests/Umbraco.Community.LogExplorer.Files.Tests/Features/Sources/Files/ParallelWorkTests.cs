using Umbraco.Community.LogExplorer.Features.Sources.Files;

namespace Umbraco.Community.LogExplorer.Files.Tests.Features.Sources.Files;

/// <summary>Parallel scan work runs every index and throws what sequential work would (ADR 0026).</summary>
public class ParallelWorkTests
{
    [Fact]
    public void For_ManyIndexes_RunsEachOnce()
    {
        // Arrange
        int[] runs = new int[1000];

        // Act
        ParallelWork.For(runs.Length, index => runs[index]++);

        // Assert
        Assert.All(runs, count => Assert.Equal(1, count));
    }

    [Fact]
    public void For_BodyThrows_RethrowsTheExceptionUnwrapped()
    {
        // Arrange
        static void Body(int index) => throw new InvalidOperationException($"Failed at {index}");

        // Act
        void Run() => ParallelWork.For(10, Body);

        // Assert
        Assert.Throws<InvalidOperationException>(Run);
    }
}
