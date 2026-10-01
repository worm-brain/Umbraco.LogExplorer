# ADR 0001: One source tree serves Umbraco 17 and 18

- Status: Superseded by 0010
- Date: 2026-10-01

## Context

The package supports Umbraco 17 (LTS) and 18. Both run on `net10.0`, so the usual way of serving
two hosts from one project (multi-targeting by target framework) is not available. As of today
Umbraco.Cms 17.7.0 and 18.2.0 are the latest stable releases.

The surfaces the package touches are the Management API (controllers, OpenAPI registration,
authorisation policies), package migrations, `ILoggingConfiguration`, the audit service, and the
backoffice extension API (`@umbraco-cms/backoffice`). Any of these can shift between majors.

## Decision

1. **One source tree, one package by default.** Compile against the lowest supported 17.x and
   declare the dependency as `Umbraco.Cms.* [17.0.0, 19.0.0)`. CI boots both sample sites (17 and
   18) and runs the API and end-to-end tests against each.
2. **Fallback if 18 breaks something we use:** keep the single source tree and add an MSBuild
   property (`UmbracoMajor=17|18`) that switches the Umbraco package version and defines a
   compilation symbol (`UMBRACO_18`). The pipeline then packs one build per major from the same
   source (package version lines `17.x` / `18.x`, or one ID with major-specific dependency ranges -
   decided when it happens). Version-specific code sits behind the symbol in the smallest possible
   seam, never scattered `#if` blocks.
3. The backoffice client is built once, against the `@umbraco-cms/backoffice` version of the lowest
   supported major, unless the fallback above is triggered.

## Consequences

- Most packages and users get one install that works on both majors.
- Any 17 vs 18 difference found by CI becomes a new ADR that either confirms the single build or
  triggers the fallback.
- Umbraco 16 and earlier are out of scope.
