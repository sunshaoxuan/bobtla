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
      fetchCalls.push({ url, headers: options.headers ?? {}, body: JSON.parse(options.body) });
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
          return { status: "ok", card: { type: "AdaptiveCard", body: [{ type: "TextBlock", text: "hola" }] } };
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
      async sendMessageToConversation(payload) {
        teams.conversations.lastMessage = payload;
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
  assert.equal(fetchCalls[0].body.text, "hello");
  assert.equal(fetchCalls[0].headers.Authorization, "Bearer u");
  assert.equal(state.detectedLanguage, "en");
  assert.equal(preview.value || preview.textContent, "hola");
  await applyButton.trigger("click");
  assert.equal(fetchCalls[1].url, "/api/reply");
  assert.equal(fetchCalls[1].body.replyText, "hola");
  assert.equal(fetchCalls[1].body.text, "hola");
  assert.equal(fetchCalls[1].body.sourceLanguage, "en");
  assert.equal(fetchCalls[1].body.sourceLanguage, state.detectedLanguage);
  assert.equal(fetchCalls[1].body.metadata.modelId, "model-a");
  assert.equal(fetchCalls[1].body.metadata.tone, "formal");
  assert.equal(fetchCalls[1].headers.Authorization, "Bearer u");
  assert.equal(state.tone, "formal");
  assert.deepEqual(teams.conversations.lastMessage.attachments[0].content, {
    type: "AdaptiveCard",
    body: [{ type: "TextBlock", text: "hola" }]
  });
  assert.equal(teams.conversations.lastMessage.type, "card");
  assert.equal(preview.value || preview.textContent, "已发送 Adaptive Card 回贴");
});

test("compose plugin sends additional target languages", async () => {
  const fetchCalls = [];
  const fakeFetch = async (url, options = {}) => {
    if (options.body) {
      fetchCalls.push({ url, headers: options.headers ?? {}, body: JSON.parse(options.body) });
    }
    return {
      ok: true,
      async json() {
        if (url === "/api/metadata") {
          return {
            models: [{ id: "model-a", displayName: "Model A", costPerCharUsd: 0.00002 }],
            languages: [
              { id: "auto", name: "Auto", isDefault: true },
              { id: "es", name: "Español" },
              { id: "fr", name: "Français" },
              { id: "ja", name: "日本語" }
            ],
            features: { terminologyToggle: true, toneToggle: true },
            pricing: { currency: "USD" }
          };
        }
        if (url === "/api/translate") {
          return { text: "hola", detectedLanguage: "en" };
        }
        if (url === "/api/reply") {
          return {
            status: "ok",
            card: {
              type: "AdaptiveCard",
              body: [
                { type: "TextBlock", text: "hola" },
                { type: "TextBlock", text: "bonjour" },
                { type: "TextBlock", text: "こんにちは" }
              ]
            }
          };
        }
        throw new Error(`unexpected url ${url}`);
      }
    };
  };

  const suggestButton = createStubElement();
  const applyButton = createStubElement();
  const input = createStubElement({ value: "hello" });
  const preview = createStubElement();
  const targetSelect = Object.assign(createStubElement({ value: "es" }), { replaceChildren() {} });
  const additionalTargetsSelect = createStubElement({ value: [] });

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
      async sendMessageToConversation(payload) {
        teams.conversations.lastMessage = payload;
      }
    }
  };

  await initComposePlugin({
    ui: { input, targetSelect, additionalTargetsSelect, suggestButton, applyButton, preview },
    teams,
    fetcher: fakeFetch
  });

  additionalTargetsSelect.value = ["fr", "ja"];
  await additionalTargetsSelect.trigger("change");
  await suggestButton.trigger("click");
  await applyButton.trigger("click");

  const translateCall = fetchCalls.find((call) => call.url === "/api/translate");
  const replyCall = fetchCalls.find((call) => call.url === "/api/reply");
  assert.ok(translateCall, "expected translate call");
  assert.ok(replyCall, "expected reply call");
  assert.deepEqual(translateCall.body.additionalTargetLanguages, ["fr", "ja"]);
  assert.deepEqual(replyCall.body.additionalTargetLanguages, ["fr", "ja"]);
  assert.equal(
    teams.conversations.lastMessage.attachments[0].content.body[1].text,
    "bonjour"
  );
  assert.equal(
    teams.conversations.lastMessage.attachments[0].content.body[2].text,
    "こんにちは"
  );
  assert.equal(preview.value || preview.textContent, "已发送 Adaptive Card 回贴");
});

test("compose plugin falls back to first non-auto target", async () => {
  const fetchCalls = [];
  const fakeFetch = async (url, options = {}) => {
    if (options.body) {
      fetchCalls.push({ url, headers: options.headers ?? {}, body: JSON.parse(options.body) });
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
  assert.equal(fetchCalls[0].body.targetLanguage, "fr");
});

test("compose plugin corrects target when locale missing from metadata", async () => {
  const fetchCalls = [];
  const fakeFetch = async (url, options = {}) => {
    if (options.body) {
      fetchCalls.push({ url, headers: options.headers ?? {}, body: JSON.parse(options.body) });
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
  assert.equal(fetchCalls[0].body.targetLanguage, "ja");
});

test("compose plugin reports reply failure", async () => {
  const fetchCalls = [];
  const fakeFetch = async (url, options = {}) => {
    if (options.body) {
      fetchCalls.push({ url, headers: options.headers ?? {}, body: JSON.parse(options.body) });
    }
    if (url === "/api/reply") {
      return {
        ok: false,
        async json() {
          return { error: "failed" };
        },
        async text() {
          return "reply failed";
        },
        status: 500
      };
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
        if (url === "/api/translate") {
          return { text: "hola", metadata: { modelId: "model-a" } };
        }
        return {};
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
      async initialize() {},
      async getContext() {
        return { tenant: { id: "t" }, user: { id: "u" }, channel: { id: "c" }, app: { locale: "en-US" } };
      }
    },
    conversations: {
      async sendMessageToConversation() {
        teams.conversations.sent = true;
      }
    }
  };

  await initComposePlugin({
    ui: { input, targetSelect, suggestButton, applyButton, preview },
    teams,
    fetcher: fakeFetch
  });

  await suggestButton.trigger("click");
  await applyButton.trigger("click");

  assert.equal(teams.conversations.sent, undefined);
  assert.match(preview.value || preview.textContent, /发送失败/);
  const replyCall = fetchCalls.find((call) => call.url === "/api/reply");
  assert.ok(replyCall);
});

test("compose plugin uses concrete target when user locale is unavailable", async () => {
  const fetchCalls = [];
  const fakeFetch = async (url, options = {}) => {
    if (options.body) {
      fetchCalls.push({ url, headers: options.headers ?? {}, body: JSON.parse(options.body) });
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
  assert.equal(translateCall.body.targetLanguage, "es");
});
