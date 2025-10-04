import { buildStatusCards, formatLocaleOptions } from "./viewModel.js";
export { handleGlossaryUpload, renderGlossaryUploadFeedback, renderGlossaryEntries } from "./settingsTab.js";

const FALLBACK_STATUS = {
  currentStageId: "phase5",
  overallCompletionPercent: 80,
  nextSteps: [
    "完成密钥映射 Runbook 并固化凭据分发",
    "安排 Graph/OBO 冒烟测试验证令牌链路",
    "切换至真实模型并执行发布 SmokeTest"
  ],
  stages: [
    { id: "phase1", name: "阶段 1：平台基线", completed: true },
    { id: "phase2", name: "阶段 2：安全与合规", completed: true },
    { id: "phase3", name: "阶段 3：性能与可观测", completed: true },
    { id: "phase4", name: "阶段 4：前端体验", completed: true },
    { id: "phase5", name: "阶段 5：上线准备", completed: false }
  ],
  stageFiveDiagnostics: {
    stageReady: false,
    hmacConfigured: false,
    hmacStatus: "仍启用了 HMAC 回退，需要切换到 AAD/OBO",
    graphScopesValid: false,
    graphScopesStatus: "Graph 作用域缺失或格式不正确",
    smokeTestRecent: false,
    smokeStatus: "尚未记录冒烟成功",
    lastSmokeSuccess: null,
    failureReason: "冒烟链路尚未通过"
  },
  frontend: {
    completionPercent: 80,
    dataPlaneReady: true,
    uiImplemented: true,
    integrationReady: false
  }
};

const FALLBACK_ROADMAP = {
  activeStageId: "phase5",
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
      completed: true,
      deliverables: [
        "Teams 设置 Tab 与仪表盘 UI 凝练交互",
        "前端消费 /api/status、/api/roadmap 实时刷新",
        "LocalizationCatalogService 发布多语言资源"
      ]
    },
    {
      id: "phase5",
      name: "阶段 5：上线准备",
      objective: "串联真实模型、端到端联调并准备发布验收。",
      completed: false,
      deliverables: [
        "接入生产模型与监控",
        "密钥映射与凭据分发 Runbook",
        "Graph/OBO 冒烟脚本与报表",
        "真实模型切换 SmokeTest 清单"
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

const FALLBACK_METRICS = {
  usage: { totalRequests: 15872, successRate: 0.982, window: "24h" },
  cost: { monthlyUsd: 312.45, dailyUsd: 12.68 },
  failures: [
    { reason: "RateLimitExceeded", count: 12 },
    { reason: "AuthenticationFailed", count: 5 },
    { reason: "ModelTimeout", count: 3 }
  ],
  tests: {
    failing: [
      {
        id: "translationRouter",
        name: "TranslationRouterTests",
        failures: 2,
        reason: "限流策略回退路径未命中"
      }
    ]
  },
  updatedAt: "2024-03-15T08:00:00Z"
};

const NORMALIZED_METRICS_TOKEN = Symbol("normalizedMetrics");
const METRICS_REFRESH_INTERVAL = 60_000;

const numberFormatter = new Intl.NumberFormat("zh-CN");
const currencyFormatter = new Intl.NumberFormat("zh-CN", {
  style: "currency",
  currency: "USD",
  maximumFractionDigits: 2
});
const percentFormatter = new Intl.NumberFormat("zh-CN", {
  style: "percent",
  maximumFractionDigits: 1
});
const updatedFormatter = new Intl.DateTimeFormat("zh-CN", {
  month: "2-digit",
  day: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
  hour12: false
});

let latestCards = null;
let latestMetrics = null;
let metricsTimer = null;
let metricsRefreshBound = false;

export function normalizeMetrics(metrics) {
  if (metrics && metrics[NORMALIZED_METRICS_TOKEN]) {
    return metrics;
  }

  const safe = metrics ?? {};
  const usageSource = safe.usage ?? {};
  const costSource = safe.cost ?? {};
  const testsSource = safe.tests ?? {};

  const totalRequestsRaw = Number(usageSource.totalRequests ?? usageSource.requests ?? usageSource.total ?? 0);
  const totalRequests = Number.isFinite(totalRequestsRaw) ? Math.max(0, Math.round(totalRequestsRaw)) : 0;

  const successRateRaw = Number(usageSource.successRate ?? usageSource.success_ratio ?? usageSource.successRatio ?? usageSource.successPercentage ?? usageSource.success_percent ?? NaN);
  let successRate = Number.isFinite(successRateRaw) ? successRateRaw : null;
  if (typeof successRate === "number") {
    successRate = successRate > 1 ? successRate / 100 : successRate;
    successRate = Math.max(0, Math.min(1, successRate));
  }

  const windowLabel = usageSource.window ?? usageSource.range ?? usageSource.period ?? null;

  const monthlyRaw = Number(costSource.monthlyUsd ?? costSource.monthToDateUsd ?? costSource.monthly ?? 0);
  const monthlyUsd = Number.isFinite(monthlyRaw) ? Math.max(0, monthlyRaw) : 0;
  const dailyRaw = Number(costSource.dailyUsd ?? costSource.dailyAverageUsd ?? costSource.daily ?? NaN);
  const dailyUsd = Number.isFinite(dailyRaw) ? Math.max(0, dailyRaw) : null;

  const failures = (Array.isArray(safe.failures) ? safe.failures : [])
    .filter((item) => item && typeof item === "object")
    .map((item, index) => {
      const countRaw = Number(item.count ?? item.total ?? item.times ?? 0);
      const count = Number.isFinite(countRaw) ? Math.max(0, Math.round(countRaw)) : 0;
      return {
        reason: item.reason ?? item.code ?? item.message ?? `未知原因 ${index + 1}`,
        count
      };
    })
    .sort((a, b) => b.count - a.count);

  const failing = Array.isArray(testsSource.failing) ? testsSource.failing : [];
  const normalizedFailing = failing
    .filter((item) => item && typeof item === "object" && item.id)
    .map((item) => {
      const failuresRaw = Number(item.failures ?? item.count ?? item.total ?? 1);
      const failures = Number.isFinite(failuresRaw) ? Math.max(1, Math.round(failuresRaw)) : 1;
      return {
        id: item.id,
        name: item.name ?? "",
        failures,
        reason: item.reason ?? item.lastFailureReason ?? ""
      };
    })
    .sort((a, b) => b.failures - a.failures);

  const normalized = {
    usage: {
      totalRequests,
      successRate,
      window: windowLabel
    },
    cost: {
      monthlyUsd,
      dailyUsd
    },
    failures,
    tests: {
      failing: normalizedFailing
    },
    updatedAt: safe.updatedAt ?? safe.timestamp ?? safe.generatedAt ?? null
  };

  normalized[NORMALIZED_METRICS_TOKEN] = true;
  return normalized;
}

latestMetrics = normalizeMetrics(FALLBACK_METRICS);

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

export function renderSummary(cards, metricsInput = latestMetrics) {
  const cardData = cards ?? {};
  const metrics = metricsInput ? normalizeMetrics(metricsInput) : latestMetrics;

  if (metrics && metrics !== latestMetrics) {
    latestMetrics = metrics;
  }

  const overallPercent = Number.isFinite(cardData.overallPercent) ? cardData.overallPercent : 0;
  const overall = document.querySelector("[data-overall-progress]");
  const overallText = document.querySelector("[data-overall-text]");
  updateProgress(overall, overallPercent);
  if (overallText) {
    overallText.textContent = `整体完成度 ${overallPercent}%`;
  }

  const frontendData = cardData.frontend ?? {};
  const frontendPercent = Number.isFinite(frontendData.completionPercent) ? frontendData.completionPercent : 0;
  const frontend = document.querySelector("[data-frontend-progress]");
  const frontendText = document.querySelector("[data-frontend-text]");
  updateProgress(frontend, frontendPercent);
  if (frontendText) {
    frontendText.textContent = `前端完成度 ${frontendPercent}%`;
  }

  const readinessList = document.querySelector("[data-readiness]");
  if (readinessList) {
    readinessList.innerHTML = "";
    const items = [
      { label: "数据面", ready: Boolean(frontendData.dataPlaneReady) },
      { label: "界面实现", ready: Boolean(frontendData.uiImplemented) },
      { label: "联调", ready: Boolean(frontendData.integrationReady) }
    ];
    items.forEach((item) => {
      const li = document.createElement("li");
      li.className = item.ready ? "ready" : "pending";
      li.textContent = `${item.label}: ${item.ready ? "已就绪" : "待完成"}`;
      readinessList.appendChild(li);
    });
  }

  const diagnosticsData = cardData.stageFiveDiagnostics ?? {};
  const diagnosticsList = document.querySelector("[data-stage-five-diagnostics]");
  if (diagnosticsList) {
    diagnosticsList.innerHTML = "";
    const diagItems = Array.isArray(diagnosticsData.items) ? diagnosticsData.items : [];
    if (diagItems.length === 0) {
      const empty = document.createElement("li");
      empty.className = "diagnostic diagnostic--empty";
      empty.textContent = "暂无诊断信息";
      diagnosticsList.appendChild(empty);
    } else {
      diagItems.forEach((item) => {
        const li = document.createElement("li");
        li.className = `diagnostic ${item.ready ? "diagnostic--ready" : "diagnostic--pending"}`;
        const label = document.createElement("span");
        label.className = "diagnostic__label";
        label.textContent = item.label ?? "";
        const message = document.createElement("span");
        message.className = "diagnostic__message";
        message.textContent = item.message ?? "";
        li.append(label, message);
        diagnosticsList.appendChild(li);
      });
    }
  }

  const diagnosticsFailure = document.querySelector("[data-stage-five-failure]");
  if (diagnosticsFailure) {
    const failureText = typeof diagnosticsData.failureReason === "string" ? diagnosticsData.failureReason : "";
    if (failureText) {
      diagnosticsFailure.textContent = `阻塞原因：${failureText}`;
      diagnosticsFailure.hidden = false;
    } else {
      diagnosticsFailure.textContent = "";
      diagnosticsFailure.hidden = true;
    }
  }

  const nextSteps = document.querySelector("[data-next-steps]");
  const steps = Array.isArray(cardData.nextSteps) ? cardData.nextSteps : [];
  if (nextSteps) {
    nextSteps.innerHTML = "";
    steps.forEach((step) => {
      const li = document.createElement("li");
      li.textContent = step;
      nextSteps.appendChild(li);
    });
  }

  const activeStageHeading = document.querySelector("[data-active-stage-title]");
  const activeStageBody = document.querySelector("[data-active-stage-body]");
  const activeStage = cardData.activeStage ?? null;
  if (activeStage && activeStageHeading && activeStageBody) {
    activeStageHeading.textContent = `${activeStage.name}`;
    activeStageBody.textContent = activeStage.objective ?? "";
  }

  if (!metrics) {
    return;
  }

  const usageValue = document.querySelector("[data-metric-usage-value]");
  if (usageValue) {
    usageValue.textContent = numberFormatter.format(metrics.usage.totalRequests ?? 0);
  }

  const usageDetail = document.querySelector("[data-metric-usage-detail]");
  if (usageDetail) {
    const usageParts = [];
    if (typeof metrics.usage.successRate === "number") {
      usageParts.push(`成功率 ${percentFormatter.format(metrics.usage.successRate)}`);
    }
    if (metrics.usage.window) {
      usageParts.push(`窗口 ${metrics.usage.window}`);
    }
    usageDetail.textContent = usageParts.length > 0 ? usageParts.join(" · ") : "暂无统计";
  }

  const costValue = document.querySelector("[data-metric-cost-value]");
  if (costValue) {
    costValue.textContent = currencyFormatter.format(metrics.cost.monthlyUsd ?? 0);
  }

  const costDetail = document.querySelector("[data-metric-cost-detail]");
  if (costDetail) {
    costDetail.textContent = typeof metrics.cost.dailyUsd === "number"
      ? `日均 ${currencyFormatter.format(metrics.cost.dailyUsd)}`
      : "日均 --";
  }

  const totalFailureCount = metrics.failures.reduce((sum, item) => sum + (Number.isFinite(item.count) ? item.count : 0), 0);
  const failureTotal = document.querySelector("[data-metric-failure-total]");
  if (failureTotal) {
    failureTotal.textContent = numberFormatter.format(totalFailureCount);
  }

  const failureDetail = document.querySelector("[data-metric-failure-detail]");
  if (failureDetail) {
    const topFailure = metrics.failures[0];
    failureDetail.textContent = topFailure ? `主要原因 ${topFailure.reason}` : "暂无失败记录";
  }

  const failureList = document.querySelector("[data-failure-reasons]");
  if (failureList) {
    failureList.innerHTML = "";
    const topFailures = metrics.failures.slice(0, 5);
    if (topFailures.length === 0) {
      const empty = document.createElement("li");
      empty.className = "metrics__empty";
      empty.textContent = "最近窗口内没有失败记录";
      failureList.appendChild(empty);
    } else {
      topFailures.forEach((failure) => {
        const item = document.createElement("li");
        const reason = document.createElement("span");
        reason.className = "metrics__reason";
        reason.textContent = failure.reason;
        const count = document.createElement("span");
        count.className = "metrics__count";
        count.textContent = numberFormatter.format(failure.count);
        item.append(reason, count);
        failureList.appendChild(item);
      });
    }
  }

  const metricsUpdated = document.querySelector("[data-metrics-updated]");
  if (metricsUpdated) {
    metricsUpdated.textContent = `最近更新：${formatUpdatedLabel(metrics.updatedAt)}`;
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

export function renderTests(tests, metricsInput = latestMetrics) {
  const list = document.querySelector("[data-test-list]");
  if (!list) return;
  list.innerHTML = "";

  const metrics = metricsInput ? normalizeMetrics(metricsInput) : latestMetrics;
  if (metrics && metrics !== latestMetrics) {
    latestMetrics = metrics;
  }

  const failingMap = new Map();
  if (metrics) {
    metrics.tests.failing.forEach((item) => {
      failingMap.set(item.id, item);
    });
  }

  (Array.isArray(tests) ? tests : []).forEach((test) => {
    if (!test) return;

    const li = document.createElement("li");
    const failureInfo = test.id ? failingMap.get(test.id) : undefined;
    if (failureInfo) {
      li.classList.add("test--failing");
    }

    const nameEl = document.createElement("strong");
    nameEl.textContent = test.name ?? test.id ?? "未命名测试";
    li.appendChild(nameEl);

    const descriptionEl = document.createElement("span");
    descriptionEl.textContent = test.description ?? "暂无描述";
    li.appendChild(descriptionEl);

    if (test.automated) {
      const automatedTag = document.createElement("span");
      automatedTag.className = "tag";
      automatedTag.textContent = "自动化";
      li.appendChild(automatedTag);
    }

    if (failureInfo) {
      const failureTag = document.createElement("span");
      failureTag.className = "tag tag--danger";
      failureTag.textContent = `失败 ${failureInfo.failures} 次`;
      li.appendChild(failureTag);

      if (failureInfo.reason) {
        const reasonEl = document.createElement("span");
        reasonEl.className = "test__reason";
        reasonEl.textContent = failureInfo.reason;
        li.appendChild(reasonEl);
      }
    }

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

async function handleMetricsRefresh() {
  if (typeof document === "undefined") {
    return;
  }

  const refreshButton = document.querySelector("[data-metrics-refresh]");
  const metricsCard = document.querySelector("[data-metrics-card]");

  if (refreshButton) {
    refreshButton.disabled = true;
  }
  if (metricsCard) {
    metricsCard.classList.add("is-loading");
  }

  try {
    const response = await fetchJson("/api/metrics");
    const normalized = response ? normalizeMetrics(response) : normalizeMetrics(latestMetrics ?? FALLBACK_METRICS);
    latestMetrics = normalized;
    if (latestCards) {
      renderSummary(latestCards, latestMetrics);
      renderTests(latestCards.tests, latestMetrics);
    }
  } finally {
    if (refreshButton) {
      refreshButton.disabled = false;
    }
    if (metricsCard) {
      metricsCard.classList.remove("is-loading");
    }
  }
}

function setupMetricsRefresh() {
  if (typeof document === "undefined") {
    return;
  }

  const refreshButton = document.querySelector("[data-metrics-refresh]");
  if (refreshButton && !metricsRefreshBound) {
    refreshButton.addEventListener("click", () => {
      handleMetricsRefresh();
    });
    metricsRefreshBound = true;
  }

  if (metricsTimer) {
    clearInterval(metricsTimer);
  }

  metricsTimer = setInterval(handleMetricsRefresh, METRICS_REFRESH_INTERVAL);
}

function formatUpdatedLabel(timestamp) {
  if (!timestamp) {
    return "--";
  }
  const date = typeof timestamp === "number" ? new Date(timestamp) : new Date(String(timestamp));
  if (Number.isNaN(date.getTime())) {
    return "--";
  }
  return updatedFormatter.format(date);
}

async function bootstrap() {
  const [status, roadmap, locales, configuration, metrics] = await Promise.all([
    fetchJson("/api/status"),
    fetchJson("/api/roadmap"),
    fetchJson("/api/localization/locales"),
    fetchJson("/api/configuration"),
    fetchJson("/api/metrics")
  ]);

  latestCards = buildStatusCards(status ?? FALLBACK_STATUS, roadmap ?? FALLBACK_ROADMAP);
  latestMetrics = normalizeMetrics(metrics ?? FALLBACK_METRICS);

  renderSummary(latestCards, latestMetrics);
  renderTimeline(latestCards.timeline);
  renderTests(latestCards.tests, latestMetrics);
  renderLocales(formatLocaleOptions(locales ?? FALLBACK_LOCALES));
  renderLanguages(configuration?.supportedLanguages ?? FALLBACK_LANGUAGES);
  setupMetricsRefresh();
}

if (typeof document !== "undefined" && typeof document.addEventListener === "function") {
  document.addEventListener("DOMContentLoaded", bootstrap);
}
