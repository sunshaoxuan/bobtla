import test from "node:test";
import assert from "node:assert/strict";
import { groupStagesForTimeline, buildStatusCards, formatLocaleOptions } from "../src/webapp/viewModel.js";

test("groupStagesForTimeline marks active stage and progress", () => {
  const stages = [
    { id: "phase1", name: "阶段 1：平台基线", completed: true },
    { id: "phase4", name: "阶段 4：前端体验", completed: false }
  ];

  const grouped = groupStagesForTimeline(stages, "phase4");
  assert.equal(grouped.length, 2);
  assert.equal(grouped[0].progress, 100);
  assert.equal(grouped[1].isActive, true);
  assert.equal(grouped[1].progress, 50);
});

test("buildStatusCards merges roadmap and status information", () => {
  const status = {
    currentStageId: "phase4",
    overallCompletionPercent: 60,
    frontend: { completionPercent: 55, dataPlaneReady: true, uiImplemented: true, integrationReady: false },
    nextSteps: ["完善设置页"],
    stages: [{ id: "phase1", name: "阶段 1", completed: true }]
  };
  const roadmap = {
    activeStageId: "phase4",
    stages: [
      { id: "phase1", name: "阶段 1", completed: true, objective: "完成基础" },
      { id: "phase4", name: "阶段 4", completed: false, objective: "交付前端" }
    ],
    tests: [{ id: "dashboard", name: "dashboardViewModel.test.js", description: "verify" }]
  };

  const cards = buildStatusCards(status, roadmap);
  assert.equal(cards.timeline.length, 2);
  assert.equal(cards.activeStage.name, "阶段 4");
  assert.equal(cards.frontend.uiImplemented, true);
  assert.equal(cards.tests[0].id, "dashboard");
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
