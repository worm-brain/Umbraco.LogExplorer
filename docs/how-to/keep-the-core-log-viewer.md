# Keep the core Log Viewer

The Log Explorer adds a **Log Explorer** item under **Settings > Advanced** and, by default, hides
the core **Log Viewer** item next to it, so editors see one log tool.

To show both, set `HideCoreLogViewer` to `false`:

```json
{
  "LogExplorer": {
    "HideCoreLogViewer": false
  }
}
```

Restart the site. Both items then appear under **Settings > Advanced**.

Keeping both is useful while a team moves over: the [generated query](use-the-query-language.md)
is in the core Log Viewer's own dialect, so you can paste it there and compare.

## Related

- [Configuration reference](../reference/configuration.md)
