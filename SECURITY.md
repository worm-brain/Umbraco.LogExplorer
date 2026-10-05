# Security policy

## Supported versions

| Package line | Umbraco | Supported |
| --- | --- | --- |
| `17.x` | 17 | Yes |
| `18.x` | 18 | Yes |

Security fixes go into the newest release of each line.

## Reporting a vulnerability

Please do not open a public issue for a security problem.

Report it privately through GitHub: on the repository's **Security** tab, choose **Report a
vulnerability**. Include what you found, how to reproduce it, and which package version and
Umbraco version you used.

You will get an acknowledgement, and the fix will be coordinated with you before any details are
published.

## Scope

Log Explorer reads log files and shows them in the Umbraco backoffice, so reports about these are
especially welcome:

- a user seeing a source, or log entries, they should not;
- secrets or connection details reaching the browser;
- log content being rendered as HTML or script;
- access to the API without Settings section access.

See [the security model](docs/explanation/security.md) for what the explorer is designed to
guarantee today.
