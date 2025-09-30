import test from "node:test";
import assert from "node:assert/strict";
import { initMessageExtensionDialog } from "../src/webapp/dialog.js";

function createStubElement(initial = {}) {
  return {
    value: initial.value ?? "",
    checked: Boolean(initial.checked),
    textContent: initial.textContent ?? "",
    hidden: false,
    listeners: new Map(),
    replaceChildren() {},
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

test("initMessageExtensionDialog posts payload and updates translation", async () => {
  const fetchCalls = [];
  const fakeFetch = async (url, options = {}) => {
    if (options.body) {
      fetchCalls.push({ url, body: JSON.parse(options.body) });
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
        return { text: "hola", metadata: { modelId: "model-a" } };
      }
    };
  };

  const ui = {
    modelSelect: createStubElement(),
    sourceSelect: createStubElement(),
    targetSelect: createStubElement(),
    terminologyToggle: createStubElement({ checked: true }),
    costHint: createStubElement(),
    input: createStubElement({ value: "hello" }),
    translation: createStubElement(),
    previewButton: createStubElement(),
    submitButton: createStubElement(),
    errorBanner: createStubElement()
  };

  const teams = {
    app: {
      async initialize() {},
      async getContext() {
        return { tenant: { id: "tenant" }, user: { id: "user" }, channel: { id: "channel" }, app: { locale: "en-US" } };
      }
    },
    dialog: {
      submit(payload) {
        teams.dialog.lastSubmit = payload;
      }
    }
  };

  await initMessageExtensionDialog({ ui, teams, fetcher: fakeFetch });
  await ui.input.trigger("input");
  await ui.previewButton.trigger("click");
  assert.equal(fetchCalls.length, 1);
  assert.equal(fetchCalls[0].url, "/api/translate");
  assert.equal(fetchCalls[0].body.text, "hello");
  await ui.submitButton.trigger("click");
  assert.equal(teams.dialog.lastSubmit.translation, "hola");
});

test("initMessageExtensionDialog falls back to first non-auto target", async () => {
  const fetchCalls = [];
  const fakeFetch = async (url, options = {}) => {
    if (options.body) {
      fetchCalls.push({ url, body: JSON.parse(options.body) });
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
        return { text: "bonjour", metadata: { modelId: "model-a" } };
      }
    };
  };

  const ui = {
    modelSelect: createStubElement(),
    sourceSelect: createStubElement(),
    targetSelect: createStubElement(),
    terminologyToggle: createStubElement({ checked: true }),
    costHint: createStubElement(),
    input: createStubElement({ value: "hello" }),
    translation: createStubElement(),
    previewButton: createStubElement(),
    submitButton: createStubElement(),
    errorBanner: createStubElement()
  };

  const teams = {
    app: {
      async initialize() {},
      async getContext() {
        return { tenant: { id: "tenant" }, user: { id: "user" }, channel: { id: "channel" }, app: { locale: "zh-CN" } };
      }
    },
    dialog: {
      submit(payload) {
        teams.dialog.lastSubmit = payload;
      }
    }
  };

  const { state } = await initMessageExtensionDialog({ ui, teams, fetcher: fakeFetch });
  await ui.input.trigger("input");
  await ui.previewButton.trigger("click");

  assert.equal(state.targetLanguage, "fr");
  assert.equal(ui.targetSelect.value, "fr");
  assert.equal(fetchCalls.length, 1);
  assert.equal(fetchCalls[0].body.targetLanguage, "fr");
});
