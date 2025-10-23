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
  const fallbackMessage = typeof errorMessage === "string" && errorMessage.trim()
    ? errorMessage.trim()
    : "请求失败";
  const rawText = await response.text();

  if (!response.ok) {
    let detail = "";
    let errorCode = "";
    if (rawText) {
      try {
        const parsed = JSON.parse(rawText);
        const errorPayload = parsed && typeof parsed === "object" ? parsed.error ?? parsed : null;
        const messageCandidate = [
          errorPayload?.message,
          errorPayload?.title,
          parsed?.message,
          parsed?.error_description
        ].find((value) => typeof value === "string" && value.trim());
        if (messageCandidate) {
          detail = messageCandidate.trim();
        }
        const codeCandidate = [errorPayload?.code, parsed?.code]
          .find((value) => typeof value === "string" && value.trim());
        if (codeCandidate) {
          errorCode = codeCandidate.trim();
        }
      } catch {
        detail = rawText.trim();
      }
    }

    let message = detail || `${fallbackMessage}: ${response.status}`;
    if (detail && errorCode && !detail.includes(errorCode)) {
      message = `${detail} (${errorCode})`;
    } else if (!detail && rawText.trim()) {
      message = `${fallbackMessage}: ${response.status} ${rawText.trim()}`;
    }

    throw new Error(message);
  }

  if (!rawText) {
    return null;
  }

  try {
    return JSON.parse(rawText);
  } catch {
    return rawText;
  }
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

export async function translateText(payload, fetchImpl = fetch, { authorization } = {}) {
  const headers = { "Content-Type": "application/json" };
  const headerValue = buildAuthorizationHeader(authorization);
  if (headerValue) {
    headers.Authorization = headerValue;
  }
  const response = await fetchImpl("/api/translate", {
    method: "POST",
    headers,
    body: JSON.stringify(payload)
  });
  return await parseJsonResponse(response, "翻译接口失败");
}

export async function rewriteTranslation(payload, fetchImpl = fetch, { authorization } = {}) {
  const headers = { "Content-Type": "application/json" };
  const headerValue = buildAuthorizationHeader(authorization);
  if (headerValue) {
    headers.Authorization = headerValue;
  }
  const response = await fetchImpl("/api/rewrite", {
    method: "POST",
    headers,
    body: JSON.stringify(payload)
  });
  return await parseJsonResponse(response, "译文润色失败");
}

export async function sendReply(payload, fetchImpl = fetch, { authorization } = {}) {
  const headers = { "Content-Type": "application/json" };
  const headerValue = buildAuthorizationHeader(authorization);
  if (headerValue) {
    headers.Authorization = headerValue;
  }
  const response = await fetchImpl("/api/reply", {
    method: "POST",
    headers,
    body: JSON.stringify(payload)
  });
  return await parseJsonResponse(response, "发送回帖失败");
}

function buildAuthorizationHeader(authorization) {
  if (!authorization) {
    return undefined;
  }
  if (authorization.startsWith("Bearer ")) {
    return authorization;
  }
  return `Bearer ${authorization}`;
}

export async function saveOfflineDraft(payload, fetchImpl = fetch, { authorization } = {}) {
  const headers = {
    "Content-Type": "application/json"
  };
  const headerValue = buildAuthorizationHeader(authorization);
  if (headerValue) {
    headers.Authorization = headerValue;
  }
  const response = await fetchImpl("/api/offline-draft", {
    method: "POST",
    headers,
    body: JSON.stringify(payload)
  });
  return await parseJsonResponse(response, "保存离线草稿失败");
}

export async function listOfflineDrafts({ userId }, fetchImpl = fetch, { authorization } = {}) {
  if (!userId) {
    throw new Error("缺少用户标识，无法获取离线草稿");
  }
  const headers = {};
  const headerValue = buildAuthorizationHeader(authorization);
  if (headerValue) {
    headers.Authorization = headerValue;
  }
  const url = `/api/offline-draft?userId=${encodeURIComponent(userId)}`;
  const response = await fetchImpl(url, {
    method: "GET",
    headers
  });
  return await parseJsonResponse(response, "获取离线草稿失败");
}

export { FALLBACK_METADATA };

export default {
  fetchMetadata,
  detectLanguage,
  translateText,
  rewriteTranslation,
  sendReply,
  saveOfflineDraft,
  listOfflineDrafts,
  FALLBACK_METADATA
};
