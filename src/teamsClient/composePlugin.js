import { ensureTeamsContext } from "./teamsContext.js";
import { fetchMetadata, translateText, sendReply } from "./api.js";
import {
  buildDialogState,
  buildTranslatePayload,
  buildReplyPayload,
  calculateCostHint,
  updateStateWithResponse
} from "./state.js";

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
    const payload = buildTranslatePayload({ ...state }, context);
    try {
      const response = await translateText(payload, fetcher);
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
    const replyPayload = buildReplyPayload(state, context, finalText);
    let replyResult;
    try {
      replyResult = await sendReply(replyPayload, fetcher);
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
