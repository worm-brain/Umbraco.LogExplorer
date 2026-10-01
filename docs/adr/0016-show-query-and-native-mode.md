# ADR 0016: Show query compiles on every change; native mode is gated by allowNativeQuery

- Status: Accepted
- Date: 2026-10-01
- Issue: #43

## Context

Issue #43 adds `POST /sources/{alias}/compile` and `/validate`, the show-query panel (UI brief
§4.6), disabled chips for nodes the source cannot run (BRIEF §6.3) and native mode (BRIEF §6.2).
Several things the briefs leave open had to be settled:

- `LogSourceBase.Compile` is gated by the `NativeQuery` feature (ADR 0008), so a source with no
  native language cannot compile at all.
- `AllowNativeQuery` (BRIEF §12) lives on `LogSourceDefinition`, which the UI never sees, and no
  shared code enforced it.
- Unsupported chips must be visible and disabled whether or not the panel is open, and must never
  reach `/search` (where a provider would reject them).
- The search box is a single-line `uui-input`, while compiled queries have one clause per line.

## Decision

1. **Compile needs `nativeQuery`.** `/compile` keeps the base-class gate and answers
   `unsupported_feature` for a source without a native language. The "Show generated query"
   button and the panel are hidden for such a source: there is nothing to show.
2. **`AllowNativeQuery: false` is enforced by the registry.** It wraps the source in
   `NativeQueryDisabledSource`, which refuses any query carrying a native query and
   `ValidateNative` with `NotSupportedException` (`unsupported_feature`) and passes everything
   else through, compile included. `GET /sources` adds `allowNativeQuery` (the source declares
   `nativeQuery` and is not wrapped). Sources declare `nativeQuery` whatever the setting; the
   files source no longer drops it when `AllowNativeQuery` is false, so "Show query" stays there
   too (follow-up to #43). The UI hides native mode and "Edit as native" when it is
   false; "Show query" stays.
3. **The client compiles on every change that affects the output**, panel open or not (debounced
   150 ms, one request in flight, skipped when only range, sort, page or cursor changed). Compiling
   touches no store, so the cost is small, and it keeps the not-shown list current.
4. **"Cannot show" is not "cannot run"** (Jack, follow-up to #43). A chip is **unsupported**,
   drawn disabled ("Not supported by {source}") and left out of `queryState`, only when the source
   cannot run it: it holds a condition whose operator the source does not declare in
   `capabilities.operators` (known at once, so a just-added chip the source cannot run never
   reaches `/search`). A chip holding a node the latest compile returned in `unsupported` is
   **not expressible**: the source runs it, so it stays active and filters as usual, and the
   show-query panel lists it under the query as "Not shown in {language}: {chip}" (its tooltip
   says the same). The context exposes these as `unsupportedChips` and `notExpressibleChips`.
   The compile is sent the query exactly as `queryState` runs it, so the panel never shows a
   clause that does not run.
5. **Native mode** is `native` in the view state being a string, `""` included (written to the URL
   as `native=`), on a source with `allowNativeQuery`. "Edit as native" moves the compiled text into
   it with clauses joined by spaces (a text input drops line breaks), clears the chips and level
   toggles it expresses, and keeps the unsupported and not-expressible chips as chips; the
   not-expressible ones go on ANDing with the native query, and a notification says so. "Edit as
   native" is disabled, with the reason in the panel, when keeping them would split an OR group
   (include chips on one field, some expressible and some not), because the compiled text would
   hold half of the OR. Enter stores the text; chips still AND with it. Input is validated 400 ms after the last keystroke, and the committed query is
   validated when native mode opens, so an `invalid_native_query` from `/search` also shows its
   position in the box.
6. **The panel's open state is in the URL** as `sq=1`, so a shared link opens with it.
7. The sample `Fake` source gains an `Operators` setting (comma list) and a native validator that
   checks quotes and brackets and reports a position; its `QueryAsync` throws
   `InvalidNativeQueryException` for an invalid native query. It still does not evaluate native
   queries.

## Consequences

- BRIEF §11.1 lists `allowNativeQuery` on `/sources` and the `nativeQuery` requirement of
  `/compile`; §12 describes the enforcement.
- Panels added later that query the source (facets, patterns, overview) should read
  `queryState` rather than `state`, or they will send unsupported chips and native queries the
  source does not allow.
- For the files source, compile reports some nodes unsupported that its C# filter runs
  (`@exception.*`, `@timestamp`, `@resource.*`, ADR 0013 point 5). Under point 4 they stay
  active, so an Exception facet value clicked on the files source filters the results and is
  listed as not shown in Serilog Expressions.
