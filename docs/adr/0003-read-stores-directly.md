# ADR 0003: Read log stores directly, never query through ILogViewerService

- Status: Accepted
- Date: 2026-10-01

## Context

Umbraco 17's `ILogViewerService` / `ILogViewerRepository` re-read the compact JSON files on every
request, refuse any period over 1 GB (`CanViewLogsAsync`), flatten properties to strings, page by
skip/take and accept only Serilog filter expressions. `ILogViewer` is obsolete and removed in 18.
See BRIEF §3.

## Decision

Every provider reads its store directly (`Serilog.Formatting.Compact.Reader` for files,
`LogsQueryClient` for Log Analytics, the Seq API for Seq). `ILogViewerService` is used for exactly
two things:

1. reading the configured minimum level per sink for the Overview view (files source only);
2. reading `umbracoLogViewerQuery` saved searches for the one-time import (Phase 2).

## Consequences

- No 1 GB limit, typed attributes, cursor paging.
- The files provider owns its own file discovery, which must match Umbraco's file naming (a Verify
  item in BRIEF §10.1).
