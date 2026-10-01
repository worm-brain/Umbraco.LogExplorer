using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security;
using Umbraco.Community.LogExplorer.Core.Sources;

namespace Umbraco.Community.LogExplorer.Infrastructure.Api;

/// <summary>
/// Reduces the current backoffice user to the Core <see cref="UserContext"/> (user key and group
/// aliases), so source visibility can be decided without Core referencing Umbraco types.
/// </summary>
public interface IUserContextAccessor
{
    /// <summary>Gets the current user as a <see cref="UserContext"/>.</summary>
    /// <returns>The user context.</returns>
    /// <exception cref="InvalidOperationException">No backoffice user is signed in.</exception>
    UserContext GetCurrent();
}

/// <inheritdoc />
internal sealed class BackOfficeUserContextAccessor(IBackOfficeSecurityAccessor securityAccessor)
    : IUserContextAccessor
{
    /// <inheritdoc />
    public UserContext GetCurrent()
    {
        // The controllers require Settings access, so a user is always present here; the throw
        // guards against the accessor being used outside an authorised request.
        IUser user =
            securityAccessor.BackOfficeSecurity?.CurrentUser
            ?? throw new InvalidOperationException("No backoffice user is signed in.");

        return new UserContext(
            user.Key,
            new HashSet<string>(
                user.Groups.Select(group => group.Alias),
                StringComparer.OrdinalIgnoreCase
            )
        );
    }
}
