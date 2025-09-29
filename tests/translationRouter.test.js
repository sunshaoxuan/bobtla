import test from "node:test";
import assert from "node:assert/strict";
import { MockModelProvider } from "../src/models/modelProvider.js";
import { TranslationRouter } from "../src/services/translationRouter.js";
import { LanguageDetector } from "../src/services/languageDetector.js";
import { GlossaryManager } from "../src/services/glossaryManager.js";
import { BudgetGuard } from "../src/services/budgetGuard.js";
import { AuditLogger } from "../src/services/auditLogger.js";

test("router applies glossary and falls back on failure", async () => {
  const failingProvider = new MockModelProvider({
    id: "primary",
    costPerCharUsd: 0.0001,
    latencyTargetMs: 100,
    behavior: { failures: 1 }
  });
  const successProvider = new MockModelProvider({
    id: "backup",
    costPerCharUsd: 0.00005,
    latencyTargetMs: 200,
    behavior: { translationPrefix: "[ok]", detectedLanguage: "en", confidence: 0.9 }
  });
  const glossary = new GlossaryManager();
  glossary.upsertEntry("tenant", "cpu", "中央处理器", { strategy: "mixed" });
  const detector = new LanguageDetector([successProvider]);
  const budget = new BudgetGuard({ dailyBudgetUsd: 1 });
  const audit = new AuditLogger({});
  const router = new TranslationRouter({
    providers: [failingProvider, successProvider],
    glossaryManager: glossary,
    detector,
    budgetGuard: budget,
    auditLogger: audit,
    retry: 0
  });

  const result = await router.translate({
    text: "CPU ready",
    targetLanguage: "zh-Hans",
    tenantId: "tenantA",
    userId: "userA",
    channelId: "channelA"
  });

  assert.equal(result.text.includes("中央处理器"), true);
  assert.equal(result.modelId, "backup");
  assert.equal(audit.records.length, 1);
});

test("router enforces budget", async () => {
  const provider = new MockModelProvider({
    id: "primary",
    costPerCharUsd: 1,
    latencyTargetMs: 100,
    behavior: { translationPrefix: "[high cost]" }
  });
  const router = new TranslationRouter({
    providers: [provider],
    glossaryManager: new GlossaryManager(),
    detector: new LanguageDetector([provider]),
    budgetGuard: new BudgetGuard({ dailyBudgetUsd: 0.5 })
  });

  await assert.rejects(
    () =>
      router.translate({
        text: "Expensive call",
        targetLanguage: "es",
        tenantId: "tenantA",
        userId: "userA"
      }),
    /Daily translation budget exceeded/
  );
});
