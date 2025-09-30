import test from "node:test";
import assert from "node:assert/strict";
import { initSettingsTab, buildTenantConfig } from "../src/webapp/settingsTab.js";

function createStubElement(initial = {}) {
  return {
    value: initial.value ?? "",
    checked: Boolean(initial.checked),
    textContent: initial.textContent ?? "",
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

test("buildTenantConfig serialises state", () => {
  const config = buildTenantConfig(
    {
      targetLanguage: "ja",
      allowedModels: new Set(["model-a", "model-b"]),
      useTerminology: true,
      tone: "formal"
    },
    { tenant: { id: "tenant-1" } }
  );
  assert.deepEqual(config.allowedModels, ["model-a", "model-b"]);
  assert.equal(config.features.terminology, true);
  assert.equal(config.features.tone, "formal");
  assert.equal(config.tenantId, "tenant-1");
});

test("initSettingsTab registers save handler", async () => {
  let savedConfig;
  let validitySet = false;
  let successNotified = false;
  const sdk = {
    app: {
      async initialize() {
        return undefined;
      },
      async getContext() {
        return { tenant: { id: "tenant" }, user: { id: "user" }, app: { locale: "en-US" } };
      }
    },
    pages: {
      config: {
        setValidityState(value) {
          validitySet = value;
        },
        registerOnSaveHandler(handler) {
          const event = {
            notifySuccess: () => {
              successNotified = true;
            }
          };
          handler(event);
        },
        async setConfig(config) {
          savedConfig = config;
        }
      }
    }
  };

  const modelContainer = createStubElement();
  const defaultLanguageSelect = createStubElement();
  const terminologyToggle = createStubElement({ checked: true });
  const toneSelect = createStubElement({ value: "formal" });
  const statusLabel = createStubElement();
  const saveButton = createStubElement();

  await initSettingsTab({
    ui: { modelContainer, defaultLanguageSelect, terminologyToggle, toneSelect, statusLabel, saveButton },
    teams: sdk,
    fetcher: async () => ({
      ok: true,
      async json() {
        return {
          models: [
            { id: "model-a", displayName: "Model A", costPerCharUsd: 0.0001 },
            { id: "model-b", displayName: "Model B", costPerCharUsd: 0.0002 }
          ],
          languages: [
            { id: "auto", name: "Auto", isDefault: true },
            { id: "ja", name: "日本語" }
          ],
          features: { terminologyToggle: true, toneToggle: true },
          pricing: { currency: "USD" }
        };
      }
    })
  });

  assert.equal(validitySet, true);
  await sdk.pages.config.setConfig({});
  toneSelect.value = "formal";
  await toneSelect.trigger("change");
  await saveButton.trigger("click");
  assert.equal(typeof savedConfig.state, "string");
  const parsed = JSON.parse(savedConfig.state);
  assert.equal(parsed.features.tone, "formal");
  assert.equal(statusLabel.textContent, "已保存");
  assert.equal(successNotified, true);
});

test("settings tab saves concrete target when user locale is unavailable", async () => {
  let savedConfig;
  const sdk = {
    app: {
      async initialize() {},
      async getContext() {
        return { tenant: { id: "tenant" }, user: { id: "user" }, app: {} };
      }
    },
    pages: {
      config: {
        setValidityState() {},
        registerOnSaveHandler(handler) {
          handler({ notifySuccess() {} });
        },
        async setConfig(config) {
          savedConfig = config;
        }
      }
    }
  };

  const modelContainer = createStubElement();
  const defaultLanguageSelect = Object.assign(createStubElement(), { replaceChildren() {} });
  const terminologyToggle = createStubElement({ checked: true });
  const toneSelect = createStubElement({ value: "neutral" });
  const statusLabel = createStubElement();
  const saveButton = createStubElement();

  await initSettingsTab({
    ui: { modelContainer, defaultLanguageSelect, terminologyToggle, toneSelect, statusLabel, saveButton },
    teams: sdk,
    fetcher: async () => ({
      ok: true,
      async json() {
        return {
          models: [{ id: "model-a", displayName: "Model A", costPerCharUsd: 0.0001 }],
          languages: [
            { id: "auto", name: "Auto", isDefault: true },
            { id: "es", name: "Español" }
          ],
          features: { terminologyToggle: true },
          pricing: { currency: "USD" }
        };
      }
    })
  });

  await saveButton.trigger("click");

  const parsed = JSON.parse(savedConfig.state);
  assert.equal(parsed.defaultTargetLanguage, "es");
});
