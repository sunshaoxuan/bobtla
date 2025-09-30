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
    offlineDraft: true
  },
  pricing: {
    currency: "USD",
    dailyBudgetUsd: 20
  }
};

export async function fetchMetadata(fetchImpl = fetch) {
  try {
    const response = await fetchImpl("/api/metadata", { method: "GET" });
    if (!response.ok) {
      throw new Error(`metadata request failed: ${response.status}`);
    }
    return await response.json();
  } catch (error) {
    console.warn("使用本地消息扩展元数据", error.message);
    return FALLBACK_METADATA;
  }
}

export async function translateText(payload, fetchImpl = fetch) {
  const response = await fetchImpl("/api/translate", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });
  if (!response.ok) {
    const text = await response.text();
    throw new Error(`翻译接口失败: ${response.status} ${text}`);
  }
  return await response.json();
}

export async function saveOfflineDraft(payload, fetchImpl = fetch) {
  const response = await fetchImpl("/api/draft", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });
  if (!response.ok) {
    throw new Error(`保存离线草稿失败: ${response.status}`);
  }
  return await response.json();
}

export { FALLBACK_METADATA };

export default {
  fetchMetadata,
  translateText,
  saveOfflineDraft
};
