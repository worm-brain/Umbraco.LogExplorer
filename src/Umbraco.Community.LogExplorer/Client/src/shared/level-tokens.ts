import { css } from "@umbraco-cms/backoffice/external/lit";

/**
 * The six severity colours (UI brief §5.1), defined once as plugin-scoped tokens on `:host`.
 * Add this to the `styles` of any element that shows a level badge or toggle, and colour with
 * `var(--log-explorer-level-{level})` and `var(--log-explorer-level-{level}-contrast)`.
 *
 * No UUI token set covers six levels. The UI brief suggests reusing `--uui-color-positive`,
 * `-warning` and `-danger` where close, but in the backoffice's dark theme the positive and
 * danger pairs drop below 4.5:1 (about 4.4:1 and 4.3:1), so every level uses the brief's palette,
 * which reads the same on light and dark surfaces. Contrast of each fill/text pair, checked with
 * the WCAG formula: TRACE 9.4, DEBUG 5.1, INFO 5.3, WARN 8.3, ERROR 7.6, FATAL 15.5.
 *
 * @cssprop --log-explorer-level-trace - TRACE fill; `-contrast` is its text colour.
 * @cssprop --log-explorer-level-debug - DEBUG fill.
 * @cssprop --log-explorer-level-info - INFO fill.
 * @cssprop --log-explorer-level-warn - WARN fill.
 * @cssprop --log-explorer-level-error - ERROR fill.
 * @cssprop --log-explorer-level-fatal - FATAL fill.
 */
export const LEVEL_TOKENS = css`
  :host {
    --log-explorer-level-trace: #d9d9de;
    --log-explorer-level-trace-contrast: #303033;
    --log-explorer-level-debug: #4a6fa5;
    --log-explorer-level-debug-contrast: #ffffff;
    --log-explorer-level-info: #1e7a4c;
    --log-explorer-level-info-contrast: #ffffff;
    --log-explorer-level-warn: #f2c94c;
    --log-explorer-level-warn-contrast: #303033;
    --log-explorer-level-error: #a5133f;
    --log-explorer-level-error-contrast: #ffffff;
    --log-explorer-level-fatal: #4a0a1c;
    --log-explorer-level-fatal-contrast: #ffffff;
  }
`;
