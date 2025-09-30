import { describe, test, expect, beforeEach, jest } from "@jest/globals";
import { initMessageExtensionDialog } from "../src/teamsClient/messageExtensionDialog.js";

function createDialogDom() {
  document.body.innerHTML = `
    <main data-dialog-root>
      <select data-model-select></select>
      <select data-source-select></select>
      <p data-detected-language></p>
      <select data-target-select></select>
      <label><input type="checkbox" data-terminology-toggle /></label>
      <label><input type="checkbox" data-tone-toggle /></label>
      <label><input type="checkbox" data-rag-toggle /></label>
      <label><textarea data-context-hints></textarea></label>
      <p data-cost-hint></p>
      <textarea data-source-text></textarea>
      <textarea data-translation-text></textarea>
      <button data-preview-translation></button>
      <button data-submit-translation></button>
      <p data-error-banner></p>
      <section data-offline-draft-section hidden>
        <p data-offline-draft-status></p>
        <ul data-offline-draft-list></ul>
      </section>
      <button data-save-offline-draft hidden></button>
    </main>
  `;
}

describe("message extension dialog (jest)", () => {
  let teams;
  let fetchCalls;
  let fakeFetch;

  beforeEach(() => {
    createDialogDom();
    fetchCalls = [];
    fakeFetch = jest.fn(async (url, options = {}) => {
      const bodyText = options.body;
      if (bodyText) {
        fetchCalls.push({ url, body: JSON.parse(bodyText) });
      } else {
        fetchCalls.push({ url });
      }
      if (url === "/api/metadata") {
        return {
          ok: true,
          async json() {
            return {
              models: [{ id: "model-a", displayName: "Model A", costPerCharUsd: 0.00002 }],
              languages: [
                { id: "auto", name: "Auto", isDefault: true },
                { id: "es", name: "Español" },
                { id: "ja", name: "日本語" }
              ],
              features: { terminologyToggle: true, toneToggle: true },
              pricing: { currency: "USD" }
            };
          }
        };
      }
      if (url === "/api/detect") {
        return {
          ok: true,
          async json() {
            return { language: "en", confidence: 0.92 };
          }
        };
      }
      if (url === "/api/translate") {
        return {
          ok: true,
          async json() {
            return {
              text: fetchCalls.at(-1).body.targetLanguage === "ja" ? "こんにちは" : "hola",
              detectedLanguage: "en",
              metadata: { modelId: "model-a", tone: "formal" }
            };
          }
        };
      }
      if (url === "/api/rewrite") {
        return {
          ok: true,
          async json() {
            return { text: `【润色】${fetchCalls.at(-1).body.text}`, metadata: { tone: "formal" } };
          }
        };
      }
      if (url === "/api/reply") {
        return {
          ok: true,
          async json() {
            return { status: "ok", card: { type: "AdaptiveCard", body: [] } };
          }
        };
      }
      throw new Error(`Unexpected url ${url}`);
    });
    teams = {
      dialog: {
        submit: jest.fn((payload) => {
          teams.dialog.lastSubmit = payload;
        })
      },
      app: {
        initialize: jest.fn(async () => undefined),
        getContext: jest.fn(async () => ({
          tenant: { id: "tenant" },
          user: { id: "user" },
          channel: { id: "channel" },
          app: { locale: "en-US" }
        }))
      }
    };
  });

  async function flushPromises() {
    await new Promise((resolve) => setTimeout(resolve, 0));
  }

  test("allows switching languages and previews translation", async () => {
    const { state } = await initMessageExtensionDialog({ teams, fetcher: fakeFetch });
    const input = document.querySelector("[data-source-text]");
    const target = document.querySelector("[data-target-select]");
    const preview = document.querySelector("[data-preview-translation]");
    const translation = document.querySelector("[data-translation-text]");

    expect(state.targetLanguage).toBe("es");
    target.value = "ja";
    target.dispatchEvent(new Event("change"));
    input.value = "hello";
    input.dispatchEvent(new Event("input"));
    await flushPromises();
    preview.dispatchEvent(new Event("click"));
    await flushPromises();

    expect(fetchCalls.find((call) => call.url === "/api/detect")).toBeTruthy();
    const translateCall = fetchCalls.find((call) => call.url === "/api/translate" && call.body.text === "hello");
    expect(translateCall.body.targetLanguage).toBe("ja");
    expect(translation.value).toBe("こんにちは");
    const detectedLabel = document.querySelector("[data-detected-language]");
    expect(detectedLabel.textContent).toContain("en");
    expect(state.targetLanguage).toBe("ja");
  });

  test("submits edited translation with rewrite and reply", async () => {
    await initMessageExtensionDialog({ teams, fetcher: fakeFetch });
    const input = document.querySelector("[data-source-text]");
    const preview = document.querySelector("[data-preview-translation]");
    const translation = document.querySelector("[data-translation-text]");
    const submit = document.querySelector("[data-submit-translation]");

    input.value = "good morning";
    input.dispatchEvent(new Event("input"));
    await flushPromises();
    preview.dispatchEvent(new Event("click"));
    await flushPromises();

    translation.value = "hola";
    submit.dispatchEvent(new Event("click"));
    await flushPromises();

    const rewriteCall = fetchCalls.find((call) => call.url === "/api/rewrite");
    const replyCall = fetchCalls.find((call) => call.url === "/api/reply");
    expect(rewriteCall.body.text).toBe("hola");
    expect(replyCall.body.translation).toBe("【润色】hola");
    expect(teams.dialog.submit).toHaveBeenCalled();
    expect(teams.dialog.lastSubmit.translation).toBe("【润色】hola");
    expect(translation.value).toBe("【润色】hola");
  });

  test("enabling rag forwards context hints in translate payload", async () => {
    await initMessageExtensionDialog({ teams, fetcher: fakeFetch });
    const ragToggle = document.querySelector("[data-rag-toggle]");
    const hints = document.querySelector("[data-context-hints]");
    const input = document.querySelector("[data-source-text]");
    const preview = document.querySelector("[data-preview-translation]");

    ragToggle.checked = true;
    ragToggle.dispatchEvent(new Event("change"));
    hints.value = "budget meeting\ncontract";
    hints.dispatchEvent(new Event("input"));
    input.value = "hello";
    input.dispatchEvent(new Event("input"));
    await flushPromises();
    preview.dispatchEvent(new Event("click"));
    await flushPromises();

    const translateCall = fetchCalls.find((call) => call.url === "/api/translate");
    expect(translateCall.body.useRag).toBe(true);
    expect(translateCall.body.contextHints).toEqual(["budget meeting", "contract"]);
  });

  test("disabling rag keeps payload without context", async () => {
    await initMessageExtensionDialog({ teams, fetcher: fakeFetch });
    const ragToggle = document.querySelector("[data-rag-toggle]");
    const input = document.querySelector("[data-source-text]");
    const preview = document.querySelector("[data-preview-translation]");

    ragToggle.checked = false;
    ragToggle.dispatchEvent(new Event("change"));
    input.value = "hello";
    input.dispatchEvent(new Event("input"));
    await flushPromises();
    preview.dispatchEvent(new Event("click"));
    await flushPromises();

    const translateCall = fetchCalls.find((call) => call.url === "/api/translate");
    expect(translateCall.body.useRag).toBe(false);
    expect(Array.isArray(translateCall.body.contextHints)).toBe(true);
    expect(translateCall.body.contextHints).toHaveLength(0);
  });
});
