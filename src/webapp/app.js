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
  "zh-CN",
  "zh-TW",
  "ko-KR",
  "fr-FR",
  "de-DE",
  "es-ES",
  "it-IT",
  "pt-BR",
  "pt-PT",
  "ru-RU",
  "nl-NL",
  "sv-SE",
  "da-DK",
  "fi-FI",
  "nb-NO",
  "pl-PL",
  "cs-CZ",
  "sk-SK",
  "hu-HU",
  "tr-TR",
  "ar-SA",
  "he-IL",
  "hi-IN",
  "th-TH",
  "vi-VN",
  "id-ID",
  "ms-MY",
  "uk-UA",
  "el-GR",
  "ro-RO",
  "bg-BG",
  "hr-HR",
  "sl-SI",
  "lt-LT",
  "lv-LV",
  "et-EE",
  "sr-RS",
  "ta-IN"
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
}

document.addEventListener("DOMContentLoaded", bootstrap);
