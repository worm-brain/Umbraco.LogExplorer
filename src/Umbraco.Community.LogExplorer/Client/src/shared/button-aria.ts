/** The `data-*` attributes {@link syncButtonAria} copies, and the ARIA attribute each becomes. */
const FORWARDED = {
  pressed: "aria-pressed",
  expanded: "aria-expanded",
  haspopup: "aria-haspopup",
} as const;

/**
 * Copies ARIA state from `uui-button` hosts onto the `<button>` in their shadow roots.
 *
 * `uui-button` forwards only `aria-label`/`aria-labelledby` to that inner button, and the inner
 * button is what takes focus and what assistive technology reads, so `aria-pressed` or
 * `aria-expanded` set on the host never reach anyone (verified in 17 and 18; #51). Elements put
 * the state on the host as `data-pressed`, `data-expanded` or `data-haspopup` instead and call
 * this after each update; it waits for each button's own render first.
 *
 * @param root - Where to look for `uui-button`s, usually the element's `renderRoot`.
 * @returns Resolves once every button found has been updated.
 */
export async function syncButtonAria(root: ParentNode): Promise<void> {
  const selector = Object.keys(FORWARDED)
    .map((name) => `uui-button[data-${name}]`)
    .join(", ");
  const buttons = Array.from(root.querySelectorAll<HTMLElement & { updateComplete?: Promise<unknown> }>(selector));
  for (const button of buttons) {
    await button.updateComplete;
    const inner = button.shadowRoot?.querySelector("#button");
    if (!inner) continue;
    for (const [name, aria] of Object.entries(FORWARDED)) {
      const value = button.dataset[name];
      if (value !== undefined) inner.setAttribute(aria, value);
    }
  }
}
