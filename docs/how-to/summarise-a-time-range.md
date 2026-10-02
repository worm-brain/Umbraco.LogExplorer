# Summarise a time range

The **Overview** view summarises everything matching the query bar in three panels. Open it with
the **Overview** tab in the workspace header.

![The Overview view: entries by level with minimum levels, most frequent messages, and exception types](../images/overview.png)

## Entries by level

Every level's count with a proportional bar. Levels hidden by the level filter stay listed, muted,
so the summary never hides a number.

For the log files source the panel also lists **Minimum levels (configuration)**: the minimum level
each Serilog sink is configured to write, such as `Global` and `UmbracoFile`. A sink set to
`Warning` explains why you see no INFO entries from it.

## Most frequent messages

The six message templates with the most entries. Select one to see its entries in the Search view.
For the full list, with level mix and volume, use [Patterns](find-frequent-messages.md).

## Exception types

Every exception type in the range with its count. Select one to see those entries in Search. The
panel says so when the range has no exceptions.

## Related

- [Narrow the time range](narrow-the-time-range.md)
- [Find the most frequent messages](find-frequent-messages.md)
