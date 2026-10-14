<!--
Before opening this PR, read CONTRIBUTING.md.
Pull requests are issue-first and invitation-only: they must close an issue a maintainer has
already accepted (labelled `accepted` or `help wanted`). PRs without an accepted issue behind
them may be closed with a pointer to the policy, regardless of code quality.
-->

## Linked issue

Closes #<!-- issue number -->

<!-- The issue above must be labelled `accepted` or `help wanted`. If it is not, this PR is
     premature - open/triage the issue first. -->

## What this changes

<!-- One or two sentences. Keep the diff scoped to this one issue. Screenshots help for UI changes. -->

## Checklist

- [ ] This PR closes a single issue that is labelled `accepted` or `help wanted`.
- [ ] The diff is scoped to that issue - no unrelated reformatting, renames, or dependency bumps.
- [ ] `dotnet build -warnaserror` and `dotnet test` pass locally for Umbraco 17 and 18 (`-p:UmbracoMajor=18`).
- [ ] If the client changed: `bun run format:check`, `typecheck`, `test` and `build` pass.
- [ ] Formatted with CSharpier and Prettier.
- [ ] C# members I touched have XML doc comments; exported TypeScript I touched has TSDoc.
- [ ] New or changed behaviour has tests (happy path + obvious failure path).
- [ ] A user-visible change has a line under `## [Unreleased]` in `CHANGELOG.md` and updates its page in `docs/`.

## AI assistance

- [ ] AI tools were used to write or substantially shape this change.

<!-- If checked: a human is accountable for this PR, has reviewed every hunk, and can explain
     and revise it under review. See CONTRIBUTING.md. Disclosure is context, not a mark against
     the PR - do not hide it. -->
