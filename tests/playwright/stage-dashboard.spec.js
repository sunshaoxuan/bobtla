import { test, expect } from "@playwright/test";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, "../..");
const stageRoot = path.join(repoRoot, "src", "TlaPlugin", "wwwroot");

function readStageFile(relativePath) {
  const absolute = path.join(stageRoot, relativePath);
  const normalized = path.normalize(absolute);
  if (!normalized.startsWith(stageRoot)) {
    throw new Error(`Attempted to read outside stage root: ${relativePath}`);
  }
  return readFileSync(normalized);
}

function resolveContentType(filePath) {
  const ext = path.extname(filePath).toLowerCase();
  switch (ext) {
    case ".html":
      return "text/html";
    case ".css":
      return "text/css";
    case ".js":
      return "application/javascript";
    case ".json":
      return "application/json";
    default:
      return "application/octet-stream";
  }
}

async function serveStageStatic(route) {
  const url = new URL(route.request().url());
  const relative = url.pathname.replace(/^\//, "");
  try {
    const body = readStageFile(relative);
    await route.fulfill({
      status: 200,
      headers: { "content-type": resolveContentType(relative) },
      body
    });
  } catch (error) {
    await route.fulfill({ status: 404, body: "not found" });
  }
}

test.describe("Stage dashboard caching", () => {
  test("falls back to built-in status snapshot when the API is unavailable", async ({ page }) => {
    await page.addInitScript(() => {
      window.localStorage?.clear?.();
    });

    const roadmapPayload = {
      activeStageId: "phase5",
      stages: [
        { id: "phase5", name: "阶段 5：上线准备", objective: "串联 Stage 数据", completed: false }
      ],
      tests: []
    };

    const localesPayload = [{ id: "zh-CN", displayName: "简体中文", isDefault: true }];
    const configurationPayload = { supportedLanguages: ["zh-CN", "en-US"] };
    const metricsPayload = {
      usage: { totalRequests: 64, successRate: 0.94, window: "24h" },
      cost: { monthlyUsd: 12.5, dailyUsd: 0.42 },
      failures: [],
      updatedAt: "2024-05-18T11:00:00Z"
    };

    await page.route("**/api/status", async (route) => {
      await route.fulfill({ status: 503, body: "status offline" });
    });

    await page.route("**/api/roadmap", async (route) => {
      await route.fulfill({
        status: 200,
        headers: { "content-type": "application/json" },
        body: JSON.stringify(roadmapPayload)
      });
    });

    await page.route("**/api/localization/locales", async (route) => {
      await route.fulfill({
        status: 200,
        headers: { "content-type": "application/json" },
        body: JSON.stringify(localesPayload)
      });
    });

    await page.route("**/api/configuration", async (route) => {
      await route.fulfill({
        status: 200,
        headers: { "content-type": "application/json" },
        body: JSON.stringify(configurationPayload)
      });
    });

    await page.route("**/api/metrics", async (route) => {
      await route.fulfill({
        status: 200,
        headers: { "content-type": "application/json" },
        body: JSON.stringify(metricsPayload)
      });
    });

    await page.route("http://stage.test/**", serveStageStatic);

    await page.goto("http://stage.test/webapp/index.html");

    const statusUpdated = page.locator("[data-status-updated]");
    await expect(statusUpdated).toHaveAttribute("data-source", "fallback");
    await expect(statusUpdated).toContainText("（内置数据）");
    await expect(page.locator("[data-overall-text]")).toContainText("80%");
    await expect(page.locator("[data-next-steps] li").first()).toContainText("Runbook");
    await expect(page.locator("[data-stage-five-diagnostics]")).toContainText("Graph 作用域缺失");
  });

  test("reuses cached status data when the network fails", async ({ page }) => {
    await page.addInitScript(() => {
      window.localStorage?.clear?.();
    });

    const statusPayload = {
      currentStageId: "phase5",
      overallCompletionPercent: 88,
      stages: [
        { id: "phase1", completed: true },
        { id: "phase5", completed: false, name: "阶段 5：上线准备" }
      ],
      stageFiveDiagnostics: {
        stageReady: true,
        smokeTestRecent: true,
        lastSmokeSuccess: "2024-05-18T10:00:00Z",
        failureReason: null
      },
      frontend: { completionPercent: 92 },
      updatedAt: "2024-05-18T10:00:00Z"
    };

    const roadmapPayload = {
      activeStageId: "phase5",
      stages: [
        { id: "phase5", name: "阶段 5：上线准备", objective: "串联 Stage 数据" }
      ],
      tests: []
    };

    const localesPayload = [{ id: "zh-CN", displayName: "简体中文", isDefault: true }];
    const configurationPayload = { supportedLanguages: ["zh-CN", "en-US"] };
    const metricsPayload = {
      usage: { totalRequests: 123, successRate: 0.95, window: "24h" },
      cost: { monthlyUsd: 42.5, dailyUsd: 1.7 },
      failures: [],
      updatedAt: "2024-05-18T09:58:00Z"
    };

    let statusCalls = 0;

    await page.route("**/api/status", async (route) => {
      statusCalls += 1;
      if (statusCalls === 1) {
        await route.fulfill({
          status: 200,
          headers: { "content-type": "application/json" },
          body: JSON.stringify(statusPayload)
        });
        return;
      }
      await route.fulfill({ status: 503, body: "status unavailable" });
    });

    await page.route("**/api/roadmap", async (route) => {
      await route.fulfill({
        status: 200,
        headers: { "content-type": "application/json" },
        body: JSON.stringify(roadmapPayload)
      });
    });

    await page.route("**/api/localization/locales", async (route) => {
      await route.fulfill({
        status: 200,
        headers: { "content-type": "application/json" },
        body: JSON.stringify(localesPayload)
      });
    });

    await page.route("**/api/configuration", async (route) => {
      await route.fulfill({
        status: 200,
        headers: { "content-type": "application/json" },
        body: JSON.stringify(configurationPayload)
      });
    });

    await page.route("**/api/metrics", async (route) => {
      await route.fulfill({
        status: 200,
        headers: { "content-type": "application/json" },
        body: JSON.stringify(metricsPayload)
      });
    });

    await page.route("http://stage.test/**", serveStageStatic);

    await page.goto("http://stage.test/webapp/index.html");

    await expect(page.locator("[data-overall-text]")).toContainText("88%");
    await expect(page.locator("[data-status-updated]")).toHaveAttribute("data-source", "network");

    await page.reload();

    await expect(page.locator("[data-overall-text]")).toContainText("88%");
    const statusUpdated = page.locator("[data-status-updated]");
    await expect(statusUpdated).toHaveAttribute("data-source", "cache");
    await expect(statusUpdated).toContainText("（缓存）");
    expect(statusCalls).toBeGreaterThanOrEqual(2);
  });

  test("falls back to cached metrics during refresh failures", async ({ page }) => {
    await page.addInitScript(() => {
      window.localStorage?.clear?.();
    });

    const statusPayload = {
      currentStageId: "phase5",
      overallCompletionPercent: 85,
      stages: [
        { id: "phase5", name: "阶段 5：上线准备", completed: false }
      ],
      stageFiveDiagnostics: {
        stageReady: false,
        smokeTestRecent: false,
        lastSmokeSuccess: null,
        failureReason: "missing"
      },
      frontend: { completionPercent: 80 },
      updatedAt: "2024-05-18T10:05:00Z"
    };

    const roadmapPayload = {
      activeStageId: "phase5",
      stages: [{ id: "phase5", name: "阶段 5：上线准备", objective: "完成 Stage 仪表盘" }],
      tests: []
    };

    const localesPayload = [{ id: "ja-JP", displayName: "日本語", isDefault: true }];
    const configurationPayload = { supportedLanguages: ["ja-JP", "en-US", "zh-CN"] };

    const metricsPayload = {
      usage: { totalRequests: 4321, successRate: 0.72, window: "24h" },
      cost: { monthlyUsd: 128.4, dailyUsd: 5.12 },
      failures: [
        { reason: "AuthenticationFailed", count: 2 },
        { reason: "Budget", count: 1 }
      ],
      updatedAt: "2024-05-18T10:06:00Z"
    };

    let metricsCalls = 0;
    let failMetrics = false;

    await page.route("**/api/status", async (route) => {
      await route.fulfill({
        status: 200,
        headers: { "content-type": "application/json" },
        body: JSON.stringify(statusPayload)
      });
    });

    await page.route("**/api/roadmap", async (route) => {
      await route.fulfill({
        status: 200,
        headers: { "content-type": "application/json" },
        body: JSON.stringify(roadmapPayload)
      });
    });

    await page.route("**/api/localization/locales", async (route) => {
      await route.fulfill({
        status: 200,
        headers: { "content-type": "application/json" },
        body: JSON.stringify(localesPayload)
      });
    });

    await page.route("**/api/configuration", async (route) => {
      await route.fulfill({
        status: 200,
        headers: { "content-type": "application/json" },
        body: JSON.stringify(configurationPayload)
      });
    });

    await page.route("**/api/metrics", async (route) => {
      metricsCalls += 1;
      if (failMetrics && metricsCalls > 1) {
        await route.fulfill({ status: 503, body: "metrics failure" });
        return;
      }
      await route.fulfill({
        status: 200,
        headers: { "content-type": "application/json" },
        body: JSON.stringify(metricsPayload)
      });
    });

    await page.route("http://stage.test/**", serveStageStatic);

    await page.goto("http://stage.test/webapp/index.html");

    await expect(page.locator("[data-metric-usage-value]")).toHaveText("4,321");
    await expect(page.locator("[data-metrics-updated]")).toHaveAttribute("data-source", "network");

    failMetrics = true;

    const refreshButton = page.locator("[data-metrics-refresh]");
    await refreshButton.click();

    const metricsUpdated = page.locator("[data-metrics-updated]");
    await expect(metricsUpdated).toHaveAttribute("data-source", "cache");
    await expect(metricsUpdated).toContainText("（缓存）");
    await expect(page.locator("[data-metric-usage-value]")).toHaveText("4,321");
    await expect(page.locator("[data-metric-cost-value]")).toHaveText("$128.40");
    expect(metricsCalls).toBeGreaterThanOrEqual(2);
  });

  test("uses built-in metrics snapshot when the metrics API is offline", async ({ page }) => {
    await page.addInitScript(() => {
      window.localStorage?.clear?.();
    });

    const statusPayload = {
      currentStageId: "phase5",
      overallCompletionPercent: 88,
      stages: [
        { id: "phase1", completed: true },
        { id: "phase5", completed: false, name: "阶段 5：上线准备" }
      ],
      stageFiveDiagnostics: {
        stageReady: true,
        smokeTestRecent: true,
        lastSmokeSuccess: "2024-05-18T10:00:00Z",
        failureReason: null
      },
      frontend: { completionPercent: 92 },
      updatedAt: "2024-05-18T10:00:00Z"
    };

    const roadmapPayload = {
      activeStageId: "phase5",
      stages: [
        { id: "phase5", name: "阶段 5：上线准备", objective: "串联 Stage 数据" }
      ],
      tests: []
    };

    const localesPayload = [{ id: "zh-CN", displayName: "简体中文", isDefault: true }];
    const configurationPayload = { supportedLanguages: ["zh-CN", "en-US"] };

    await page.route("**/api/status", async (route) => {
      await route.fulfill({
        status: 200,
        headers: { "content-type": "application/json" },
        body: JSON.stringify(statusPayload)
      });
    });

    await page.route("**/api/roadmap", async (route) => {
      await route.fulfill({
        status: 200,
        headers: { "content-type": "application/json" },
        body: JSON.stringify(roadmapPayload)
      });
    });

    await page.route("**/api/localization/locales", async (route) => {
      await route.fulfill({
        status: 200,
        headers: { "content-type": "application/json" },
        body: JSON.stringify(localesPayload)
      });
    });

    await page.route("**/api/configuration", async (route) => {
      await route.fulfill({
        status: 200,
        headers: { "content-type": "application/json" },
        body: JSON.stringify(configurationPayload)
      });
    });

    await page.route("**/api/metrics", async (route) => {
      await route.fulfill({ status: 503, body: "metrics offline" });
    });

    await page.route("http://stage.test/**", serveStageStatic);

    await page.goto("http://stage.test/webapp/index.html");

    await expect(page.locator("[data-metric-usage-value]")).toHaveText("15,872");
    await expect(page.locator("[data-metric-cost-value]")).toHaveText("US$312.45");
    const metricsUpdated = page.locator("[data-metrics-updated]");
    await expect(metricsUpdated).toHaveAttribute("data-source", "fallback");
    await expect(metricsUpdated).toContainText("（内置数据）");
    await expect(page.locator("[data-failure-reasons] li").first()).toContainText("RateLimitExceeded");
  });
});
