// BRIEF §14 Phase 1 criterion 1: from an ERROR entry, a user who does not know Serilog syntax sees
// every entry from the same request in at most 2 clicks.
import { test } from "./support/test.js";

test.fixme("from an ERROR entry, Same request shows the whole request in two clicks (#42)", async () => {
  // Waits for #42 (Same request in the entry drawer): open an ERROR row, select Same request,
  // then expect one Request chip and every row sharing that request id.
});
