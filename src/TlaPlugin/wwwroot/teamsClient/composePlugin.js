import { ensureTeamsContext } from "./teamsContext.js";
import { fetchMetadata, translateText, sendReply } from "./api.js";
import {
  buildDialogState,
  buildTranslatePayload,
  buildReplyPayload,
  calculateCostHint,
  updateStateWithResponse,
  resolveAdditionalTargetLanguages
} from "./state.js";

function resolveComposeUi(root = typeof document !== "undefined" ? document : undefined) {
  if (!root) {
    return {};
  }
  return {
    input: root.querySelector?.("[data-compose-input]"),
    targetSelect: root.querySelector?.("[data-compose-target]") ?? root.querySelector?.("[data-target-select]"),
    additionalTargetsSelect: root.querySelector?.("[data-compose-additional-targets]"),
    suggestButton: root.querySelector?.("[data-compose-suggest]"),
    applyButton: root.querySelector?.("[data-compose-apply]"),
    preview: root.querySelector?.("[data-compose-preview]"),
    terminologyToggle: root.querySelector?.("[data-terminology-toggle]"),
    toneToggle: root.querySelector?.("[data-tone-toggle]"),
    costHint: root.querySelector?.("[data-compose-cost]")
  };
}

function setPreview(previewElement, text) {
  if (!previewElement) {
    return;
  }
  if ("value" in previewElement) {
    previewElement.value = text;
    return;
  }
  previewElement.textContent = text;
}

function getMultiSelectValues(element) {
  if (!element) {
    return [];
  }
  if (typeof element.getSelectedValues === "function") {
    const values = element.getSelectedValues();
    return Array.isArray(values) ? values.filter((value) => typeof value === "string" && value.trim()) : [];
  }
  if (element.selectedOptions) {
    return Array.from(element.selectedOptions)
      .map((option) => option.value)
      .filter((value) => typeof value === "string" && value.trim());
  }
  if (Array.isArray(element.value)) {
    return element.value.filter((value) => typeof value === "string" && value.trim());
  }
  if (Array.isArray(element.optionsData)) {
    return element.optionsData
      .filter((option) => option?.selected)
      .map((option) => option.value)
      .filter((value) => typeof value === "string" && value.trim());
  }
  if (typeof element.value === "string" && element.value.trim()) {
    return [element.value.trim()];
  }
  return [];
}

function setMultiSelectValues(element, values = []) {
  if (!element) {
    return;
  }
  const normalized = Array.isArray(values)
    ? values.filter((value) => typeof value === "string" && value.trim())
    : [];
  if (typeof element.setSelectedValues === "function") {
    element.setSelectedValues(normalized);
    return;
  }
  const set = new Set(normalized);
  if (element.options) {
    Array.from(element.options).forEach((option) => {
      option.selected = set.has(option.value);
    });
  }
  if (Array.isArray(element.optionsData)) {
    element.optionsData = element.optionsData.map((option) => ({
      ...option,
      selected: set.has(option.value)
    }));
  }
  element.value = normalized;
}

export async function initComposePlugin({ ui = resolveComposeUi(), teams, fetcher } = {}) {
  const { teams: sdk, context } = await ensureTeamsContext({ teams });
  const metadata = await fetchMetadata(fetcher);
  const state = buildDialogState({ models: metadata.models, languages: metadata.languages, context });
  state.text = "";
  state.translation = "";

  let cachedAuthorization;
  async function resolveAuthorization() {
    if (cachedAuthorization !== undefined) {
      return cachedAuthorization;
    }
    if (sdk?.authentication?.getAuthToken) {
      try {
        const token = await sdk.authentication.getAuthToken();
        if (token) {
          cachedAuthorization = `Bearer ${token}`;
          return cachedAuthorization;
        }
      } catch (error) {
        console.warn("获取 Teams OAuth 令牌失败", error);
      }
    }
    const fallback = context?.user?.id ?? context?.user?.aadObjectId;
    cachedAuthorization = fallback ? `Bearer ${fallback}` : undefined;
    return cachedAuthorization;
  }

  const targetLanguages = metadata.languages.filter((lang) => lang.id !== "auto");

  if (ui.targetSelect && typeof ui.targetSelect.replaceChildren === "function" && typeof document !== "undefined") {
    const nodes = targetLanguages.map((lang) => {
      const option = document.createElement("option");
      option.value = lang.id;
      option.textContent = lang.name;
      return option;
    });
    ui.targetSelect.replaceChildren(...nodes);
  } else if (ui.targetSelect) {
    ui.targetSelect.optionsData = targetLanguages;
  }

  const availableTargetIds = targetLanguages.map((lang) => lang.id);
  state.availableTargetLanguages = availableTargetIds;
  const fallbackTarget = availableTargetIds[0] ?? "";
  if (!availableTargetIds.includes(state.targetLanguage)) {
    state.targetLanguage = fallbackTarget;
  }

  if (ui.additionalTargetsSelect) {
    if (
      typeof ui.additionalTargetsSelect.replaceChildren === "function" &&
      typeof document !== "undefined"
    ) {
      const nodes = targetLanguages.map((lang) => {
        const option = document.createElement("option");
        option.value = lang.id;
        option.textContent = lang.name;
        return option;
      });
      ui.additionalTargetsSelect.replaceChildren(...nodes);
    } else {
      ui.additionalTargetsSelect.optionsData = targetLanguages.map((lang) => ({
        value: lang.id,
        label: lang.name,
        selected: false
      }));
    }
  }

  const applyAdditionalTargets = (values) => {
    state.additionalTargetLanguages = values;
    if (ui.additionalTargetsSelect) {
      setMultiSelectValues(ui.additionalTargetsSelect, values);
    }
    return values;
  };

  const syncAdditionalTargets = () => {
    const selected = ui.additionalTargetsSelect
      ? getMultiSelectValues(ui.additionalTargetsSelect)
      : state.additionalTargetLanguages;
    const allowed = Array.isArray(state.availableTargetLanguages)
      ? state.availableTargetLanguages
      : availableTargetIds;
    const resolved = resolveAdditionalTargetLanguages(selected, state.targetLanguage, allowed);
    return applyAdditionalTargets(resolved);
  };

  applyAdditionalTargets(state.additionalTargetLanguages);

  if (ui.targetSelect) {
    if (!availableTargetIds.includes(ui.targetSelect.value)) {
      ui.targetSelect.value = fallbackTarget;
    }
    ui.targetSelect.value = state.targetLanguage;
    ui.targetSelect.addEventListener?.("change", (event) => {
      state.targetLanguage = event.target.value;
      syncAdditionalTargets();
    });
  }

  ui.additionalTargetsSelect?.addEventListener?.("change", () => {
    syncAdditionalTargets();
  });

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

  const updateCost = () => {
    if (ui.costHint && ui.input) {
      state.text = ui.input.value ?? "";
      state.charCount = state.text.length;
      ui.costHint.textContent = calculateCostHint(state, metadata.models, metadata.pricing);
    }
  };

  ui.input?.addEventListener?.("input", updateCost);
  updateCost();

  async function requestSuggestion() {
    if (!ui.input) {
      return;
    }
    state.text = ui.input.value ?? "";
    if (!state.text.trim()) {
      setPreview(ui.preview, "请输入要翻译的文本");
      return;
    }
    const additionalTargets = syncAdditionalTargets();
    const payload = buildTranslatePayload(
      { ...state, additionalTargetLanguages: additionalTargets },
      context
    );
    try {
      const authorization = await resolveAuthorization();
      const response = await translateText(payload, fetcher, { authorization });
      const nextState = updateStateWithResponse(state, response);
      Object.assign(state, nextState);
      if (ui.toneToggle) {
        ui.toneToggle.checked = state.tone === "formal";
      }
      setPreview(ui.preview, state.translation ?? "");
    } catch (error) {
      setPreview(ui.preview, `翻译失败：${error.message}`);
    }
  }

  ui.suggestButton?.addEventListener?.("click", () => requestSuggestion());

  ui.applyButton?.addEventListener?.("click", async () => {
    if (!state.translation) {
      await requestSuggestion();
    }
    const finalText = state.translation;
    if (!finalText) {
      return;
    }
    const additionalTargets = syncAdditionalTargets();
    const replyPayload = buildReplyPayload(
      { ...state, additionalTargetLanguages: additionalTargets },
      context,
      finalText
    );
    let replyResult;
    try {
      const authorization = await resolveAuthorization();
      replyResult = await sendReply(replyPayload, fetcher, { authorization });
    } catch (error) {
      setPreview(ui.preview, `发送失败：${error.message}`);
      return;
    }
    if (sdk.conversations?.sendMessageToConversation) {
      const message = {
        conversationId: context?.channel?.id
      };
      if (replyResult?.card) {
        message.attachments = [
          {
            contentType: "application/vnd.microsoft.card.adaptive",
            content: replyResult.card
          }
        ];
        message.type = "card";
      } else {
        message.content = finalText;
        message.type = "text";
      }
      await sdk.conversations.sendMessageToConversation(message);
    } else if (ui.input) {
      ui.input.value = finalText;
    }
    setPreview(ui.preview, replyResult?.card ? "已发送 Adaptive Card 回贴" : finalText);
  });

  return { state, metadata, context };
}

if (typeof document !== "undefined") {
  document.addEventListener("DOMContentLoaded", () => {
    const composeRoot = document.querySelector?.("[data-compose-root]");
    if (composeRoot) {
      initComposePlugin().catch((error) => console.error("初始化 Compose 插件失败", error));
    }
  });
}

export default {
  initComposePlugin
};
