const DEFAULT_LOCALE = "ja-JP";
const FALLBACK_STRINGS = {
  "tla.toast.dashboard.status": "プロジェクト状況を読み込めません。ダッシュボードはキャッシュデータを表示しています。",
  "tla.toast.dashboard.roadmap": "ロードマップを読み込めません。組み込みテンプレートを表示しています。",
  "tla.toast.dashboard.locales": "利用可能な言語を取得できません。既定値を使用します。",
  "tla.toast.dashboard.configuration": "構成を取得できません。言語一覧は既定値に基づきます。",
  "tla.toast.dashboard.metrics": "メトリックを取得できません。最後のキャッシュを表示しています。",
  "tla.toast.fetchGeneric": "リクエスト {0} に失敗しました。後でもう一度お試しください。",
  "tla.toast.settings.glossaryFetch": "用語集を読み込めません。後でもう一度お試しください。",
  "tla.toast.settings.configuration": "テナント ポリシーを読み込めません。既定値を使用します。",
  "tla.settings.glossary.empty": "登録済みの用語はありません。",
  "tla.settings.glossary.noConflicts": "競合は検出されませんでした。",
  "tla.settings.policies.noBannedTerms": "禁止語は設定されていません。",
  "tla.settings.policies.noStyleTemplates": "スタイル テンプレートはありません。",
  "tla.settings.upload.selectFile": "先に CSV または TermBase ファイルを選択してください。",
  "tla.settings.upload.progress.uploading": "アップロード中…",
  "tla.settings.upload.progress.parsing": "ファイルを解析しています…",
  "tla.settings.upload.progress.complete": "アップロードが完了しました",
  "tla.settings.upload.summary": "{0} 件を取り込み、{1} 件を更新しました。",
  "tla.settings.upload.error.noFile": "用語ファイルを先に選択してください。",
  "tla.settings.upload.error.http": "アップロードに失敗しました: {0}"
};

let activeCatalog = {
  locale: DEFAULT_LOCALE,
  defaultLocale: DEFAULT_LOCALE,
  displayName: "日本語 (日本)",
  strings: { ...FALLBACK_STRINGS }
};

const DEFAULT_FETCH = typeof fetch === "function" ? fetch.bind(globalThis) : undefined;

function resolvePreferredLocale(preferredLocale) {
  if (typeof preferredLocale === "string" && preferredLocale.trim() !== "") {
    return preferredLocale;
  }
  if (typeof navigator !== "undefined") {
    if (Array.isArray(navigator.languages) && navigator.languages.length > 0) {
      return navigator.languages[0];
    }
    if (typeof navigator.language === "string") {
      return navigator.language;
    }
  }
  return DEFAULT_LOCALE;
}

export function getActiveCatalog() {
  return activeCatalog;
}

export function getString(key, fallback) {
  if (typeof key !== "string" || key.length === 0) {
    return typeof fallback === "string" ? fallback : "";
  }
  if (activeCatalog.strings && Object.prototype.hasOwnProperty.call(activeCatalog.strings, key)) {
    return activeCatalog.strings[key];
  }
  if (Object.prototype.hasOwnProperty.call(FALLBACK_STRINGS, key)) {
    return FALLBACK_STRINGS[key];
  }
  if (typeof fallback === "string") {
    return fallback;
  }
  return key;
}

export function formatString(template, ...values) {
  if (typeof template !== "string") {
    return String(template ?? "");
  }
  return values.reduce((acc, value, index) => {
    const token = new RegExp(`\\{${index}\\}`, "g");
    return acc.replace(token, value ?? "");
  }, template);
}

export async function initializeLocalization(preferredLocale, options = {}) {
  const fetchImpl = options.fetchImpl ?? DEFAULT_FETCH;
  if (typeof fetchImpl !== "function") {
    return activeCatalog;
  }

  const targetLocale = resolvePreferredLocale(preferredLocale);
  const encodedLocale = encodeURIComponent(targetLocale);
  const url = `/api/localization/catalog/${encodedLocale}`;

  try {
    const response = await fetchImpl(url, { headers: { Accept: "application/json" } });
    if (!response?.ok) {
      return activeCatalog;
    }
    const payload = await response.json();
    if (!payload || typeof payload !== "object" || !payload.strings) {
      return activeCatalog;
    }
    const strings = {
      ...FALLBACK_STRINGS,
      ...(typeof payload.strings === "object" ? payload.strings : {})
    };
    activeCatalog = {
      locale: typeof payload.locale === "string" ? payload.locale : targetLocale,
      defaultLocale: typeof payload.defaultLocale === "string" ? payload.defaultLocale : DEFAULT_LOCALE,
      displayName: typeof payload.displayName === "string" ? payload.displayName : targetLocale,
      strings
    };
  } catch (error) {
    if (typeof console !== "undefined" && typeof console.debug === "function") {
      console.debug("Failed to load localization catalog", error);
    }
  }

  return activeCatalog;
}

export default {
  initializeLocalization,
  getString,
  formatString,
  getActiveCatalog
};
