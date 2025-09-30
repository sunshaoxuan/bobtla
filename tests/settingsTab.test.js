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
      useTerminology: true
    },
    { tenant: { id: "tenant-1" } }
  );
  assert.deepEqual(config.allowedModels, ["model-a", "model-b"]);
  assert.equal(config.features.terminology, true);
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
  const statusLabel = createStubElement();
  const saveButton = createStubElement();

  await initSettingsTab({
    ui: { modelContainer, defaultLanguageSelect, terminologyToggle, statusLabel, saveButton },
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
          features: { terminologyToggle: true },
          pricing: { currency: "USD" }
        };
      }
    })
  });

  assert.equal(validitySet, true);
  await sdk.pages.config.setConfig({});
  await saveButton.trigger("click");
  assert.equal(typeof savedConfig.state, "string");
  assert.equal(statusLabel.textContent, "已保存");
  assert.equal(successNotified, true);
});
