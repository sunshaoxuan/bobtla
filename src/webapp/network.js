import { showToast } from "./toast.js";
import { emitTelemetry } from "./telemetry.js";
import { getString, formatString } from "./localization.js";

const DEFAULT_FETCH = typeof fetch === "function" ? fetch.bind(globalThis) : undefined;

function now() {
  if (typeof performance !== "undefined" && typeof performance.now === "function") {
    return performance.now();
  }
  return Date.now();
}

function delay(ms) {
  return new Promise((resolve) => {
    setTimeout(resolve, ms);
  });
}

async function fetchWithTimeout(fetchImpl, url, init, timeoutMs) {
  if (!Number.isFinite(timeoutMs) || timeoutMs <= 0 || typeof AbortController !== "function") {
    return fetchImpl(url, init);
  }

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    return await fetchImpl(url, { ...init, signal: controller.signal });
  } finally {
    clearTimeout(timer);
  }
}

function normalizeResponseStatus(response) {
  if (!response || typeof response !== "object") {
    return {
      ok: false,
      status: 0
    };
  }
  const ok = typeof response.ok === "boolean" ? response.ok : (response.status ?? 0) >= 200 && (response.status ?? 0) < 300;
  const status = typeof response.status === "number" ? response.status : ok ? 200 : 500;
  return { ok, status };
}

export async function fetchJson(url, options = {}) {
  const {
    fetchImpl = DEFAULT_FETCH,
    retries = 2,
    retryDelayMs = 400,
    timeoutMs = 15000,
    toast = true,
    toastMessage,
    toastKey,
    method = "GET",
    headers,
    onFailure
  } = options;

  if (typeof fetchImpl !== "function") {
    throw new Error("fetch implementation is not available");
  }

  const attempts = Math.max(1, Math.floor(Number(retries)) + 1);
  let lastError;

  for (let attempt = 1; attempt <= attempts; attempt += 1) {
    const startedAt = now();
    try {
      const response = await fetchWithTimeout(fetchImpl, url, { method, headers }, timeoutMs);
      const duration = now() - startedAt;
      const { ok, status } = normalizeResponseStatus(response);
      emitTelemetry({
        type: "network",
        name: "fetchJson",
        url,
        status,
        ok,
        duration,
        attempt
      });

      if (!ok) {
        const error = new Error(`请求 ${url} 失败: ${status}`);
        error.status = status;
        error.response = response;
        throw error;
      }

      if (response && typeof response.json === "function") {
        return await response.json();
      }

      if (response && typeof response.text === "function") {
        const text = await response.text();
        try {
          return JSON.parse(text);
        } catch (parseError) {
          return text;
        }
      }

      return response;
    } catch (error) {
      lastError = error instanceof Error ? error : new Error(String(error));
      emitTelemetry({
        type: "network",
        name: "fetchJsonFailure",
        url,
        attempt,
        message: lastError.message
      });

      if (attempt < attempts) {
        await delay(retryDelayMs * Math.max(1, attempt));
        continue;
      }

      if (typeof console !== "undefined" && typeof console.warn === "function") {
        console.warn(`无法获取 ${url}: ${lastError.message}`);
      }

      if (toast) {
        let resolvedMessage = typeof toastMessage === "function"
          ? toastMessage({ url, error: lastError })
          : toastMessage;

        if (typeof resolvedMessage !== "string" || resolvedMessage.trim() === "") {
          const template = getString("tla.toast.fetchGeneric", `无法获取 {0}，请稍后重试。`);
          resolvedMessage = formatString(template, url);
        }

        showToast(resolvedMessage, "danger", { key: toastKey });
      }

      if (typeof onFailure === "function") {
        try {
          onFailure({ url, error: lastError });
        } catch (callbackError) {
          if (typeof console !== "undefined" && typeof console.error === "function") {
            console.error("fetchJson onFailure callback failed", callbackError);
          }
        }
      }

      return null;
    }
  }

  return null;
}
