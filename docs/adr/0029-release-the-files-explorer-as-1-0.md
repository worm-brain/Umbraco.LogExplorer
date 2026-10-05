# ADR 0029: Release the log files explorer as 1.0

- Status: Accepted
- Date: 2026-10-05
- Issue: #52

## Context

The roadmap released Phase 1 as alpha prereleases and kept the stable 1.0 for the end of Phase 2:
Application Insights and Seq sources, sensitive sources and masking, saved views, export and CMS
deep links. The README, the NuGet readme and the docs promised those features "in 1.0".

Phase 1 is complete. The explorer reads Umbraco's own log files, covers every job in the build
brief that does not need another source, passes the package smoke test on clean sites of both
majors, and is a drop-in replacement for the core Log Viewer. Holding a stable release back for
features that are a separate piece of work keeps sites on a prerelease for no gain in what they
get today.

## Decision

- The current feature set ships as the first stable release of each package line: `17.0.0` for
  Umbraco 17 and `18.0.0` for Umbraco 18 (ADR 0010). There are no alpha or beta prereleases first.
- The Phase 2 features ship later as minor versions of both lines (`17.1.0` and `18.1.0` onwards),
  each when it is ready. They are additive: a new source type, new endpoints, new configuration.
- The public docs call them **Coming next**, not "Coming in 1.0". The configuration keys bound for
  them stay reserved and have no effect until their feature ships.
- Until masking ships, the security model's warning stands: anything the site logs is visible to
  everyone with Settings access, as it is in the core Log Viewer.

## Consequences

- The release workflow tags `v17.0.0` and `v18.0.0`. `dotnet add package` without `--version`
  picks the newest version overall, which is the `18.x` line, so the install instructions name the
  version for Umbraco 17 sites.
- The build brief's Phase 1 and Phase 2 release lines, and #52, change from prereleases to these
  versions.
- Stable versions carry a compatibility promise. The package major follows the Umbraco major
  (ADR 0010), so a breaking change to configuration or the Management API cannot take a new
  package major of its own. Phase 2 work must therefore keep today's contracts and only add to
  them; anything that cannot waits for the next Umbraco major.
