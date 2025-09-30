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

test("dialog runs detect → translate → rewrite and submits rewritten text", async () => {
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
            features: { terminologyToggle: true, toneToggle: true },
            pricing: { currency: "USD" }
          };
        }
        if (url === "/api/detect") {
          return { language: "en", confidence: 0.9 };
        }
        if (url === "/api/translate") {
          return { text: "hola", detectedLanguage: "en", metadata: { modelId: "model-a", tone: "neutral" } };
        }
        if (url === "/api/rewrite") {
          return { text: "【正式】hola", metadata: { tone: "formal" } };
        }
        throw new Error(`unexpected url ${url}`);
      }
    };
  };

  const ui = {
    modelSelect: createStubElement(),
    sourceSelect: createStubElement(),
    targetSelect: createStubElement(),
    terminologyToggle: createStubElement({ checked: true }),
    toneToggle: createStubElement({ checked: false }),
    detectedLabel: createStubElement(),
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
  ui.toneToggle.checked = true;
  await ui.toneToggle.trigger("change");
  await ui.input.trigger("input");
  await ui.previewButton.trigger("click");
  assert.equal(fetchCalls[0].url, "/api/detect");
  assert.equal(fetchCalls[1].url, "/api/translate");
  assert.equal(fetchCalls[1].body.text, "hello");
  await ui.submitButton.trigger("click");
  assert.equal(fetchCalls[2].url, "/api/rewrite");
  assert.equal(teams.dialog.lastSubmit.translation, "【正式】hola");
  assert.equal(teams.dialog.lastSubmit.tone, "formal");
  assert.equal(ui.translation.value, "【正式】hola");
});

test("dialog defaults to first non-auto target and preserves detection label", async () => {
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
            features: { terminologyToggle: true, toneToggle: true },
            pricing: { currency: "USD" }
          };
        }
        if (url === "/api/detect") {
          return { language: "fr", confidence: 0.6 };
        }
        if (url === "/api/translate") {
          return { text: "bonjour", detectedLanguage: "fr", metadata: { modelId: "model-a" } };
        }
        if (url === "/api/rewrite") {
          return { text: "bonjour", metadata: { tone: "neutral" } };
        }
        throw new Error(`unexpected url ${url}`);
      }
    };
  };

  const ui = {
    modelSelect: createStubElement(),
    sourceSelect: createStubElement(),
    targetSelect: createStubElement(),
    terminologyToggle: createStubElement({ checked: true }),
    toneToggle: createStubElement({ checked: true }),
    detectedLabel: createStubElement(),
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
        return { tenant: { id: "tenant" }, user: { id: "user" }, channel: { id: "channel" }, app: { locale: "ja-JP" } };
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
  assert.equal(fetchCalls[0].url, "/api/detect");
  assert.match(ui.detectedLabel.textContent, /Français/);
  await ui.submitButton.trigger("click");
  assert.equal(fetchCalls.at(-1).url, "/api/rewrite");
});
