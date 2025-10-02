import { FALLBACK_METADATA } from "./api.js";

function isSelectableLanguage(language) {
  return Boolean(language?.id) && language.id !== "auto";
}

function resolveDefaultTargetLanguage(languages = [], locale = "") {
  if (!Array.isArray(languages) || languages.length === 0) {
    return "en";
  }
  const exactLocale = languages.find((lang) => lang.id === locale);
  if (isSelectableLanguage(exactLocale)) {
    return exactLocale.id;
  }
  const normalized = locale?.split?.("-")?.[0];
  if (normalized) {
    const match = languages.find((lang) => lang.id === normalized);
    if (isSelectableLanguage(match)) {
      return match.id;
    }
  }
  const defaultLocale = languages.find((lang) => lang.isDefault && isSelectableLanguage(lang));
  if (defaultLocale) {
    return defaultLocale.id;
  }
  const firstSelectable = languages.find((lang) => isSelectableLanguage(lang));
  if (firstSelectable) {
    return firstSelectable.id;
  }
  return languages[0]?.id ?? "en";
}

export function resolveThreadId(context) {
  return (
    context?.message?.threadId ??
    context?.message?.replyToId ??
    context?.message?.id ??
    context?.conversation?.id ??
    context?.chat?.id ??
    undefined
  );
}

export function buildDialogState({ models, languages, context } = {}) {
  const metadata = {
    models: models?.length ? models : FALLBACK_METADATA.models,
    languages: languages?.length ? languages : FALLBACK_METADATA.languages
  };
  const defaultModel = metadata.models[0];
  const targetLanguage = resolveDefaultTargetLanguage(metadata.languages, context?.app?.locale ?? "");
  const availableTargets = metadata.languages.filter((lang) => isSelectableLanguage(lang)).map((lang) => lang.id);
  return {
    text: "",
    translation: "",
    modelId: defaultModel?.id ?? "",
    sourceLanguage: metadata.languages[0]?.id ?? "auto",
    targetLanguage,
    additionalTargetLanguages: resolveAdditionalTargetLanguages(
      undefined,
      targetLanguage,
      availableTargets
    ),
    availableTargetLanguages: availableTargets,
    threadId: resolveThreadId(context),
    useTerminology: true,
    useRag: false,
    contextHints: [],
    tone: "neutral",
    charCount: 0,
    detectedLanguage: undefined,
    detectionConfidence: 0
  };
}

function normalizeContextHints(hints) {
  if (!hints) {
    return [];
  }
  const list = Array.isArray(hints) ? hints : [hints];
  return list
    .map((item) => (typeof item === "string" ? item.trim() : ""))
    .filter(Boolean);
}

function applyRagPreferences(payload, state) {
  if (!payload || !state) {
    return payload;
  }
  const contextHints = normalizeContextHints(state.contextHints);
  payload.useRag = Boolean(state.useRag);
  payload.contextHints = contextHints;
  return payload;
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
  const additionalTargetLanguages = resolveAdditionalTargetLanguages(
    state.additionalTargetLanguages,
    state.targetLanguage,
    state.availableTargetLanguages
  );
  const payload = {
    text: state.text,
    sourceLanguage: state.sourceLanguage === "auto" ? undefined : state.sourceLanguage,
    targetLanguage: state.targetLanguage,
    additionalTargetLanguages,
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
  const threadId = state?.threadId ?? resolveThreadId(context);
  if (threadId) {
    payload.threadId = threadId;
  }
  return applyRagPreferences(payload, state);
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
  const payload = {
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
  const threadId = state?.threadId ?? resolveThreadId(context);
  if (threadId) {
    payload.threadId = threadId;
  }
  return payload;
}

export function buildReplyPayload(state, context, text) {
  if (!text?.trim()) {
    throw new Error("缺少回帖文本");
  }
  const additionalTargetLanguages = resolveAdditionalTargetLanguages(
    state.additionalTargetLanguages,
    state.targetLanguage,
    state.availableTargetLanguages
  );
  const payload = {
    replyText: text,
    text,
    sourceLanguage: state.sourceLanguage === "auto" ? state.detectedLanguage : state.sourceLanguage,
    targetLanguage: state.targetLanguage,
    additionalTargetLanguages,
    tenantId: context?.tenant?.id,
    userId: context?.user?.id,
    channelId: context?.channel?.id,
    metadata: {
      modelId: state.modelId,
      tone: state.tone,
      useTerminology: Boolean(state.useTerminology)
    }
  };
  const threadId = state?.threadId ?? resolveThreadId(context);
  if (threadId) {
    payload.threadId = threadId;
  }
  return applyRagPreferences(payload, state);
}

export function resolveAdditionalTargetLanguages(list, targetLanguage, available = []) {
  const source = Array.isArray(list) ? list : [];
  const normalizedTarget = typeof targetLanguage === "string" ? targetLanguage.trim() : "";
  const allowed = Array.isArray(available) && available.length > 0 ? new Set(available) : null;
  const seen = new Set();
  const result = [];
  for (const entry of source) {
    if (typeof entry !== "string") {
      continue;
    }
    const trimmed = entry.trim();
    if (!trimmed || trimmed === normalizedTarget) {
      continue;
    }
    if (allowed && !allowed.has(trimmed)) {
      continue;
    }
    if (seen.has(trimmed)) {
      continue;
    }
    seen.add(trimmed);
    result.push(trimmed);
  }
  return result;
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
  updateStateWithResponse,
  resolveThreadId
};
