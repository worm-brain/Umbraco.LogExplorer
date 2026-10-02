# Find the most frequent messages

The **Patterns** view groups the entries matching your query by message template: the text before
values were filled in. It answers "which errors happen most this week?" and "what is filling my
logs?" without reading entries one by one.

Open it with the **Patterns** tab in the workspace header. The query bar stays, and still filters.

![The Patterns view: templates with highlighted placeholders, level-mix bars, sparklines, counts, and Focus and Mute buttons](../images/patterns.png)

## Read a pattern

Each row shows:

- **the template**, with its `{Placeholders}` highlighted, and under it the source and one sample
  message;
- **the level mix**, a bar of how the pattern's entries split by level;
- **the volume**, a sparkline across the time range, so a burst stands out from a steady trickle;
- **the count**, which the list is sorted by, highest first.

## Find the most frequent errors

1. Choose a range, such as **Last 7 days**.
2. Type `level:error` in the search box and press Enter. A line above the list names the level
   filter, with a **Show all levels** button.
3. Read the list from the top.

## Focus on a pattern or mute it

- **Focus** filters to that template and opens the Search view on its entries.
- **Mute** excludes that template from every view and confirms with "Muted: {template}". Use it
  for a chatty component you want out of the way. Remove the chip to bring it back.

You can do the same from an entry: **Same pattern** in the entry drawer filters on its template.

## Related

- [Summarise a time range](summarise-a-time-range.md)
- [Search and filter](search-and-filter.md)
