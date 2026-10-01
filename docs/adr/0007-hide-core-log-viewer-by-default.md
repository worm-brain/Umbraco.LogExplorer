# ADR 0007: Hide the core Log Viewer by default

- Status: Accepted
- Date: 2026-10-01

## Context

The brief contradicted itself: §2 set `HideCoreLogViewer` to default `true`, the §13 configuration
example showed `false`.

## Decision

`HideCoreLogViewer` defaults to `true`. Installing the package replaces the core Settings > Log
Viewer menu item with Log Explorer. Setting it to `false` shows both.

## Consequences

- The explorer must cover the core viewer's daily uses from the first alpha (Gate 1).
- Hiding happens through the extension registry, so uninstalling the package restores the core item
  with no clean-up.
