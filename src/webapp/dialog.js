import { ensureTeamsContext } from "./teamsContext.js";
import { fetchMetadata, translateText } from "./apiClient.js";
import { buildDialogState, calculateCostHint, buildTranslatePayload, updateStateWithResponse } from "./dialogState.js";

function resolveDialogUi(root = typeof document !== "undefined" ? document : undefined) {
  if (!root) {
    return {};
  }
  return {
    modelSelect: root.querySelector?.("[data-model-select]"),
    sourceSelect: root.querySelector?.("[data-source-select]"),
    targetSelect: root.querySelector?.("[data-target-select]"),
    terminologyToggle: root.querySelector?.("[data-terminology-toggle]"),
    costHint: root.querySelector?.("[data-cost-hint]"),
    input: root.querySelector?.("[data-source-text]"),
    translation: root.querySelector?.("[data-translation-text]"),
    previewButton: root.querySelector?.("[data-preview-translation]"),
    submitButton: root.querySelector?.("[data-submit-translation]"),
    errorBanner: root.querySelector?.("[data-error-banner]")
  };
}

function applySelectOptions(element, options, { valueKey, labelKey }) {
  if (!element || !Array.isArray(options)) {
    return;
  }
  const entries = options.map((item) => ({ value: item[valueKey], label: item[labelKey] ?? item[valueKey] }));
  if (typeof element.replaceChildren === "function" && typeof document !== "undefined" && document?.createElement) {
    const nodes = entries.map((entry) => {
      const option = document.createElement("option");
      option.value = entry.value;
      option.textContent = entry.label;
      return option;
    });
    element.replaceChildren(...nodes);
  } else {
    element.optionsData = entries;
  }
  if (entries.length && element.value === undefined) {
    element.value = entries[0].value;
  }
}

function bindCostHint(ui, state, metadata) {
  if (!ui.costHint) {
    return;
  }
  ui.costHint.textContent = calculateCostHint(state, metadata.models, metadata.pricing);
}

function updateError(ui, message) {
  if (!ui.errorBanner) {
    return;
  }
  if (!message) {
    ui.errorBanner.textContent = "";
    ui.errorBanner.hidden = true;
    return;
  }
  ui.errorBanner.hidden = false;
  ui.errorBanner.textContent = message;
}

export async function initMessageExtensionDialog({ ui = resolveDialogUi(), teams, fetcher } = {}) {
  const { teams: sdk, context } = await ensureTeamsContext({ teams });
  const metadata = await fetchMetadata(fetcher);
  const state = buildDialogState({ models: metadata.models, languages: metadata.languages, context });

  applySelectOptions(ui.modelSelect, metadata.models, { valueKey: "id", labelKey: "displayName" });
  applySelectOptions(ui.sourceSelect, metadata.languages, { valueKey: "id", labelKey: "name" });

  const targetLanguages = metadata.languages.filter((lang) => lang.id !== "auto");
  applySelectOptions(ui.targetSelect, targetLanguages, {
    valueKey: "id",
    labelKey: "name"
  });

  const availableTargetIds = targetLanguages.map((lang) => lang.id);
  if (!availableTargetIds.includes(state.targetLanguage) && availableTargetIds.length) {
    state.targetLanguage = availableTargetIds[0];
    if (ui.targetSelect) {
      ui.targetSelect.value = state.targetLanguage;
    }
  }

  if (ui.modelSelect) {
    ui.modelSelect.value = state.modelId;
    ui.modelSelect.addEventListener?.("change", (event) => {
      state.modelId = event.target.value;
      bindCostHint(ui, state, metadata);
    });
  }
  if (ui.sourceSelect) {
    ui.sourceSelect.value = state.sourceLanguage;
    ui.sourceSelect.addEventListener?.("change", (event) => {
      state.sourceLanguage = event.target.value;
    });
  }
  if (ui.targetSelect) {
    ui.targetSelect.value = state.targetLanguage;
    ui.targetSelect.addEventListener?.("change", (event) => {
      state.targetLanguage = event.target.value;
    });
  }
  if (ui.terminologyToggle) {
    ui.terminologyToggle.checked = state.useTerminology;
    ui.terminologyToggle.addEventListener?.("change", (event) => {
      state.useTerminology = Boolean(event.target.checked);
    });
  }

  if (ui.input) {
    ui.input.addEventListener?.("input", (event) => {
      state.text = event.target.value ?? "";
      state.charCount = state.text.length;
      bindCostHint(ui, state, metadata);
    });
  }

  bindCostHint(ui, state, metadata);

  async function requestTranslation() {
    try {
      updateError(ui, "");
      const payload = buildTranslatePayload(state, context);
      const response = await translateText(payload, fetcher);
      const nextState = updateStateWithResponse(state, response);
      state.translation = nextState.translation;
      state.modelId = nextState.modelId;
      if (ui.translation) {
        ui.translation.value = nextState.translation ?? "";
      }
      bindCostHint(ui, state, metadata);
      return nextState;
    } catch (error) {
      updateError(ui, error.message);
      throw error;
    }
  }

  ui.previewButton?.addEventListener?.("click", () => {
    return requestTranslation();
  });

  ui.submitButton?.addEventListener?.("click", async () => {
    if (!state.translation && state.text) {
      try {
        await requestTranslation();
      } catch (error) {
        return;
      }
    }
    sdk.dialog?.submit?.({
      translation: state.translation,
      targetLanguage: state.targetLanguage,
      modelId: state.modelId,
      useTerminology: state.useTerminology
    });
  });

  return { state, metadata, context };
}

if (typeof document !== "undefined") {
  document.addEventListener("DOMContentLoaded", () => {
    initMessageExtensionDialog().catch((error) => console.error("初始化消息扩展对话框失败", error));
  });
}

export default {
  initMessageExtensionDialog
};
