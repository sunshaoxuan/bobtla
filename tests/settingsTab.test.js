import test from "node:test";
import assert from "node:assert/strict";
import {
  renderGlossaryList,
  renderConflictList,
  renderErrorList
} from "../src/webapp/settingsTab.js";

test("renderGlossaryList maps entries to readable strings", () => {
  const container = {};
  renderGlossaryList(container, [
    { source: "CPU", target: "中央处理器", scope: "tenant:contoso" }
  ]);
  assert.deepEqual(container.items, ["CPU → 中央处理器（tenant:contoso）"]);
});

test("renderConflictList includes both existing and incoming targets", () => {
  const container = {};
  renderConflictList(container, [
    { source: "CPU", existingTarget: "中央处理器", incomingTarget: "处理器" }
  ]);
  assert.ok(container.items[0].includes("CPU"));
  assert.ok(container.items[0].includes("中央处理器"));
  assert.ok(container.items[0].includes("处理器"));
});

test("renderErrorList clears container when no errors provided", () => {
  const container = {};
  renderErrorList(container, []);
  assert.deepEqual(container.items, []);
});
