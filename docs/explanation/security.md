# The security model

Logs can hold personal data, request details and stack traces, so the explorer is deliberately
narrow about who can use it and what leaves the server.

## Who can use it

- The explorer and every endpoint of its API require access to the **Settings** section, the same
  as the core Log Viewer.
- Each source can be limited to user groups with `AllowedUserGroups`. A source marked
  `"Sensitive": true` is shown with a lock and, unless groups are listed, only to the `admin`
  group. A source you may not see is left out of the source list, and calling it directly returns
  `forbidden_source`.

## What leaves the server

- The browser only receives a source's alias, display name, type, sensitive flag and capabilities.
  Connection strings, API keys and other settings never leave the server.
- Log text is always rendered as text, never as HTML, so a logged script cannot run in the
  backoffice.

## Read-only

The explorer only reads. It never writes to, deletes from or rotates your log files, and a native
query can only filter entries, never change them.

## Coming in 1.0

- **Masking:** every response replaces the values of properties whose names look secret
  (`*Password*`, `*Secret*`, `Authorization`, `Cookie`, `*Token*`) and redacts e-mail addresses.
  You cannot filter on a masked property, so nobody can probe for its value.
- **Audit:** an Umbraco audit entry when someone queries a sensitive source.
- **Load limits:** at most four queries in flight per user.

Until masking ships, treat anything your site logs as visible to everyone with Settings access.

## Reporting a vulnerability

See [SECURITY.md](../../SECURITY.md).
