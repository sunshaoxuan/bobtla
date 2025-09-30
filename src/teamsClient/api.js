const FALLBACK_METADATA = {
  models: [
    { id: "azureOpenAI:gpt-4o", displayName: "Azure OpenAI GPT-4o", costPerCharUsd: 0.00002, latencyTargetMs: 1800 },
    { id: "anthropic:claude-3", displayName: "Anthropic Claude 3", costPerCharUsd: 0.000018, latencyTargetMs: 2200 },
    { id: "ollama:llama3", displayName: "Ollama Llama 3", costPerCharUsd: 0.000005, latencyTargetMs: 2500 }
  ],
  languages: [
    { id: "auto", name: "自动检测", isDefault: true },
    { id: "en", name: "English" },
    { id: "zh-Hans", name: "简体中文" },
    { id: "ja", name: "日本語" },
    { id: "fr", name: "Français" }
  ],
  features: {
    terminologyToggle: true,
    offlineDraft: true,
    toneToggle: true
  },
  pricing: {
    currency: "USD",
    dailyBudgetUsd: 20
  }
};

async function parseJsonResponse(response, errorMessage) {
  if (!response.ok) {
    const text = await response.text();
    throw new Error(`${errorMessage}: ${response.status} ${text}`);
  }
  return response.json();
}

export async function fetchMetadata(fetchImpl = fetch) {
  try {
    const response = await fetchImpl("/api/metadata", { method: "GET" });
    return await parseJsonResponse(response, "metadata request failed");
  } catch (error) {
    console.warn("使用本地消息扩展元数据", error.message);
    return FALLBACK_METADATA;
  }
}

export async function detectLanguage(payload, fetchImpl = fetch) {
  const response = await fetchImpl("/api/detect", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });
  return await parseJsonResponse(response, "语言检测失败");
}

export async function translateText(payload, fetchImpl = fetch) {
  const response = await fetchImpl("/api/translate", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });
  return await parseJsonResponse(response, "翻译接口失败");
}

export async function rewriteTranslation(payload, fetchImpl = fetch) {
  const response = await fetchImpl("/api/rewrite", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });
  return await parseJsonResponse(response, "译文润色失败");
}

export async function sendReply(payload, fetchImpl = fetch) {
  const response = await fetchImpl("/api/reply", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });
  return await parseJsonResponse(response, "发送回帖失败");
}

export async function saveOfflineDraft(payload, fetchImpl = fetch) {
  const response = await fetchImpl("/api/draft", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });
  return await parseJsonResponse(response, "保存离线草稿失败");
}

export { FALLBACK_METADATA };

export default {
  fetchMetadata,
  detectLanguage,
  translateText,
  rewriteTranslation,
  sendReply,
  saveOfflineDraft,
  FALLBACK_METADATA
};
