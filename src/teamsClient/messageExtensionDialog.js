import { ensureTeamsContext } from "./teamsContext.js";
import { fetchMetadata, detectLanguage, translateText, rewriteTranslation, sendReply } from "./api.js";
import {
  buildDialogState,
  calculateCostHint,
  buildTranslatePayload,
  buildDetectPayload,
  buildRewritePayload,
  buildReplyPayload,
  updateStateWithResponse
} from "./state.js";

function resolveDialogUi(root = typeof document !== "undefined" ? document : undefined) {
  if (!root) {
    return {};
  }
  return {
    modelSelect: root.querySelector?.("[data-model-select]"),
    sourceSelect: root.querySelector?.("[data-source-select]"),
    targetSelect: root.querySelector?.("[data-target-select]"),
    terminologyToggle: root.querySelector?.("[data-terminology-toggle]"),
    toneToggle: root.querySelector?.("[data-tone-toggle]"),
    costHint: root.querySelector?.("[data-cost-hint]"),
    detectedLabel: root.querySelector?.("[data-detected-language]"),
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

function updateDetectedLanguageLabel(ui, state, languages) {
  if (!ui.detectedLabel) {
    return;
  }
  if (!state.detectedLanguage) {
    ui.detectedLabel.textContent = "自动检测：--";
    ui.detectedLabel.hidden = false;
    return;
  }
  const matched = languages.find((lang) => lang.id === state.detectedLanguage);
  const display = matched?.name ?? state.detectedLanguage;
  const confidenceValue = state.detectionConfidence ?? 0;
  const confidence = confidenceValue ? `（置信度 ${(confidenceValue * 100).toFixed(0)}%）` : "";
  ui.detectedLabel.textContent = `自动检测：${display}${confidence}`;
  ui.detectedLabel.hidden = false;
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
  const fallbackTarget = availableTargetIds[0] ?? "";
  if (!availableTargetIds.includes(state.targetLanguage)) {
    state.targetLanguage = fallbackTarget;
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
      if (state.sourceLanguage !== "auto") {
        state.detectedLanguage = undefined;
        state.detectionConfidence = 0;
      }
      updateDetectedLanguageLabel(ui, state, metadata.languages);
    });
  }
  if (ui.targetSelect) {
    if (!availableTargetIds.includes(ui.targetSelect.value)) {
      ui.targetSelect.value = fallbackTarget;
    }
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
  if (ui.toneToggle) {
    ui.toneToggle.checked = state.tone === "formal";
    ui.toneToggle.addEventListener?.("change", (event) => {
      state.tone = event.target.checked ? "formal" : "neutral";
    });
  }

  bindCostHint(ui, state, metadata);
  updateDetectedLanguageLabel(ui, state, metadata.languages);

  let detectToken = 0;
  async function triggerDetection() {
    if (!state.text.trim() || state.sourceLanguage !== "auto") {
      return;
    }
    const currentToken = ++detectToken;
    try {
      const detectPayload = buildDetectPayload(state, context);
      const detection = await detectLanguage(detectPayload, fetcher);
      if (currentToken !== detectToken) {
        return;
      }
      state.detectedLanguage = detection.language;
      state.detectionConfidence = detection.confidence ?? 0;
      updateDetectedLanguageLabel(ui, state, metadata.languages);
    } catch (error) {
      if (currentToken === detectToken) {
        updateDetectedLanguageLabel(ui, { detectedLanguage: undefined, detectionConfidence: 0 }, metadata.languages);
        console.warn("语言检测失败", error);
      }
    }
  }

  if (ui.input) {
    ui.input.addEventListener?.("input", (event) => {
      state.text = event.target.value ?? "";
      state.charCount = state.text.length;
      bindCostHint(ui, state, metadata);
      triggerDetection();
    });
  }

  async function requestTranslation() {
    try {
      updateError(ui, "");
      const payload = buildTranslatePayload(state, context);
      const response = await translateText(payload, fetcher);
      const nextState = updateStateWithResponse(state, response);
      state.translation = nextState.translation;
      state.modelId = nextState.modelId;
      if (response.metadata?.tone) {
        state.tone = response.metadata.tone;
        if (ui.toneToggle) {
          ui.toneToggle.checked = state.tone === "formal";
        }
      }
      if (response.detectedLanguage) {
        state.detectedLanguage = response.detectedLanguage;
        updateDetectedLanguageLabel(ui, state, metadata.languages);
      }
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

  ui.previewButton?.addEventListener?.("click", () => requestTranslation());

  ui.submitButton?.addEventListener?.("click", async () => {
    if (!state.translation && state.text) {
      try {
        await requestTranslation();
      } catch (error) {
        return;
      }
    }
    const edited = ui.translation?.value ?? state.translation;
    let finalText = edited;
    if (edited?.trim()) {
      try {
        const rewritePayload = buildRewritePayload(state, context, edited);
        const rewriteResult = await rewriteTranslation(rewritePayload, fetcher);
        finalText = rewriteResult.text ?? edited;
        state.translation = finalText;
        if (rewriteResult.metadata?.tone) {
          state.tone = rewriteResult.metadata.tone;
          if (ui.toneToggle) {
            ui.toneToggle.checked = state.tone === "formal";
          }
        }
        if (ui.translation) {
          ui.translation.value = finalText;
        }
      } catch (error) {
        updateError(ui, error.message);
        return;
      }
    }
    if (finalText?.trim()) {
      try {
        const replyPayload = buildReplyPayload(state, context, finalText);
        const replyResult = await sendReply(replyPayload, fetcher);
        sdk.dialog?.submit?.({
          translation: finalText,
          targetLanguage: state.targetLanguage,
          modelId: state.modelId,
          useTerminology: state.useTerminology,
          tone: state.tone,
          detectedLanguage: state.detectedLanguage,
          card: replyResult?.card,
          replyStatus: replyResult?.status
        });
      } catch (error) {
        updateError(ui, error.message);
        return;
      }
      return;
    }
    sdk.dialog?.submit?.({
      translation: finalText,
      targetLanguage: state.targetLanguage,
      modelId: state.modelId,
      useTerminology: state.useTerminology,
      tone: state.tone,
      detectedLanguage: state.detectedLanguage
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
