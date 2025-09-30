import { FALLBACK_METADATA } from "./api.js";

function resolveDefaultTargetLanguage(languages = [], locale = "") {
  if (!Array.isArray(languages) || languages.length === 0) {
    return "en";
  }
  const exactLocale = languages.find((lang) => lang.id === locale);
  if (exactLocale) {
    return exactLocale.id;
  }
  const normalized = locale?.split?.("-")?.[0];
  if (normalized) {
    const match = languages.find((lang) => lang.id === normalized);
    if (match) {
      return match.id;
    }
  }
  const defaultLocale = languages.find((lang) => lang.isDefault && lang.id !== "auto");
  if (defaultLocale) {
    return defaultLocale.id;
  }
  const firstNonAuto = languages.find((lang) => lang.id !== "auto");
  if (firstNonAuto) {
    return firstNonAuto.id;
  }
  return languages[0].id;
}

export function buildDialogState({ models, languages, context } = {}) {
  const metadata = {
    models: models?.length ? models : FALLBACK_METADATA.models,
    languages: languages?.length ? languages : FALLBACK_METADATA.languages
  };
  const defaultModel = metadata.models[0];
  const targetLanguage = resolveDefaultTargetLanguage(metadata.languages, context?.app?.locale ?? "");
  return {
    text: "",
    translation: "",
    modelId: defaultModel?.id ?? "",
    sourceLanguage: metadata.languages[0]?.id ?? "auto",
    targetLanguage,
    useTerminology: true,
    tone: "neutral",
    charCount: 0,
    detectedLanguage: undefined,
    detectionConfidence: 0
  };
}

export function calculateCostHint({ charCount, modelId }, models = [], pricing = {}) {
  const model = models.find((item) => item.id === modelId);
  if (!model) {
    return "";
  }
  const cost = Number(model.costPerCharUsd ?? 0) * (charCount ?? 0);
  const currency = pricing.currency ?? "USD";
  const formatted = cost ? cost.toFixed(6) : "0";
  return `预计成本：${currency} ${formatted}（${charCount ?? 0} 字符 @ ${model.id}）`;
}

export function buildTranslatePayload(state, context) {
  if (!state?.text?.trim()) {
    throw new Error("缺少要翻译的文本");
  }
  if (!state.targetLanguage) {
    throw new Error("缺少目标语言");
  }
  return {
    text: state.text,
    sourceLanguage: state.sourceLanguage === "auto" ? undefined : state.sourceLanguage,
    targetLanguage: state.targetLanguage,
    tenantId: context?.tenant?.id,
    userId: context?.user?.id,
    channelId: context?.channel?.id,
    metadata: {
      origin: "messageExtension",
      modelId: state.modelId,
      useTerminology: Boolean(state.useTerminology),
      tone: state.tone
    }
  };
}

export function buildDetectPayload(state, context) {
  return {
    text: state.text,
    tenantId: context?.tenant?.id,
    userId: context?.user?.id
  };
}

export function buildRewritePayload(state, context, text) {
  if (!text?.trim()) {
    throw new Error("缺少润色文本");
  }
  return {
    text,
    targetLanguage: state.targetLanguage,
    tone: state.tone,
    tenantId: context?.tenant?.id,
    userId: context?.user?.id,
    channelId: context?.channel?.id,
    metadata: {
      origin: "messageExtension",
      modelId: state.modelId,
      useTerminology: Boolean(state.useTerminology)
    }
  };
}

export function buildReplyPayload(state, context, text) {
  if (!text?.trim()) {
    throw new Error("缺少回帖文本");
  }
  return {
    translation: text,
    sourceLanguage: state.sourceLanguage === "auto" ? state.detectedLanguage : state.sourceLanguage,
    targetLanguage: state.targetLanguage,
    tenantId: context?.tenant?.id,
    userId: context?.user?.id,
    channelId: context?.channel?.id,
    metadata: {
      modelId: state.modelId,
      tone: state.tone,
      useTerminology: Boolean(state.useTerminology)
    }
  };
}

export function updateStateWithResponse(state, response) {
  if (!state || !response) {
    return state;
  }
  const next = { ...state };
  if (typeof response.text === "string") {
    next.translation = response.text;
  }
  if (response.metadata?.modelId) {
    next.modelId = response.metadata.modelId;
  }
  if (response.metadata?.tone) {
    next.tone = response.metadata.tone;
  }
  if (response.detectedLanguage) {
    next.detectedLanguage = response.detectedLanguage;
  }
  return next;
}

export default {
  buildDialogState,
  calculateCostHint,
  buildTranslatePayload,
  buildDetectPayload,
  buildRewritePayload,
  buildReplyPayload,
  updateStateWithResponse
};
