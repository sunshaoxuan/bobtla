import test from "node:test";
import assert from "node:assert/strict";
import { initMessageExtensionDialog } from "../src/teamsClient/messageExtensionDialog.js";

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
        if (url === "/api/reply") {
          return { status: "ok", card: { type: "AdaptiveCard", body: [] } };
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
  assert.equal(fetchCalls[3].url, "/api/reply");
  assert.equal(fetchCalls[3].body.translation, "【正式】hola");
  assert.equal(teams.dialog.lastSubmit.translation, "【正式】hola");
  assert.equal(teams.dialog.lastSubmit.tone, "formal");
  assert.deepEqual(teams.dialog.lastSubmit.card, { type: "AdaptiveCard", body: [] });
  assert.equal(ui.translation.value, "【正式】hola");
});

test("dialog forwards glossary conflict card to Teams host", async () => {
  const fetchCalls = [];
  const conflictCard = {
    type: "AdaptiveCard",
    version: "1.5",
    actions: [
      {
        type: "Action.Submit",
        data: {
          action: "resolveGlossary",
          pendingRequest: { text: "GPU", targetLanguage: "ja" }
        }
      }
    ],
    body: []
  };
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
              { id: "ja", name: "日本語" }
            ],
            features: { terminologyToggle: true, toneToggle: true },
            pricing: { currency: "USD" }
          };
        }
        if (url === "/api/detect") {
          return { language: "en", confidence: 0.9 };
        }
        if (url === "/api/translate") {
          return {
            type: "glossaryConflict",
            attachments: [
              {
                contentType: "application/vnd.microsoft.card.adaptive",
                content: conflictCard
              }
            ]
          };
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
    input: createStubElement({ value: "GPU" }),
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

  await initMessageExtensionDialog({ ui, teams, fetcher: fakeFetch });
  await ui.input.trigger("input");
  await ui.previewButton.trigger("click");

  assert.equal(fetchCalls[0].url, "/api/detect");
  assert.equal(fetchCalls[1].url, "/api/translate");
  assert.equal(teams.dialog.lastSubmit.type, "glossaryConflict");
  assert.deepEqual(teams.dialog.lastSubmit.card, conflictCard);
  assert.deepEqual(teams.dialog.lastSubmit.attachments, [
    { contentType: "application/vnd.microsoft.card.adaptive", content: conflictCard }
  ]);
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
        if (url === "/api/reply") {
          return { status: "ok", card: { type: "AdaptiveCard", body: [] } };
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
  assert.equal(fetchCalls.at(-2).url, "/api/rewrite");
  assert.equal(fetchCalls.at(-1).url, "/api/reply");
});

test("initMessageExtensionDialog corrects target when locale missing from metadata", async () => {
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
              { id: "ja", name: "日本語" },
              { id: "ko", name: "한국어" }
            ],
            features: { terminologyToggle: true },
            pricing: { currency: "USD" }
          };
        }
        if (url === "/api/detect") {
          return { language: "ja", confidence: 0.7 };
        }
        if (url === "/api/translate") {
          return { text: "こんにちは", metadata: { modelId: "model-a" } };
        }
        if (url === "/api/rewrite") {
          return { text: "こんにちは", metadata: { tone: "neutral" } };
        }
        if (url === "/api/reply") {
          return { status: "ok" };
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
        return { tenant: { id: "tenant" }, user: { id: "user" }, channel: { id: "channel" }, app: { locale: "pt-BR" } };
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
  await ui.submitButton.trigger("click");

  assert.equal(state.targetLanguage, "ja");
  assert.equal(ui.targetSelect.value, "ja");
  const translateCall = fetchCalls.find((call) => call.url === "/api/translate");
  const replyCall = fetchCalls.find((call) => call.url === "/api/reply");
  assert.equal(fetchCalls[0].url, "/api/detect");
  assert.equal(translateCall.body.targetLanguage, "ja");
  assert.equal(replyCall.body.targetLanguage, "ja");
});

test("dialog uses concrete target when user locale is unavailable", async () => {
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
          return { language: "en", confidence: 0.8 };
        }
        if (url === "/api/translate") {
          return { text: "hola", metadata: { modelId: "model-a" } };
        }
        if (url === "/api/rewrite") {
          return { text: "hola", metadata: { tone: "neutral" } };
        }
        if (url === "/api/reply") {
          return { status: "ok" };
        }
        throw new Error(`unexpected url ${url}`);
      }
    };
  };

  const ui = {
    modelSelect: createStubElement(),
    sourceSelect: createStubElement(),
    targetSelect: Object.assign(createStubElement(), { replaceChildren() {} }),
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
        return { tenant: { id: "tenant" }, user: { id: "user" }, channel: { id: "channel" }, app: {} };
      }
    },
    dialog: {
      submit() {}
    }
  };

  const { state } = await initMessageExtensionDialog({ ui, teams, fetcher: fakeFetch });
  await ui.input.trigger("input");
  await ui.previewButton.trigger("click");
  await ui.submitButton.trigger("click");

  const translateCall = fetchCalls.find((call) => call.url === "/api/translate");
  const replyCall = fetchCalls.find((call) => call.url === "/api/reply");
  assert.equal(state.targetLanguage, "es");
  assert.equal(ui.targetSelect.value, "es");
  assert.ok(translateCall, "expected translate call");
  assert.ok(replyCall, "expected reply call");
  assert.equal(translateCall.body.targetLanguage, "es");
  assert.equal(replyCall.body.targetLanguage, "es");
});

test("dialog surfaces reply failures without closing dialog", async () => {
  const fakeFetch = async (url, options = {}) => {
    return {
      ok: url !== "/api/reply",
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
        if (url === "/api/detect") {
          return { language: "en", confidence: 0.8 };
        }
        if (url === "/api/translate") {
          return { text: "hola", metadata: { modelId: "model-a" } };
        }
        if (url === "/api/rewrite") {
          return { text: "hola", metadata: { tone: "neutral" } };
        }
        if (url === "/api/reply") {
          return { error: "failed" };
        }
        throw new Error(`unexpected url ${url}`);
      },
      async text() {
        return "reply failed";
      },
      status: url === "/api/reply" ? 500 : 200
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
      submit() {
        teams.dialog.closed = true;
      }
    }
  };

  await initMessageExtensionDialog({ ui, teams, fetcher: fakeFetch });
  await ui.input.trigger("input");
  await ui.previewButton.trigger("click");
  await ui.submitButton.trigger("click");

  assert.equal(Boolean(teams.dialog.closed), false);
  assert.match(ui.errorBanner.textContent, /reply failed/);
});
