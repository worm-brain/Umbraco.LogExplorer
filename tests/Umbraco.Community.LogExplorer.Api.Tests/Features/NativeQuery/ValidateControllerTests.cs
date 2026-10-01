using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Community.LogExplorer.Core.Fake;
using Umbraco.Community.LogExplorer.Core.Results;
using Umbraco.Community.LogExplorer.Core.Sources;
using Umbraco.Community.LogExplorer.Features.NativeQuery;
using Umbraco.Community.LogExplorer.Features.Sources;
using Umbraco.Community.LogExplorer.Infrastructure.Api;

namespace Umbraco.Community.LogExplorer.Api.Tests.Features.NativeQuery;

/// <summary>
/// <c>POST /sources/{alias}/validate</c> reports whether native-mode input can run, and where it
/// goes wrong (#43).
/// </summary>
public class ValidateControllerTests
{
    private static readonly UserContext Editor = new(
        Guid.NewGuid(),
        new HashSet<string>(["editor"], StringComparer.OrdinalIgnoreCase)
    );

    [Fact]
    public void Validate_ValidQuery_ReturnsValid()
    {
        // Arrange
        ValidateController controller = CreateController(
            new FakeLogSource(new() { Alias = "sample" })
        );

        // Act
        ValidationResult result = controller.Validate("sample", new ValidateRequest("text(\"a\")"));

        // Assert
        Assert.Equal(new ValidationResult(true, null, null), result);
    }

    [Fact]
    public void Validate_UnclosedBracket_ReturnsInvalidWithItsPosition()
    {
        // Arrange
        ValidateController controller = CreateController(
            new FakeLogSource(new() { Alias = "sample" })
        );

        // Act
        ValidationResult result = controller.Validate("sample", new ValidateRequest("a and (b"));

        // Assert
        Assert.Equal((false, 6), (result.Valid, result.Position));
    }

    [Fact]
    public void Validate_MissingNative_ThrowsArgumentException()
    {
        // Arrange
        ValidateController controller = CreateController(
            new FakeLogSource(new() { Alias = "sample" })
        );

        // Act
        Exception? thrown = Record.Exception(() =>
            controller.Validate("sample", new ValidateRequest(null))
        );

        // Assert
        Assert.IsType<ArgumentException>(thrown);
    }

    [Fact]
    public void Validate_SourceThatDisallowsNativeQueries_ThrowsNotSupported()
    {
        // Arrange
        ValidateController controller = CreateController(
            new NativeQueryDisabledSource(new FakeLogSource(new() { Alias = "sample" }))
        );

        // Act
        Exception? thrown = Record.Exception(() =>
            controller.Validate("sample", new ValidateRequest("a"))
        );

        // Assert
        Assert.IsType<NotSupportedException>(thrown);
    }

    [Fact]
    public void Validate_UnknownAlias_ThrowsKeyNotFound()
    {
        // Arrange
        ILogSourceRegistry registry = Substitute.For<ILogSourceRegistry>();
        registry.GetForUser("missing", Editor).Throws(new KeyNotFoundException("missing"));
        ValidateController controller = CreateController(registry);

        // Act
        Exception? thrown = Record.Exception(() =>
            controller.Validate("missing", new ValidateRequest("a"))
        );

        // Assert
        Assert.IsType<KeyNotFoundException>(thrown);
    }

    private static ValidateController CreateController(ILogSource source)
    {
        ILogSourceRegistry registry = Substitute.For<ILogSourceRegistry>();
        registry.GetForUser(source.Alias, Editor).Returns(source);
        return CreateController(registry);
    }

    private static ValidateController CreateController(ILogSourceRegistry registry)
    {
        IUserContextAccessor users = Substitute.For<IUserContextAccessor>();
        users.GetCurrent().Returns(Editor);
        return new ValidateController(registry, users);
    }
}
