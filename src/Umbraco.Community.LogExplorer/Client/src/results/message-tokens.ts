/**
 * One run of message text. `field` is set when the run is a property value from the template, so
 * the results row can highlight it (and the entry drawer, #41, can make it a filter).
 */
export interface MessageToken {
  text: string;
  /** The template property the text was rendered from, for example `RequestPath`. */
  field?: string;
}

/** A parsed message template: literal text and `{Property}` holes, in order. */
type TemplatePart = { literal: string } | { hole: string };

/**
 * Splits a rendered message into literal text and property values (UI brief §4.9, BRIEF §11.2
 * "tokenise the template").
 *
 * The values are located in the rendered `body` rather than re-rendered from the attributes, so
 * the row shows exactly what the source logged, Serilog's quoting and format strings included.
 * The template's literal text is matched against the body in order and whatever lies between two
 * literals is the hole's value. When the two do not line up (the body was not rendered from this
 * template, two holes are adjacent so the split is ambiguous, or a value happens to contain the
 * following literal), the whole body comes back as one plain token rather than a wrong highlight.
 *
 * Every token is plain text; callers render it as text, never as HTML.
 *
 * @param body - `LogRecord.body`, the rendered message.
 * @param template - `LogRecord.messageTemplate`, when the source has templates.
 * @param attributes - `LogRecord.attributes`, used only when there is no body: values are then
 *   rendered from the template the way Serilog does (strings quoted, everything else as JSON).
 * @returns The tokens; an empty array when there is neither a body nor a template.
 */
export function tokeniseMessage(
  body: string | null | undefined,
  template: string | null | undefined,
  attributes: Readonly<Record<string, unknown>> = {},
): Array<MessageToken> {
  if (!template) return body ? [{ text: body }] : [];

  const parts = parseTemplate(template);
  if (body === null || body === undefined) return renderFromAttributes(parts, attributes);
  return matchBody(body, parts) ?? [{ text: body }];
}

/**
 * Parses Serilog message-template syntax: `{Name}`, `{@Name}`, `{$Name}`, `{Name:format}`,
 * `{Name,alignment}`; `{{` and `}}` are literal braces. An unclosed `{` is literal text.
 */
function parseTemplate(template: string): Array<TemplatePart> {
  const parts: Array<TemplatePart> = [];
  let literal = "";
  let i = 0;
  while (i < template.length) {
    const char = template[i]!;
    const next = template[i + 1];
    if ((char === "{" && next === "{") || (char === "}" && next === "}")) {
      literal += char;
      i += 2;
      continue;
    }
    if (char === "{") {
      const close = template.indexOf("}", i + 1);
      const name = close === -1 ? undefined : propertyName(template.slice(i + 1, close));
      if (close !== -1 && name) {
        if (literal) parts.push({ literal });
        literal = "";
        parts.push({ hole: name });
        i = close + 1;
        continue;
      }
    }
    literal += char;
    i += 1;
  }
  if (literal) parts.push({ literal });
  return parts;
}

/** The property name inside a hole, without the `@`/`$` operator, format or alignment. */
function propertyName(inner: string): string | undefined {
  const name = inner.replace(/^[@$]/, "").split(/[:,]/, 1)[0]!.trim();
  return /^[\w.]+$/.test(name) ? name : undefined;
}

function matchBody(body: string, parts: ReadonlyArray<TemplatePart>): Array<MessageToken> | undefined {
  const tokens: Array<MessageToken> = [];
  let position = 0;
  for (let index = 0; index < parts.length; index++) {
    const part = parts[index]!;
    if ("literal" in part) {
      if (!body.startsWith(part.literal, position)) return undefined;
      tokens.push({ text: part.literal });
      position += part.literal.length;
      continue;
    }

    const following = parts[index + 1];
    if (following && "hole" in following) return undefined;
    const end = following ? body.indexOf(following.literal, position) : body.length;
    if (end === -1) return undefined;
    tokens.push({ text: body.slice(position, end), field: part.hole });
    position = end;
  }
  return position === body.length ? tokens : undefined;
}

function renderFromAttributes(
  parts: ReadonlyArray<TemplatePart>,
  attributes: Readonly<Record<string, unknown>>,
): Array<MessageToken> {
  return parts.map((part) => {
    if ("literal" in part) return { text: part.literal };
    if (!(part.hole in attributes)) return { text: `{${part.hole}}` };
    return { text: JSON.stringify(attributes[part.hole]) ?? "", field: part.hole };
  });
}
