import { buildStatusCards, formatLocaleOptions } from "./viewModel.js";

const FALLBACK_STATUS = {
  currentStageId: "phase4",
  overallCompletionPercent: 60,
  nextSteps: [
    "完善设置页组件并补全前端校验",
    "串联状态/路标 API 与实时刷新机制",
    "安排跨团队联调与上线验收"
  ],
  stages: [
    { id: "phase1", name: "阶段 1：平台基线", completed: true },
    { id: "phase2", name: "阶段 2：安全与合规", completed: true },
    { id: "phase3", name: "阶段 3：性能与可观测", completed: true },
    { id: "phase4", name: "阶段 4：前端体验", completed: false },
    { id: "phase5", name: "阶段 5：上线准备", completed: false }
  ],
  frontend: {
    completionPercent: 55,
    dataPlaneReady: true,
    uiImplemented: true,
    integrationReady: false
  }
};

const FALLBACK_ROADMAP = {
  activeStageId: "phase4",
  stages: [
    {
      id: "phase1",
      name: "阶段 1：平台基线",
      objective: "完成需求吸收、Minimal API 与消息扩展骨架。",
      completed: true,
      deliverables: [
        "梳理 BOBTLA 需求说明书",
        "搭建 .NET Minimal API + SQLite 基础设施",
        "交付消息扩展与 Adaptive Card 主流程"
      ]
    },
    {
      id: "phase2",
      name: "阶段 2：安全与合规",
      objective: "上线合规网关、预算守卫与密钥/OBO 管理。",
      completed: true,
      deliverables: [
        "ComplianceGateway 区域与禁译策略",
        "BudgetGuard 成本追踪",
        "KeyVaultSecretResolver 与 TokenBroker 一体化"
      ]
    },
    {
      id: "phase3",
      name: "阶段 3：性能与可观测",
      objective: "优化缓存、速率与多模型互联并沉淀指标。",
      completed: true,
      deliverables: [
        "TranslationCache 去重",
        "TranslationThrottle 并发与速率限制",
        "UsageMetricsService 聚合监控"
      ]
    },
    {
      id: "phase4",
      name: "阶段 4：前端体验",
      objective: "构建 Teams 设置页与前端仪表盘，统一本地化。",
      completed: false,
      deliverables: [
        "LocalizationCatalogService 推送日文默认 UI",
        "/api/status 与 /api/roadmap 汇总阶段数据",
        "新增 src/webapp 仪表盘页面与视图模型"
      ]
    },
    {
      id: "phase5",
      name: "阶段 5：上线准备",
      objective: "串联真实模型、端到端联调并准备发布验收。",
      completed: false,
      deliverables: [
        "接入生产模型与监控",
        "前后端联调脚本",
        "发布清单与回滚预案"
      ]
    }
  ],
  tests: [
    { id: "compliance", name: "ComplianceGatewayTests", description: "验证禁译词与区域白名单逻辑", automated: true },
    { id: "router", name: "TranslationRouterTests", description: "覆盖多模型回退与多语广播", automated: true },
    { id: "dashboard", name: "dashboardViewModel.test.js", description: "验证仪表盘阶段聚合与进度计算", automated: true }
  ]
};

const FALLBACK_LOCALES = [
  { id: "ja-JP", displayName: "日本語", isDefault: true },
  { id: "zh-CN", displayName: "简体中文" }
];

const FALLBACK_LANGUAGES = [
  "ja-JP",
  "en-US",
  "en-GB",
  "en-AU",
  "en-CA",
  "en-IN",
  "en-SG",
  "zh-CN",
  "zh-TW",
  "zh-HK",
  "zh-SG",
  "ko-KR",
  "fr-FR",
  "fr-CA",
  "fr-BE",
  "fr-CH",
  "de-DE",
  "de-AT",
  "de-CH",
  "es-ES",
  "es-MX",
  "es-AR",
  "es-CL",
  "es-CO",
  "es-US",
  "pt-BR",
  "pt-PT",
  "pt-AO",
  "it-IT",
  "it-CH",
  "nl-NL",
  "nl-BE",
  "sv-SE",
  "da-DK",
  "fi-FI",
  "nb-NO",
  "nn-NO",
  "is-IS",
  "fo-FO",
  "pl-PL",
  "cs-CZ",
  "sk-SK",
  "hu-HU",
  "ro-RO",
  "bg-BG",
  "hr-HR",
  "sr-RS",
  "bs-BA",
  "sl-SI",
  "mk-MK",
  "el-GR",
  "tr-TR",
  "tk-TM",
  "az-AZ",
  "kk-KZ",
  "ky-KG",
  "tt-RU",
  "ba-RU",
  "tg-TJ",
  "uz-UZ",
  "mn-MN",
  "uk-UA",
  "ru-RU",
  "be-BY",
  "ka-GE",
  "hy-AM",
  "he-IL",
  "yi-001",
  "ar-SA",
  "ar-EG",
  "ar-AE",
  "fa-IR",
  "ur-PK",
  "ur-IN",
  "ps-AF",
  "ku-TR",
  "ckb-IQ",
  "ug-CN",
  "sd-PK",
  "dv-MV",
  "hi-IN",
  "bn-IN",
  "bn-BD",
  "mr-IN",
  "ne-NP",
  "sa-IN",
  "ta-IN",
  "ta-SG",
  "te-IN",
  "ml-IN",
  "kn-IN",
  "pa-IN",
  "gu-IN",
  "or-IN",
  "as-IN",
  "si-LK",
  "th-TH",
  "lo-LA",
  "km-KH",
  "my-MM",
  "bo-CN",
  "dz-BT",
  "vi-VN",
  "id-ID",
  "ms-MY",
  "ms-SG",
  "jv-ID",
  "su-ID",
  "fil-PH",
  "tl-PH",
  "ceb-PH",
  "ilo-PH",
  "war-PH",
  "pam-PH",
  "sm-WS",
  "fj-FJ",
  "mi-NZ",
  "mg-MG",
  "rw-RW",
  "lg-UG",
  "sw-KE",
  "sw-TZ",
  "yo-NG",
  "ha-NG",
  "ig-NG",
  "am-ET",
  "ti-ER",
  "so-SO",
  "om-ET",
  "rwk-TZ",
  "wo-SN",
  "ff-SN",
  "bm-ML",
  "kj-NA",
  "rn-BI",
  "sn-ZW",
  "st-ZA",
  "tn-ZA",
  "ts-ZA",
  "ve-ZA",
  "xh-ZA",
  "zu-ZA",
  "nso-ZA",
  "pap-CW",
  "crs-SC",
  "gl-ES",
  "ca-ES",
  "eu-ES",
  "oc-FR",
  "ast-ES",
  "sc-IT",
  "vec-IT",
  "rm-CH",
  "br-FR",
  "cy-GB",
  "ga-IE",
  "gd-GB",
  "mt-MT",
  "fy-NL",
  "af-ZA"
];

async function fetchJson(url) {
  try {
    const response = await fetch(url);
    if (!response.ok) {
      throw new Error(`请求 ${url} 失败: ${response.status}`);
    }
    return await response.json();
  } catch (error) {
    console.warn("获取数据失败，使用本地样例:", error.message);
    return null;
  }
}

function updateProgress(element, percent) {
  if (!element) return;
  const safePercent = Number.isFinite(percent) ? Math.max(0, Math.min(100, percent)) : 0;
  element.style.setProperty("--value", `${safePercent}%`);
  element.setAttribute("aria-valuenow", String(safePercent));
  const textNode = element.querySelector("span");
  if (textNode) {
    textNode.textContent = `${safePercent}%`;
  }
}

function renderSummary(cards) {
  const overall = document.querySelector("[data-overall-progress]");
  const overallText = document.querySelector("[data-overall-text]");
  updateProgress(overall, cards.overallPercent);
  if (overallText) {
    overallText.textContent = `整体完成度 ${cards.overallPercent}%`;
  }

  const frontend = document.querySelector("[data-frontend-progress]");
  const frontendText = document.querySelector("[data-frontend-text]");
  updateProgress(frontend, cards.frontend.completionPercent);
  if (frontendText) {
    frontendText.textContent = `前端完成度 ${cards.frontend.completionPercent}%`;
  }

  const readinessList = document.querySelector("[data-readiness]");
  if (readinessList) {
    readinessList.innerHTML = "";
    const items = [
      { label: "数据面", ready: cards.frontend.dataPlaneReady },
      { label: "界面实现", ready: cards.frontend.uiImplemented },
      { label: "联调", ready: cards.frontend.integrationReady }
    ];
    items.forEach((item) => {
      const li = document.createElement("li");
      li.className = item.ready ? "ready" : "pending";
      li.textContent = `${item.label}: ${item.ready ? "已就绪" : "待完成"}`;
      readinessList.appendChild(li);
    });
  }

  const nextSteps = document.querySelector("[data-next-steps]");
  if (nextSteps) {
    nextSteps.innerHTML = "";
    cards.nextSteps.forEach((step) => {
      const li = document.createElement("li");
      li.textContent = step;
      nextSteps.appendChild(li);
    });
  }

  const activeStageHeading = document.querySelector("[data-active-stage-title]");
  const activeStageBody = document.querySelector("[data-active-stage-body]");
  if (cards.activeStage && activeStageHeading && activeStageBody) {
    activeStageHeading.textContent = `${cards.activeStage.name}`;
    activeStageBody.textContent = cards.activeStage.objective;
  }
}

function renderTimeline(timeline) {
  const container = document.querySelector("[data-timeline]");
  if (!container) return;
  container.innerHTML = "";
  timeline.forEach((stage) => {
    const card = document.createElement("article");
    card.className = `timeline-card ${stage.completed ? "timeline-card--done" : stage.isActive ? "timeline-card--active" : ""}`;

    const header = document.createElement("header");
    header.innerHTML = `<span class="badge">${stage.order}</span><h3>${stage.name}</h3>`;
    card.appendChild(header);

    const objective = document.createElement("p");
    objective.className = "timeline-card__objective";
    objective.textContent = stage.objective;
    card.appendChild(objective);

    if (Array.isArray(stage.deliverables) && stage.deliverables.length > 0) {
      const list = document.createElement("ul");
      list.className = "timeline-card__list";
      stage.deliverables.forEach((item) => {
        const li = document.createElement("li");
        li.textContent = item;
        list.appendChild(li);
      });
      card.appendChild(list);
    }

    const progressBar = document.createElement("div");
    progressBar.className = "progress";
    progressBar.innerHTML = `<span>${stage.progress}%</span>`;
    updateProgress(progressBar, stage.progress);
    card.appendChild(progressBar);

    container.appendChild(card);
  });
}

function renderTests(tests) {
  const list = document.querySelector("[data-test-list]");
  if (!list) return;
  list.innerHTML = "";
  tests.forEach((test) => {
    const li = document.createElement("li");
    li.innerHTML = `<strong>${test.name}</strong><span>${test.description}</span>${test.automated ? "<span class=\"tag\">自动化</span>" : ""}`;
    list.appendChild(li);
  });
}

function renderLocales(locales) {
  const list = document.querySelector("[data-locale-list]");
  if (!list) return;
  list.innerHTML = "";
  locales.forEach((locale) => {
    const li = document.createElement("li");
    li.textContent = `${locale.name}${locale.isDefault ? "（默认）" : ""}`;
    list.appendChild(li);
  });
}

function renderLanguages(languages) {
  const list = document.querySelector("[data-language-list]");
  const count = document.querySelector("[data-language-count]");
  const items = Array.isArray(languages) ? languages.filter((code) => typeof code === "string") : [];
  if (count) {
    count.textContent = `当前支持 ${items.length} 种语言`;
  }
  if (!list) return;
  list.innerHTML = "";
  items.forEach((code) => {
    const entry = document.createElement("li");
    const [language, region] = String(code).split("-");
    const meta = region ? region.toUpperCase() : language.toUpperCase();
    entry.innerHTML = `<strong>${code}</strong><span>${meta}</span>`;
    list.appendChild(entry);
  });
}

function toggleHidden(element, shouldHide) {
  if (!element) return;
  element.hidden = shouldHide;
}

function setText(element, text) {
  if (!element) return;
  element.textContent = text;
}

function setHtml(element, html) {
  if (!element) return;
  element.innerHTML = html;
}

function normalizeConflict(conflict) {
  if (!conflict || typeof conflict !== "object") {
    return null;
  }
  const source = conflict.source ?? conflict.Source ?? "";
  const existingTarget = conflict.existingTarget ?? conflict.ExistingTarget ?? "";
  const incomingTarget = conflict.incomingTarget ?? conflict.IncomingTarget ?? "";
  const scope = conflict.scope ?? conflict.Scope ?? "未知作用域";
  return { source, existingTarget, incomingTarget, scope };
}

export function renderGlossaryEntries(listElement, entries) {
  if (!listElement) return;
  const safeEntries = Array.isArray(entries) ? entries : [];
  if (safeEntries.length === 0) {
    listElement.innerHTML = "<li>暂无术语条目。</li>";
    return;
  }
  const html = safeEntries
    .map((entry) => {
      const source = entry.source ?? entry.Source ?? "(未知源词)";
      const target = entry.target ?? entry.Target ?? "(无译文)";
      const scope = entry.scope ?? entry.Scope ?? "global";
      return `<li><strong>${source}</strong><span>${target}</span><span>${scope}</span></li>`;
    })
    .join("");
  listElement.innerHTML = html;
}

export function renderGlossaryUploadFeedback(elements, payload) {
  if (!elements) return;
  const result = payload ?? {};
  const imported = Number.isFinite(result.imported) ? result.imported : 0;
  const updated = Number.isFinite(result.updated) ? result.updated : 0;
  const conflicts = Array.isArray(result.conflicts) ? result.conflicts.map(normalizeConflict).filter(Boolean) : [];
  const errors = Array.isArray(result.errors) ? result.errors.filter((item) => typeof item === "string" && item.trim().length > 0) : [];

  toggleHidden(elements.resultsContainer, false);
  setText(elements.summaryLabel, `新增 ${imported} 条，更新 ${updated} 条。`);
  setText(elements.importedCount, String(imported));
  setText(elements.updatedCount, String(updated));
  setText(elements.conflictCount, String(conflicts.length));
  setText(elements.errorCount, String(errors.length));

  if (conflicts.length > 0) {
    const conflictHtml = conflicts
      .map((conflict) => `<li><strong>${conflict.source}</strong>：现有译文「${conflict.existingTarget}」，上传译文「${conflict.incomingTarget}」（${conflict.scope}）</li>`)
      .join("");
    setHtml(elements.conflictList, conflictHtml);
    toggleHidden(elements.conflictContainer, false);
  } else {
    setHtml(elements.conflictList, "<li>未检测到冲突。</li>");
    toggleHidden(elements.conflictContainer, true);
  }

  if (errors.length > 0) {
    const errorHtml = errors.map((error) => `<li>${error}</li>`).join("");
    setHtml(elements.errorList, errorHtml);
    toggleHidden(elements.errorContainer, false);
  } else {
    setHtml(elements.errorList, "<li>未报告错误。</li>");
    toggleHidden(elements.errorContainer, true);
  }
}

function showStatus(elements, message) {
  if (!elements || !elements.statusLabel) return;
  elements.statusLabel.textContent = message;
}

export async function refreshGlossaryList(elements, fetchImpl = fetch) {
  if (!elements?.glossaryList) {
    return;
  }
  try {
    const response = await fetchImpl("/api/glossary");
    if (!response?.ok) {
      throw new Error(`获取术语失败：${response?.status ?? "未知错误"}`);
    }
    const entries = await response.json();
    renderGlossaryEntries(elements.glossaryList, entries);
  } catch (error) {
    renderGlossaryEntries(elements.glossaryList, []);
    showStatus(elements, error.message ?? "术语列表加载失败。");
  }
}

function ensureUploadElements() {
  if (typeof document === "undefined") {
    return null;
  }
  const form = document.querySelector("[data-glossary-form]");
  if (!form) {
    return null;
  }
  return {
    form,
    scopeInput: form.querySelector("[data-glossary-scope]"),
    fileInput: form.querySelector("[data-glossary-file]"),
    overrideInput: form.querySelector("[data-glossary-override]"),
    submitButton: form.querySelector('[type="submit"]'),
    resultsContainer: document.querySelector("[data-glossary-results]") ?? null,
    summaryLabel: document.querySelector("[data-glossary-summary]") ?? null,
    importedCount: document.querySelector("[data-glossary-imported]") ?? null,
    updatedCount: document.querySelector("[data-glossary-updated]") ?? null,
    conflictCount: document.querySelector("[data-glossary-conflict-count]") ?? null,
    errorCount: document.querySelector("[data-glossary-error-count]") ?? null,
    conflictContainer: document.querySelector("[data-glossary-conflicts]") ?? null,
    conflictList: document.querySelector("[data-glossary-conflict-list]") ?? null,
    errorContainer: document.querySelector("[data-glossary-errors]") ?? null,
    errorList: document.querySelector("[data-glossary-error-list]") ?? null,
    statusLabel: document.querySelector("[data-glossary-status]") ?? null,
    glossaryList: document.querySelector("[data-glossary-items]") ?? null
  };
}

export async function handleGlossaryUpload({ elements, fetchImpl = fetch, formDataFactory } = {}) {
  if (!elements?.form) {
    return;
  }

  const file = elements.fileInput?.files?.[0];
  if (!file) {
    showStatus(elements, "请选择需要上传的术语文件。");
    return;
  }

  const scope = elements.scopeInput?.value ?? "tenant";
  const overwrite = elements.overrideInput?.checked ? "true" : "false";
  const submitButton = elements.submitButton;

  const formData = formDataFactory ? formDataFactory(elements.form) : new FormData();
  if (typeof formData.set === "function") {
    formData.set("scope", scope);
    formData.set("overwrite", overwrite);
    formData.set("file", file, file.name ?? "glossary.csv");
  } else if (typeof formData.append === "function") {
    formData.append("scope", scope);
    formData.append("overwrite", overwrite);
    formData.append("file", file, file.name ?? "glossary.csv");
  }

  if (submitButton) {
    submitButton.disabled = true;
  }

  try {
    const response = await fetchImpl("/api/glossary/upload", {
      method: "POST",
      body: formData
    });

    let payload = null;
    try {
      payload = await response.json();
    } catch (error) {
      payload = null;
    }

    if (!response.ok) {
      const messages = [];
      if (payload?.error && typeof payload.error === "string") {
        messages.push(payload.error);
      }
      if (Array.isArray(payload?.errors)) {
        messages.push(...payload.errors.filter((item) => typeof item === "string"));
      }
      if (messages.length === 0) {
        messages.push(`上传失败：${response.status}`);
      }
      renderGlossaryUploadFeedback(elements, {
        imported: payload?.imported ?? 0,
        updated: payload?.updated ?? 0,
        conflicts: payload?.conflicts ?? [],
        errors: messages
      });
      showStatus(elements, messages[0]);
      return;
    }

    renderGlossaryUploadFeedback(elements, payload ?? {});
    showStatus(elements, `已导入 ${payload?.imported ?? 0} 条，更新 ${payload?.updated ?? 0} 条。`);
    await refreshGlossaryList(elements, fetchImpl);
  } catch (error) {
    const message = error?.message ?? "上传术语失败。";
    renderGlossaryUploadFeedback(elements, {
      imported: 0,
      updated: 0,
      conflicts: [],
      errors: [message]
    });
    showStatus(elements, message);
  } finally {
    if (submitButton) {
      submitButton.disabled = false;
    }
  }
}

export async function initializeGlossaryUpload({ fetchImpl = fetch, formDataFactory } = {}) {
  const elements = ensureUploadElements();
  if (!elements) {
    return null;
  }

  elements.form.addEventListener("submit", async (event) => {
    event.preventDefault();
    await handleGlossaryUpload({ elements, fetchImpl, formDataFactory });
  });

  await refreshGlossaryList(elements, fetchImpl);
  return elements;
}

async function bootstrap() {
  const [status, roadmap, locales, configuration] = await Promise.all([
    fetchJson("/api/status"),
    fetchJson("/api/roadmap"),
    fetchJson("/api/localization/locales"),
    fetchJson("/api/configuration")
  ]);

  const mergedCards = buildStatusCards(status ?? FALLBACK_STATUS, roadmap ?? FALLBACK_ROADMAP);
  renderSummary(mergedCards);
  renderTimeline(mergedCards.timeline);
  renderTests(mergedCards.tests);
  renderLocales(formatLocaleOptions(locales ?? FALLBACK_LOCALES));
  renderLanguages(configuration?.supportedLanguages ?? FALLBACK_LANGUAGES);
  await initializeGlossaryUpload();
}

if (typeof document !== "undefined") {
  document.addEventListener("DOMContentLoaded", () => {
    bootstrap().catch((error) => {
      console.error("初始化仪表盘失败", error);
    });
  });
}
