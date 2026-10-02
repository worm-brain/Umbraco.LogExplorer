# Umbraco Log Explorer documentation

Umbraco Log Explorer is a click-to-filter log explorer for the Umbraco 17 and 18 backoffice. These
pages are organised by what you are trying to do, following [Diataxis](https://diataxis.fr/):

| If you want to... | Read |
| --- | --- |
| Learn the explorer by using it once, start to finish | [Tutorial](#tutorial) |
| Get one job done with a feature you already know about | [How-to guides](#how-to-guides) |
| Look up a setting, a search operator or an endpoint | [Reference](#reference) |
| Understand how it works and why it was built that way | [Explanation](#explanation) |

New here? Install the package (see the [README](../README.md#install)), then start with the
tutorial.

## Tutorial

- [Find your first error in five minutes](tutorials/find-your-first-error.md): open the explorer,
  zoom in on a spike, open an error and see the whole request it came from.

## How-to guides

One page per feature, each with screenshots:

- [Search and filter](how-to/search-and-filter.md): the search box, filter chips, the fields panel
  and the level toggles.
- [Narrow the time range](how-to/narrow-the-time-range.md): time range presets, a custom range and
  zooming with the histogram.
- [Inspect an entry](how-to/inspect-an-entry.md): the entry drawer, its properties and the
  exception.
- [Follow a request](how-to/follow-a-request.md): Same request and Around this.
- [Find the most frequent messages](how-to/find-frequent-messages.md): the Patterns view, Focus and
  Mute.
- [Summarise a time range](how-to/summarise-a-time-range.md): the Overview view.
- [Use the query language](how-to/use-the-query-language.md): Show generated query and native
  mode.
- [Share a view](how-to/share-a-view.md): copy a link that reopens exactly what you see.
- [Keep the core Log Viewer](how-to/keep-the-core-log-viewer.md): show both log tools under
  Settings.

## Reference

- [Configuration](reference/configuration.md): every `LogExplorer` setting and its default.
- [Search syntax](reference/search-syntax.md): what the search box understands.
- [Fields and levels](reference/fields.md): field names, aliases and how levels map.
- [Keyboard shortcuts](reference/keyboard-shortcuts.md)
- [Management API](reference/api.md): the endpoints behind the explorer, for scripts and tools.

## Explanation

- [The provider model](explanation/provider-model.md): sources, provider types and capabilities.
- [How log files are read](explanation/reading-log-files.md): why the newest entries load
  quickly, and what Approximate means.
- [The security model](explanation/security.md): who can use the explorer and what it can see.

## Extend

- [Writing a provider](extend/writing-a-provider.md): add your own log store as a source.

## Decisions

The [architecture decision records](adr/README.md) explain why the project is built the way it is.
