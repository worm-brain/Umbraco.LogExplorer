/**
 * A run of stack-trace lines: `framework` runs are the ones the drawer collapses behind
 * "Show framework frames" (UI brief §4.11 section 6).
 */
export interface StackRun {
  framework: boolean;
  lines: Array<string>;
}

/**
 * Namespaces counted as framework code: the .NET runtime, ASP.NET Core and other Microsoft
 * libraries, and Umbraco itself. What is left is the site's own code, which is what a developer
 * reading the trace is looking for. `Umbraco.Cms.` rather than `Umbraco.`, so community packages
 * named `Umbraco.Community.*` (this one included) stay visible.
 */
const FRAMEWORK_PREFIXES = ["System.", "Microsoft.", "Umbraco.Cms."];

/** `   at Namespace.Type.Method(...)`, capturing the method's full name. */
const FRAME = /^\s*at\s+([^\s(]+)/;

/** The separator .NET writes between the frames of an awaited call chain. */
const ASYNC_BOUNDARY = /^\s*--- End of stack trace from previous location/;

/**
 * Whether a stack-trace line is a framework frame (see {@link FRAMEWORK_PREFIXES}). The async
 * boundary marker counts too: it only separates frames and means nothing once they are hidden.
 *
 * @param line - One line of `LogException.stackTrace`.
 * @returns `true` for a framework frame; `false` for the site's frames and for any line that is
 *   not a frame (the exception header, `--->` inner exceptions).
 */
export function isFrameworkFrame(line: string): boolean {
  if (ASYNC_BOUNDARY.test(line)) return true;
  const method = FRAME.exec(line)?.[1];
  return method !== undefined && FRAMEWORK_PREFIXES.some((prefix) => method.startsWith(prefix));
}

/**
 * Splits a .NET stack trace into consecutive runs of framework and other lines, in order.
 *
 * @param stackTrace - `LogException.stackTrace`: the `ToString()` of the exception, so the type
 *   and message line comes first. `\r\n` and `\n` line ends both work.
 * @returns The runs; empty for an empty trace.
 */
export function splitStackTrace(stackTrace: string | null | undefined): Array<StackRun> {
  const runs: Array<StackRun> = [];
  if (!stackTrace) return runs;
  for (const line of stackTrace.split(/\r?\n/)) {
    const framework = isFrameworkFrame(line);
    const last = runs.at(-1);
    if (last && last.framework === framework) last.lines.push(line);
    else runs.push({ framework, lines: [line] });
  }
  return runs;
}

/**
 * Renders the runs as the text of the stack-trace code block.
 *
 * @param runs - From {@link splitStackTrace}.
 * @param showFramework - `true` shows every line; `false` replaces each framework run with one
 *   placeholder line, indented like a frame so the trace keeps its shape.
 * @param placeholder - The placeholder text for a run of `count` hidden lines.
 * @returns The lines joined with `\n`.
 */
export function renderStackTrace(
  runs: ReadonlyArray<StackRun>,
  showFramework: boolean,
  placeholder: (count: number) => string,
): string {
  return runs
    .flatMap((run) => (run.framework && !showFramework ? [`   ${placeholder(run.lines.length)}`] : run.lines))
    .join("\n");
}
