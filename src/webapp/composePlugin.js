import { ensureTeamsContext } from "./teamsContext.js";
import { fetchMetadata, translateText } from "./apiClient.js";
import { buildDialogState, buildTranslatePayload } from "./dialogState.js";

function resolveComposeUi(root = typeof document !== "undefined" ? document : undefined) {
  if (!root) {
    return {};
  }
  return {
    input: root.querySelector?.("[data-compose-input]"),
    targetSelect: root.querySelector?.("[data-compose-target]") ?? root.querySelector?.("[data-target-select]"),
    suggestButton: root.querySelector?.("[data-compose-suggest]"),
    applyButton: root.querySelector?.("[data-compose-apply]"),
    preview: root.querySelector?.("[data-compose-preview]"),
    terminologyToggle: root.querySelector?.("[data-terminology-toggle]")
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

export async function initComposePlugin({ ui = resolveComposeUi(), teams, fetcher } = {}) {
  const { teams: sdk, context } = await ensureTeamsContext({ teams });
  const metadata = await fetchMetadata(fetcher);
  const state = buildDialogState({ models: metadata.models, languages: metadata.languages, context });
  state.text = "";
  state.translation = "";

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
  const fallbackTarget = availableTargetIds[0] ?? "";
  if (!availableTargetIds.includes(state.targetLanguage)) {
    state.targetLanguage = fallbackTarget;
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

  async function requestSuggestion() {
    if (!ui.input) {
      return;
    }
    state.text = ui.input.value ?? "";
    if (!state.text.trim()) {
      setPreview(ui.preview, "请输入要翻译的文本");
      return;
    }
    const payload = buildTranslatePayload({ ...state }, context);
    try {
      const response = await translateText(payload, fetcher);
      state.translation = response.text ?? "";
      setPreview(ui.preview, state.translation);
    } catch (error) {
      setPreview(ui.preview, `翻译失败：${error.message}`);
    }
  }

  ui.suggestButton?.addEventListener?.("click", () => {
    return requestSuggestion();
  });

  ui.applyButton?.addEventListener?.("click", async () => {
    if (!state.translation) {
      await requestSuggestion();
    }
    if (state.translation) {
      if (sdk.conversations?.sendMessageToConversation) {
        await sdk.conversations.sendMessageToConversation({
          conversationId: context?.channel?.id,
          content: state.translation,
          type: "text"
        });
      } else if (ui.input) {
        ui.input.value = state.translation;
      }
    }
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
