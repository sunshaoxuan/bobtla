import test from "node:test";
import assert from "node:assert/strict";
import { groupStagesForTimeline, buildStatusCards, formatLocaleOptions } from "../src/webapp/viewModel.js";

test("groupStagesForTimeline marks active stage and progress", () => {
  const stages = [
    { id: "phase1", name: "阶段 1：平台基线", completed: true },
    { id: "phase4", name: "阶段 4：前端体验", completed: true },
    { id: "phase5", name: "阶段 5：上线准备", completed: false }
  ];

  const grouped = groupStagesForTimeline(stages, "phase5");
  assert.equal(grouped.length, 3);
  assert.equal(grouped[0].progress, 100);
  assert.equal(grouped[1].progress, 100);
  assert.equal(grouped[2].isActive, true);
  assert.equal(grouped[2].progress, 50);
});

test("buildStatusCards merges roadmap and status information", () => {
  const status = {
    currentStageId: "phase5",
    overallCompletionPercent: 100,
    frontend: { completionPercent: 100, dataPlaneReady: true, uiImplemented: true, integrationReady: true },
    nextSteps: ["发布清单"],
    stageFiveDiagnostics: {
      stageReady: true,
      hmacConfigured: true,
      hmacStatus: "HMAC 回退已关闭",
      graphScopesValid: true,
      graphScopesStatus: "Graph 作用域已就绪",
      smokeTestRecent: true,
      smokeStatus: "冒烟 2 小时前通过",
      failureReason: "",
      lastSmokeSuccess: "2024-03-20T02:00:00Z"
    },
    stages: [
      { id: "phase1", name: "阶段 1", completed: true },
      { id: "phase5", name: "阶段 5", completed: true }
    ]
  };
  const roadmap = {
    activeStageId: "phase5",
    stages: [
      { id: "phase1", name: "阶段 1", completed: true, objective: "完成基础" },
      { id: "phase4", name: "阶段 4", completed: true, objective: "交付前端" },
      { id: "phase5", name: "阶段 5", completed: false, objective: "上线准备" }
    ],
    tests: [{ id: "dashboard", name: "dashboardViewModel.test.js", description: "verify" }]
  };

  const cards = buildStatusCards(status, roadmap);
  assert.equal(cards.timeline.length, 3);
  assert.equal(cards.activeStage.name, "阶段 5");
  assert.equal(cards.frontend.uiImplemented, true);
  assert.equal(cards.tests[0].id, "dashboard");
  const phase5 = cards.timeline.find((stage) => stage.id === "phase5");
  assert.ok(phase5?.completed);
  assert.equal(phase5.progress, 100);
  assert.deepEqual(cards.nextSteps, ["发布清单"]);
  assert.equal(cards.overallPercent, 100);
  assert.equal(cards.stageFiveDiagnostics.items.length, 3);
  assert.equal(cards.stageFiveDiagnostics.failureReason, "");
  assert.equal(cards.stageFiveDiagnostics.items[0].ready, true);
});

test("formatLocaleOptions sorts locales alphabetically", () => {
  const locales = [
    { id: "zh-CN", displayName: "简体中文" },
    { id: "ja-JP", displayName: "日本語", isDefault: true }
  ];

  const result = formatLocaleOptions(locales);
  assert.deepEqual(result.map((item) => item.id), ["ja-JP", "zh-CN"]);
  assert.equal(result[0].isDefault, true);
});
