import test from "node:test";
import assert from "node:assert/strict";
import { initMessageExtensionDialog } from "../src/teamsClient/messageExtensionDialog.js";

function createStubElement(initial = {}) {
  return {
    value: initial.value ?? "",
    checked: Boolean(initial.checked),
    textContent: initial.textContent ?? "",
    hidden: initial.hidden ?? false,
    dataset: initial.dataset ?? {},
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
    ragToggle: createStubElement({ checked: false }),
    contextHintsInput: createStubElement(),
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
    ragToggle: createStubElement({ checked: false }),
    contextHintsInput: createStubElement(),
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
    ragToggle: createStubElement({ checked: false }),
    contextHintsInput: createStubElement(),
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

test("dialog surfaces offline draft completion state", async () => {
  const fetchCalls = [];
  const fakeFetch = async (url, options = {}) => {
    fetchCalls.push({ url, method: options.method ?? "GET" });
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
            features: { offlineDraft: true },
            pricing: { currency: "USD" }
          };
        }
        if (url.startsWith("/api/offline-draft") && (options.method ?? "GET") === "GET") {
          return {
            drafts: [
              { id: "draft-1", status: "SUCCEEDED", targetLanguage: "ja", resultText: "こんにちは" },
              { id: "draft-2", status: "FAILED", targetLanguage: "ja", errorReason: "budget" }
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
    ragToggle: createStubElement({ checked: false }),
    contextHintsInput: createStubElement(),
    detectedLabel: createStubElement(),
    costHint: createStubElement(),
    input: createStubElement({ value: "hello" }),
    translation: createStubElement(),
    previewButton: createStubElement(),
    submitButton: createStubElement(),
    errorBanner: createStubElement(),
    offlineSection: createStubElement({ hidden: true }),
    offlineStatus: createStubElement(),
    offlineList: {
      items: [],
      replaceChildren(...items) {
        this.items = items;
      }
    },
    offlineButton: createStubElement()
  };

  const teams = {
    app: {
      async initialize() {},
      async getContext() {
        return { tenant: { id: "tenant" }, user: { id: "user" }, channel: { id: "channel" }, app: { locale: "ja-JP" } };
      }
    },
    authentication: {
      async getAuthToken() {
        return "token";
      }
    },
    dialog: { submit() {} }
  };

  await initMessageExtensionDialog({ ui, teams, fetcher: fakeFetch });
  assert.equal(ui.offlineSection.hidden, false);
  assert.ok(ui.offlineStatus.textContent.includes("已完成翻译"));
  assert.equal(ui.offlineStatus.dataset.variant, "");
  assert.equal(ui.offlineList.items.length >= 1, true);
  assert.ok(String(ui.offlineList.items[0].textContent).includes("SUCCEEDED"));
  assert.ok(String(ui.offlineList.items[1].textContent).includes("FAILED"));
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
    ragToggle: createStubElement({ checked: false }),
    contextHintsInput: createStubElement(),
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
    ragToggle: createStubElement({ checked: false }),
    contextHintsInput: createStubElement(),
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

test("dialog saves offline drafts and refreshes list", async () => {
  const fetchCalls = [];
  const fakeFetch = async (url, options = {}) => {
    const method = options.method ?? "GET";
    const headers = options.headers ?? {};
    const bodyText = options.body;
    let body;
    if (bodyText) {
      body = JSON.parse(bodyText);
    }
    fetchCalls.push({ url, method, headers, body });
    if (url === "/api/metadata") {
      return {
        ok: true,
        async json() {
          return {
            models: [{ id: "model-a", displayName: "Model A", costPerCharUsd: 0.00002 }],
            languages: [
              { id: "auto", name: "Auto", isDefault: true },
              { id: "es", name: "Español" }
            ],
            features: { terminologyToggle: true, toneToggle: true, offlineDraft: true },
            pricing: { currency: "USD" }
          };
        }
      };
    }
    if (url.startsWith("/api/offline-draft") && method === "POST") {
      return {
        ok: true,
        status: 201,
        async json() {
          return { type: "offlineDraftSaved", draftId: 42, status: "PENDING" };
        }
      };
    }
    if (url.startsWith("/api/offline-draft") && method === "GET") {
      return {
        ok: true,
        async json() {
          return {
            drafts: [
              { id: 42, status: "PENDING", targetLanguage: "es", originalText: "hola" }
            ]
          };
        }
      };
    }
    if (url === "/api/detect") {
      return {
        ok: true,
        async json() {
          return { language: "en", confidence: 0.9 };
        }
      };
    }
    if (url === "/api/translate") {
      return {
        ok: true,
        async json() {
          return { text: "hola", detectedLanguage: "en", metadata: { modelId: "model-a" } };
        }
      };
    }
    if (url === "/api/rewrite") {
      return {
        ok: true,
        async json() {
          return { text: "hola", metadata: { tone: "neutral" } };
        }
      };
    }
    if (url === "/api/reply") {
      return {
        ok: true,
        async json() {
          return { status: "ok" };
        }
      };
    }
    throw new Error(`unexpected url ${url}`);
  };

  const offlineStatus = createStubElement();
  const offlineList = Object.assign(createStubElement(), {
    items: [],
    replaceChildren(...nodes) {
      this.items = nodes;
    }
  });
  const offlineSection = createStubElement();
  const offlineButton = createStubElement();

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
    errorBanner: createStubElement(),
    offlineStatus,
    offlineList,
    offlineSection,
    offlineButton
  };

  const teams = {
    app: {
      async initialize() {},
      async getContext() {
        return { tenant: { id: "tenant" }, user: { id: "user" }, channel: { id: "channel" }, app: { locale: "en-US" } };
      }
    },
    authentication: {
      async getAuthToken() {
        return "test-token";
      }
    },
    dialog: {
      submit() {}
    }
  };

  const { state } = await initMessageExtensionDialog({ ui, teams, fetcher: fakeFetch });
  assert.equal(offlineSection.hidden, false);
  assert.equal(offlineButton.hidden, false);

  await ui.offlineButton.trigger("click");
  assert.ok(
    /草稿正在后台翻译/.test(offlineStatus.textContent) ||
      /草稿 #42 已保存/.test(offlineStatus.textContent),
    `unexpected status message: ${offlineStatus.textContent}`
  );

  const postCall = fetchCalls.find((call) => call.url === "/api/offline-draft" && call.method === "POST");
  assert.ok(postCall, "expected offline draft POST call");
  assert.equal(postCall.headers.Authorization, "Bearer test-token");
  assert.equal(postCall.body.originalText, "hello");
  assert.equal(postCall.body.targetLanguage, state.targetLanguage);

  const listCall = fetchCalls.find((call) => call.url.startsWith("/api/offline-draft?") && call.method === "GET");
  assert.ok(listCall, "expected offline draft GET call");
  assert.equal(offlineList.items.length, 1);
  assert.ok(String(offlineList.items[0].textContent).includes("PENDING"));
});
