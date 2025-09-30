import test from "node:test";
import assert from "node:assert/strict";
import { initComposePlugin } from "../src/webapp/composePlugin.js";

function createStubElement(initial = {}) {
  return {
    value: initial.value ?? "",
    checked: Boolean(initial.checked),
    textContent: initial.textContent ?? "",
    optionsData: [],
    listeners: new Map(),
    addEventListener(event, handler) {
      this.listeners.set(event, handler);
    },
    trigger(event) {
      const handler = this.listeners.get(event);
      if (handler) {
        return handler({ target: this });
      }
      return undefined;
    }
  };
}

test("compose plugin sends translate request with compose text", async () => {
  const fetchCalls = [];
  const fakeFetch = async (url, options = {}) => {
    if (options.body) {
      fetchCalls.push({ url, options: JSON.parse(options.body) });
    }
    return {
      ok: true,
      async json() {
        if (url === "/api/metadata") {
          return {
            models: [{ id: "model-a", displayName: "Model A", costPerCharUsd: 0.00002 }],
            languages: [
              { id: "auto", name: "Auto", isDefault: true },
              { id: "es", name: "Español" }
            ],
            features: { terminologyToggle: true },
            pricing: { currency: "USD" }
          };
        }
        return { text: "hola" };
      }
    };
  };
  const suggestButton = createStubElement();
  const applyButton = createStubElement();
  const input = createStubElement({ value: "hello" });
  const preview = createStubElement();
  const targetSelect = createStubElement({ value: "es" });
  targetSelect.replaceChildren = () => {};

  const teams = {
    app: {
      async initialize() {
        return undefined;
      },
      async getContext() {
        return { tenant: { id: "t" }, user: { id: "u" }, channel: { id: "c" }, app: { locale: "en-US" } };
      }
    },
    conversations: {
      async sendMessageToConversation() {
        return undefined;
      }
    }
  };

  await initComposePlugin({
    ui: { input, targetSelect, suggestButton, applyButton, preview },
    teams,
    fetcher: fakeFetch
  });

  await suggestButton.trigger("click");
  assert.equal(fetchCalls.length, 1);
  assert.equal(fetchCalls[0].url, "/api/translate");
  assert.equal(fetchCalls[0].options.text, "hello");
  await applyButton.trigger("click");
  assert.equal(preview.value || preview.textContent, "hola");
});

test("compose plugin falls back to first non-auto target", async () => {
  const fetchCalls = [];
  const fakeFetch = async (url, options = {}) => {
    if (options.body) {
      fetchCalls.push({ url, options: JSON.parse(options.body) });
    }
    return {
      ok: true,
      async json() {
        if (url === "/api/metadata") {
          return {
            models: [{ id: "model-a", displayName: "Model A", costPerCharUsd: 0.00002 }],
            languages: [
              { id: "auto", name: "Auto", isDefault: true },
              { id: "fr", name: "Français" },
              { id: "de", name: "Deutsch" }
            ],
            features: { terminologyToggle: true },
            pricing: { currency: "USD" }
          };
        }
        return { text: "bonjour" };
      }
    };
  };

  const suggestButton = createStubElement();
  const input = createStubElement({ value: "hello" });
  const preview = createStubElement();
  const targetSelect = createStubElement();
  targetSelect.replaceChildren = () => {};

  const teams = {
    app: {
      async initialize() {},
      async getContext() {
        return { tenant: { id: "tenant" }, user: { id: "user" }, channel: { id: "channel" }, app: { locale: "ja-JP" } };
      }
    },
    conversations: {
      async sendMessageToConversation() {}
    }
  };

  const { state } = await initComposePlugin({
    ui: { input, targetSelect, suggestButton, preview },
    teams,
    fetcher: fakeFetch
  });

  await suggestButton.trigger("click");

  assert.equal(state.targetLanguage, "fr");
  assert.equal(targetSelect.value, "fr");
  assert.equal(fetchCalls.length, 1);
  assert.equal(fetchCalls[0].options.targetLanguage, "fr");
});
