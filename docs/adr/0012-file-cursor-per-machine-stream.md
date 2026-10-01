# ADR 0012: The files cursor holds one position per machine stream; record ids are base64url

- Status: Accepted
- Date: 2026-10-01
- Issue: #31

## Context

BRIEF §10.1 described the files cursor as "file name + byte offset + direction" and the record
`Id` as "file name + byte offset of the line". Paging merges every machine's files into one
timestamp order (load-balanced sites write `UmbracoTraceLog.{MACHINE}.{yyyyMMdd}.json` side by
side). A single file and offset cannot say where each of the other machines' files had got to, so
resuming from it would either repeat or skip their events.

## Decision

1. Each machine's files form one **stream**, ordered by day then roll index (reversed for
   newest-first). Files without a machine name in their format form one stream.
2. The cursor is base64url (no padding) of JSON `{"d":"asc"|"desc","p":[{"f":fileName,"o":offset}]}`
   with **one entry per unfinished stream**, holding its next unread position:
   - ascending, `o` is the start of the next line to read forwards;
   - descending, `o` is the exclusive end for the reverse reader (lines starting before it are next).
   A stream with no entry is finished. A cursor whose direction differs from the query's sort, or
   that does not decode, is rejected with `ArgumentException`.
3. If a cursor's file no longer exists (retention) or now falls outside the range, that stream
   resumes at the next file after it in the stream's order. Cursor file names are only matched
   against the located files, never opened as paths.
4. Streams merge on the next event's timestamp, ties broken by file name then offset (exactly
   reversed for newest-first), so the order is total and a walk returns each record once.
5. The record `Id` is base64url (no padding) of `{fileName}:{offset}`, so it goes in routes as is.
6. Known limit: events within a file are assumed to be in time order, as Serilog writes them, so a
   stream stops at the first event past the far edge of the range. An event written slightly out of
   order just inside that edge can be missed.

## Consequences

- BRIEF §10.1's cursor and Id bullet points here.
- A machine whose files first appear after a cursor was issued is not picked up by that cursor;
  a new query (or refresh) sees it.
- Cursors grow with the number of machines, which is small in practice.
- Context (#33) decodes record ids with `FilePosition.TryParseRecordId` and resolves the name
  against the locator's files.
