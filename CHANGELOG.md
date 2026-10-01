# Changelog

All notable user-visible changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Log Explorer item under Settings > Advanced with an empty Search, Patterns and Overview workspace.
- Simple search syntax parser in Core: `field:value`, `-field:value`, wildcards, comparisons, `has:field`, field aliases and `level:`/`level=` turn search box input into filter chips and a level set, with a plain-text fallback for an unbalanced quote.
