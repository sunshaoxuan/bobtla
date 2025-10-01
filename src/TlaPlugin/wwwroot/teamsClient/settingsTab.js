import { ensureTeamsContext } from "./teamsContext.js";
import { fetchMetadata } from "./api.js";
import { buildDialogState } from "./state.js";

function resolveSettingsUi(root = typeof document !== "undefined" ? document : undefined) {
  if (!root) {
    return {};
  }
  return {
    modelContainer: root.querySelector?.("[data-model-list]"),
    defaultLanguageSelect: root.querySelector?.("[data-default-language]"),
    terminologyToggle: root.querySelector?.("[data-terminology-toggle]"),
    toneSelect: root.querySelector?.("[data-default-tone]"),
    statusLabel: root.querySelector?.("[data-settings-status]"),
    saveButton: root.querySelector?.("[data-settings-save]")
  };
}

function renderModelList(container, models, state) {
  if (!container) {
    return;
  }
  if (typeof container.replaceChildren === "function" && typeof document !== "undefined") {
    const nodes = models.map((model) => {
      const label = document.createElement("label");
      label.className = "model-item";
      const checkbox = document.createElement("input");
      checkbox.type = "checkbox";
      checkbox.value = model.id;
      checkbox.checked = state.allowedModels.has(model.id);
      checkbox.addEventListener("change", (event) => {
        if (event.target.checked) {
          state.allowedModels.add(model.id);
        } else {
          state.allowedModels.delete(model.id);
        }
      });
      const span = document.createElement("span");
      span.textContent = `${model.displayName ?? model.id}（${model.costPerCharUsd} USD/char）`;
      label.append(checkbox, span);
      return label;
    });
    container.replaceChildren(...nodes);
  } else {
    container.items = models;
  }
}

function renderDefaultLanguage(select, languages, state) {
  if (!select) {
    return;
  }
  const candidates = languages.filter((lang) => lang.id !== "auto");
  const candidateIds = candidates.map((lang) => lang.id);
  const fallbackTarget = candidateIds[0] ?? "";
  if (!candidateIds.includes(state.targetLanguage)) {
    state.targetLanguage = fallbackTarget;
  }
  if (typeof select.replaceChildren === "function" && typeof document !== "undefined") {
    const options = candidates.map((lang) => {
      const option = document.createElement("option");
      option.value = lang.id;
      option.textContent = lang.name;
      return option;
    });
    select.replaceChildren(...options);
  } else {
    select.optionsData = candidates;
  }
  if (!candidateIds.includes(select.value)) {
    select.value = fallbackTarget;
  }
  select.value = state.targetLanguage;
  select.addEventListener?.("change", (event) => {
    state.targetLanguage = event.target.value;
  });
}

function renderToneOptions(select, state) {
  if (!select) {
    return;
  }
  const options = [
    { value: "neutral", label: "默认语气" },
    { value: "formal", label: "正式语气" }
  ];
  if (typeof select.replaceChildren === "function" && typeof document !== "undefined") {
    const nodes = options.map((option) => {
      const node = document.createElement("option");
      node.value = option.value;
      node.textContent = option.label;
      return node;
    });
    select.replaceChildren(...nodes);
  } else {
    select.optionsData = options;
  }
  select.value = state.tone;
  select.addEventListener?.("change", (event) => {
    state.tone = event.target.value;
  });
}

export function buildTenantConfig(state, context) {
  return {
    tenantId: context?.tenant?.id,
    defaultTargetLanguage: state.targetLanguage,
    allowedModels: Array.from(state.allowedModels),
    features: {
      terminology: Boolean(state.useTerminology),
      tone: state.tone
    }
  };
}

export async function initSettingsTab({ ui = resolveSettingsUi(), teams, fetcher } = {}) {
  const { teams: sdk, context } = await ensureTeamsContext({ teams });
  const metadata = await fetchMetadata(fetcher);
  const baseState = buildDialogState({ models: metadata.models, languages: metadata.languages, context });
  const state = {
    targetLanguage: baseState.targetLanguage,
    allowedModels: new Set(metadata.models.map((model) => model.id)),
    useTerminology: true,
    tone: baseState.tone
  };

  renderModelList(ui.modelContainer, metadata.models, state);
  renderDefaultLanguage(ui.defaultLanguageSelect, metadata.languages, state);
  renderToneOptions(ui.toneSelect, state);

  if (ui.terminologyToggle) {
    ui.terminologyToggle.checked = state.useTerminology;
    ui.terminologyToggle.addEventListener?.("change", (event) => {
      state.useTerminology = Boolean(event.target.checked);
    });
  }

  sdk.pages?.config?.setValidityState?.(true);
  let saveHandler;
  sdk.pages?.config?.registerOnSaveHandler?.((event) => {
    saveHandler = event;
  });

  async function persistSettings() {
    const config = buildTenantConfig(state, context);
    const currentUrl = typeof window !== "undefined" ? window.location?.href : "";
    await sdk.pages?.config?.setConfig?.({
      entityId: "bobtla-settings",
      contentUrl: config.contentUrl ?? currentUrl ?? "",
      suggestedDisplayName: "BOBTLA 设置",
      state: JSON.stringify(config)
    });
    saveHandler?.notifySuccess?.();
    if (ui.statusLabel) {
      ui.statusLabel.textContent = "已保存";
    }
  }

  ui.saveButton?.addEventListener?.("click", () => persistSettings());

  return { state, metadata, context };
}

if (typeof document !== "undefined") {
  document.addEventListener("DOMContentLoaded", () => {
    const settingsRoot = document.querySelector?.("[data-settings-root]");
    if (settingsRoot) {
      initSettingsTab().catch((error) => console.error("初始化设置页失败", error));
    }
  });
}

export default {
  initSettingsTab,
  buildTenantConfig
};
