import test from "node:test";
import assert from "node:assert/strict";
import { JSDOM } from "jsdom";
import { fetchJson } from "../src/webapp/network.js";
import { onTelemetry, clearTelemetryListeners } from "../src/webapp/telemetry.js";

function createJsonResponse(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" }
  });
}

test("fetchJson retries failed requests and emits telemetry", async (t) => {
  let attempts = 0;
  const events = [];
  const dispose = onTelemetry((event) => events.push(event));
  t.after(dispose);
  t.after(clearTelemetryListeners);

  const fetchImpl = async (url) => {
    attempts += 1;
    if (attempts === 1) {
      return new Response("fail", { status: 502 });
    }
    return createJsonResponse({ ok: true, url });
  };

  const result = await fetchJson("/api/example", { fetchImpl, retries: 1, toast: false });
  assert.deepEqual(result, { ok: true, url: "/api/example" });
  assert.equal(attempts, 2);
  assert.ok(events.some((event) => event.name === "fetchJson" && event.status === 502));
  assert.ok(events.some((event) => event.name === "fetchJson" && event.status === 200));
});

test("fetchJson surfaces toast on repeated failure", async (t) => {
  const dom = new JSDOM(`<!doctype html><body></body>`);
  globalThis.window = dom.window;
  globalThis.document = dom.window.document;
  t.after(() => {
    dom.window.close();
    delete globalThis.window;
    delete globalThis.document;
  });

  const fetchImpl = async () => new Response("", { status: 503 });

  const result = await fetchJson("/api/unreachable", {
    fetchImpl,
    retries: 0,
    toastMessage: "无法加载数据",
    toastKey: "network-failure"
  });

  assert.equal(result, null);
  const toast = document.querySelector(".toast");
  assert.ok(toast, "toast should be rendered on failure");
  assert.equal(toast.textContent, "无法加载数据");
});
