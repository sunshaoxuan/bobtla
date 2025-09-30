import test from "node:test";
import assert from "node:assert/strict";
import { initComposePlugin } from "../src/teamsClient/composePlugin.js";

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

test("compose plugin translates and posts reply payload", async () => {
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
            features: { terminologyToggle: true, toneToggle: true },
            pricing: { currency: "USD" }
          };
        }
        if (url === "/api/translate") {
          return { text: "hola", detectedLanguage: "en", metadata: { modelId: "model-a", tone: "formal" } };
        }
        if (url === "/api/reply") {
          return { status: "ok" };
        }
        throw new Error(`unexpected url ${url}`);
      }
    };
  };
  const suggestButton = createStubElement();
  const applyButton = createStubElement();
  const input = createStubElement({ value: "hello" });
  const preview = createStubElement();
  const targetSelect = createStubElement({ value: "es" });
  targetSelect.replaceChildren = () => {};
  const toneToggle = createStubElement({ checked: true });
  const costHint = createStubElement();

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

  const { state } = await initComposePlugin({
    ui: { input, targetSelect, suggestButton, applyButton, preview, toneToggle, costHint },
    teams,
    fetcher: fakeFetch
  });

  assert.equal(state.sourceLanguage, "auto");
  toneToggle.checked = true;
  await toneToggle.trigger("change");
  await suggestButton.trigger("click");
  assert.equal(fetchCalls[0].url, "/api/translate");
  assert.equal(fetchCalls[0].options.text, "hello");
  assert.equal(state.detectedLanguage, "en");
  await applyButton.trigger("click");
  assert.equal(preview.value || preview.textContent, "hola");
  assert.equal(fetchCalls[1].url, "/api/reply");
  assert.equal(fetchCalls[1].options.translation, "hola");
  assert.equal(fetchCalls[1].options.sourceLanguage, "en");
  assert.equal(fetchCalls[1].options.metadata.tone, "formal");
  assert.equal(state.tone, "formal");
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
            features: { terminologyToggle: true, toneToggle: true },
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

test("compose plugin corrects target when locale missing from metadata", async () => {
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
              { id: "ja", name: "日本語" },
              { id: "ko", name: "한국어" }
            ],
            features: { terminologyToggle: true },
            pricing: { currency: "USD" }
          };
        }
        return { text: "こんにちは" };
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
        return { tenant: { id: "tenant" }, user: { id: "user" }, channel: { id: "channel" }, app: { locale: "pt-BR" } };
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

  assert.equal(state.targetLanguage, "ja");
  assert.equal(targetSelect.value, "ja");
  assert.equal(fetchCalls.length, 1);
  assert.equal(fetchCalls[0].options.targetLanguage, "ja");
});

test("compose plugin uses concrete target when user locale is unavailable", async () => {
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
            features: { terminologyToggle: true, toneToggle: true },
            pricing: { currency: "USD" }
          };
        }
        return { text: "hola" };
      }
    };
  };

  const suggestButton = createStubElement();
  const input = createStubElement({ value: "hello" });
  const preview = createStubElement();
  const targetSelect = Object.assign(createStubElement(), { replaceChildren() {} });

  const teams = {
    app: {
      async initialize() {},
      async getContext() {
        return { tenant: { id: "tenant" }, user: { id: "user" }, channel: { id: "channel" }, app: {} };
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

  const translateCall = fetchCalls.find((call) => call.url === "/api/translate");
  assert.equal(state.targetLanguage, "es");
  assert.equal(targetSelect.value, "es");
  assert.ok(translateCall, "expected translate call");
  assert.equal(translateCall.options.targetLanguage, "es");
});
