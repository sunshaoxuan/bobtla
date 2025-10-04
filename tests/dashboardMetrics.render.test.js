import test from "node:test";
import assert from "node:assert/strict";
import { JSDOM } from "jsdom";
import { renderSummary, renderTests, normalizeMetrics } from "../src/webapp/app.js";

const numberFormatter = new Intl.NumberFormat("zh-CN");

test("renderSummary populates metrics blocks", (t) => {
  const dom = new JSDOM(`
    <main>
      <div class="progress" data-overall-progress><span>0%</span></div>
      <p data-overall-text></p>
      <div class="progress" data-frontend-progress><span>0%</span></div>
      <p data-frontend-text></p>
      <ul data-readiness></ul>
      <ul data-stage-five-diagnostics></ul>
      <p data-stage-five-failure hidden></p>
      <ul data-next-steps></ul>
      <h3 data-active-stage-title></h3>
      <p data-active-stage-body></p>
      <div class="metrics__grid">
        <div data-metric-usage>
          <strong data-metric-usage-value></strong>
          <span data-metric-usage-detail></span>
        </div>
        <div data-metric-cost>
          <strong data-metric-cost-value></strong>
          <span data-metric-cost-detail></span>
        </div>
        <div data-metric-failure-summary>
          <strong data-metric-failure-total></strong>
          <span data-metric-failure-detail></span>
        </div>
      </div>
      <ul data-failure-reasons></ul>
      <p data-metrics-updated></p>
    </main>
  `);

  globalThis.window = dom.window;
  globalThis.document = dom.window.document;
  t.after(() => {
    dom.window.close();
    delete globalThis.window;
    delete globalThis.document;
  });

  const cards = {
    overallPercent: 80,
    frontend: { completionPercent: 80, dataPlaneReady: true, uiImplemented: true, integrationReady: true },
    nextSteps: ["密钥映射 Runbook"],
    activeStage: { name: "阶段 5", objective: "上线准备" },
    stageFiveDiagnostics: {
      failureReason: "最近一次冒烟 40 小时前，需要重新执行",
      items: [
        { id: "hmac", label: "HMAC 配置", ready: true, message: "HMAC 回退已关闭" },
        { id: "graph", label: "Graph 作用域", ready: true, message: "Graph 作用域已就绪" },
        { id: "smoke", label: "冒烟测试", ready: false, message: "最近一次冒烟 40 小时前，需要重新执行" }
      ]
    }
  };

  const metrics = normalizeMetrics({
    usage: { totalRequests: 12345, successRate: 0.987, window: "24h" },
    cost: { monthlyUsd: 432.1, dailyUsd: 14.32 },
    failures: [
      { reason: "RateLimitExceeded", count: 12 },
      { reason: "AuthenticationFailed", count: 5 }
    ],
    updatedAt: "2024-03-18T10:00:00Z"
  });

  renderSummary(cards, metrics);

  const usageValue = document.querySelector("[data-metric-usage-value]");
  assert.equal(usageValue.textContent, numberFormatter.format(12345));

  const costDetail = document.querySelector("[data-metric-cost-detail]");
  assert.ok(costDetail.textContent.includes("日均"));

  const failureTotal = document.querySelector("[data-metric-failure-total]");
  assert.equal(failureTotal.textContent, numberFormatter.format(17));

  const failureItems = document.querySelectorAll("[data-failure-reasons] li");
  assert.equal(failureItems.length, 2);
  assert.equal(failureItems[0].querySelector(".metrics__reason").textContent, "RateLimitExceeded");

  const diagnosticItems = document.querySelectorAll("[data-stage-five-diagnostics] li");
  assert.equal(diagnosticItems.length, 3);
  assert.ok(diagnosticItems[2].textContent.includes("冒烟测试"));

  const diagnosticFailure = document.querySelector("[data-stage-five-failure]");
  assert.equal(diagnosticFailure.hidden, false);
  assert.ok(diagnosticFailure.textContent.includes("阻塞原因"));

  const updatedLabel = document.querySelector("[data-metrics-updated]").textContent;
  assert.match(updatedLabel, /^最近更新：/);
});

test("renderTests marks failing suites with metrics context", (t) => {
  const dom = new JSDOM(`<ul data-test-list></ul>`);
  globalThis.window = dom.window;
  globalThis.document = dom.window.document;
  t.after(() => {
    dom.window.close();
    delete globalThis.window;
    delete globalThis.document;
  });

  const tests = [
    { id: "router", name: "TranslationRouterTests", description: "验证回退链路", automated: true },
    { id: "dashboard", name: "Dashboard Tests", description: "仪表盘快照" }
  ];

  const metrics = normalizeMetrics({
    tests: {
      failing: [
        { id: "router", failures: 3, reason: "限流策略回退失败" }
      ]
    }
  });

  renderTests(tests, metrics);

  const failingItems = document.querySelectorAll(".test--failing");
  assert.equal(failingItems.length, 1);
  const failing = failingItems[0];
  assert.equal(failing.querySelector(".tag--danger").textContent, "失败 3 次");
  assert.ok(failing.querySelector(".test__reason").textContent.includes("限流策略"));

  const testItems = document.querySelectorAll("[data-test-list] li");
  assert.equal(testItems[1].classList.contains("test--failing"), false);
});
