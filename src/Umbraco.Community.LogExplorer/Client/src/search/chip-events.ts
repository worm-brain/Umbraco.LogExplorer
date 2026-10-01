import type { FilterNode } from "../query/filter-node.js";

/**
 * Fired by a filter chip (or its editor) when the user saves an edit. Bubbles and is composed so
 * it reaches the search box across the chip's shadow root.
 */
export class LogExplorerChipChangeEvent extends Event {
  /** The event type. */
  static readonly TYPE = "log-explorer-chip-change";

  /**
   * @param chip - The edited chip, replacing the one that fired.
   */
  constructor(readonly chip: FilterNode) {
    super(LogExplorerChipChangeEvent.TYPE, { bubbles: true, composed: true });
  }
}

/** Fired by a filter chip when its remove button is pressed. Bubbles and is composed. */
export class LogExplorerChipRemoveEvent extends Event {
  /** The event type. */
  static readonly TYPE = "log-explorer-chip-remove";

  constructor() {
    super(LogExplorerChipRemoveEvent.TYPE, { bubbles: true, composed: true });
  }
}

/** Fired by the chip editor when Cancel is pressed. Not composed: only its chip listens. */
export class LogExplorerChipEditCancelEvent extends Event {
  /** The event type. */
  static readonly TYPE = "log-explorer-chip-edit-cancel";

  constructor() {
    super(LogExplorerChipEditCancelEvent.TYPE, { bubbles: true });
  }
}

declare global {
  interface HTMLElementEventMap {
    [LogExplorerChipChangeEvent.TYPE]: LogExplorerChipChangeEvent;
    [LogExplorerChipRemoveEvent.TYPE]: LogExplorerChipRemoveEvent;
    [LogExplorerChipEditCancelEvent.TYPE]: LogExplorerChipEditCancelEvent;
  }
}
