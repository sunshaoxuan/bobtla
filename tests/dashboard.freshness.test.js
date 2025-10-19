import test from "node:test";
import assert from "node:assert/strict";
import { JSDOM } from "jsdom";

function createMemoryStorage() {
  const data = new Map();
  return {
    getItem(key) {
      return data.has(key) ? data.get(key) : null;
    },
    setItem(key, value) {
      data.set(key, String(value));
    },
    removeItem(key) {
      data.delete(key);
    },
    clear() {
      data.clear();
    }
  };
}

async function loadInternals() {
  const module = await import("../src/webapp/app.js");
  return module.__dashboardInternals;
}

test("resolveDataFromCache records freshness metadata", async (t) => {
  const storage = createMemoryStorage();
  globalThis.localStorage = storage;
  t.after(() => {
    delete globalThis.localStorage;
  });

  const internals = await loadInternals();
  const {
    resolveDataFromCache,
    getDatasetFreshness,
    resetDatasetFreshness
  } = internals;

  resetDatasetFreshness();

  const fallback = { value: "offline" };
  const resolvedFallback = resolveDataFromCache("status", null, fallback);
  assert.equal(resolvedFallback, fallback);
  let freshness = getDatasetFreshness("status");
  assert.equal(freshness.source, "fallback");
  assert.equal(freshness.timestamp, null);

  const fresh = { updatedAt: "2024-05-18T10:00:00Z", value: "live" };
  const resolvedFresh = resolveDataFromCache("status", fresh, fallback);
  assert.equal(resolvedFresh, fresh);
  freshness = getDatasetFreshness("status");
  assert.equal(freshness.source, "network");
  assert.equal(freshness.timestamp, "2024-05-18T10:00:00Z");

  const cached = resolveDataFromCache("status", undefined, fallback);
  assert.deepEqual(cached, fresh);
  freshness = getDatasetFreshness("status");
  assert.equal(freshness.source, "cache");
  assert.equal(freshness.timestamp, "2024-05-18T10:00:00Z");
});

test("fallback timestamp is captured when provided", async (t) => {
  const storage = createMemoryStorage();
  globalThis.localStorage = storage;
  t.after(() => {
    delete globalThis.localStorage;
  });

  const internals = await loadInternals();
  const {
    resolveDataFromCache,
    getDatasetFreshness,
    resetDatasetFreshness
  } = internals;

  resetDatasetFreshness();
  const fallback = { generatedAt: "2024-04-01T08:30:00Z" };
  resolveDataFromCache("roadmap", null, fallback);
  const freshness = getDatasetFreshness("roadmap");
  assert.equal(freshness.source, "fallback");
  assert.equal(freshness.timestamp, "2024-04-01T08:30:00Z");
});

test("updateFreshnessIndicator updates metrics labels with source metadata", async (t) => {
  const dom = new JSDOM("<!DOCTYPE html><p data-metrics-updated>最近更新：--</p>");
  globalThis.window = dom.window;
  globalThis.document = dom.window.document;
  const storage = createMemoryStorage();
  globalThis.localStorage = storage;

  t.after(() => {
    delete globalThis.localStorage;
    delete globalThis.document;
    delete globalThis.window;
  });

  const internals = await loadInternals();
  const {
    resolveDataFromCache,
    resetDatasetFreshness,
    updateFreshnessIndicator
  } = internals;

  resetDatasetFreshness();

  resolveDataFromCache("metrics", null, { generatedAt: null });
  updateFreshnessIndicator("metrics", "[data-metrics-updated]", "最近更新：");

  const label = document.querySelector("[data-metrics-updated]");
  assert.equal(label.textContent, "最近更新：--（内置数据）");
  assert.equal(label.dataset.source, "fallback");

  resolveDataFromCache("metrics", { updatedAt: "2024-05-20T10:00:00Z" }, null);
  updateFreshnessIndicator("metrics", "[data-metrics-updated]", "最近更新：");

  assert.equal(label.dataset.source, "network");
  assert.ok(label.textContent.startsWith("最近更新："));
  assert.ok(!label.textContent.includes("内置数据"));
});
